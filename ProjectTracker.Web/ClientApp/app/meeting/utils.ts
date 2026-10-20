import type { Milestone, Project } from './types'
import { getProjectTimelinePosition } from '@/lib/timeline'

export interface DelaySummary {
  weeks: number
  delayedMilestones: number
  milestoneNames: string[]
}

function emptyDelaySummary(): DelaySummary {
  return { weeks: 0, delayedMilestones: 0, milestoneNames: [] }
}

// Note: هذا الحساب يبقي الأسبوع الفعلي مفتوحًا بعد نهاية المشروع حتى نستطيع قياس التأخير الحقيقي.
function getElapsedProjectWeek(project: Project | null, today: Date) {
  return getProjectTimelinePosition(project, today).elapsedWeek ?? 0
}

// Note: نسبة الإنجاز العامة تحسب من متوسط تقدم المراحل المعروضة في صفحة الاجتماع.
function getOverallProgress(milestones: Milestone[]) {
  const withTasks = milestones.filter(milestone => milestone.tasks_count === undefined || milestone.tasks_count > 0)
  if (withTasks.length === 0) return 0
  const totalWeight = withTasks.reduce((sum, milestone) => sum + Math.max(1, milestone.week_end - milestone.week_start + 1), 0)
  return Math.round(withTasks.reduce(
    (sum, milestone) => sum + (milestone.progress || 0) * Math.max(1, milestone.week_end - milestone.week_start + 1),
    0
  ) / totalWeight)
}

// Note: عدد المراحل المكتملة يستخدم في كرت المؤشرات العلوي بجانب كرت التأخير.
function getCompletedMilestones(milestones: Milestone[]) {
  return milestones.filter(milestone => (milestone.progress || 0) >= 100).length
}

export function getProjectDashboardData({
  project,
  milestones,
  today,
}: {
  project: Project | null
  milestones: Milestone[]
  today: Date
}) {
  // Note: هذه الدالة تجمع بيانات داشبورد الاجتماع فقط، بدون تغيير fetch أو منطق العرض.
  const overallProgress = getOverallProgress(milestones)
  const delaySummary = getDelaySummary({ project, milestones, progress: overallProgress, today })

  return {
    overallProgress,
    delaySummary,
    completedMilestones: getCompletedMilestones(milestones),
  }
}

// Note: حساب التأخير يعتمد على أكثر مرحلة تجاوزت أسبوع نهايتها ولم تكتمل، ولا يرجع لتاريخ النهاية إلا عند غياب المراحل.
function getDelaySummary({
  project,
  milestones,
  progress,
  today,
}: {
  project: Project | null
  milestones: Milestone[]
  progress: number
  today: Date
}): DelaySummary {
  if (progress >= 100 || project?.status === 'completed') {
    return emptyDelaySummary()
  }

  const elapsedWeek = getElapsedProjectWeek(project, today)
  const delayedMilestones = milestones
    .map(milestone => ({
      ...milestone,
      delayWeeks: (milestone.progress || 0) < 100 ? elapsedWeek - milestone.week_end : 0,
    }))
    .filter(milestone => milestone.delayWeeks > 0)

  if (delayedMilestones.length > 0) {
    const mostDelayedMilestone = delayedMilestones.reduce((max, milestone) =>
      milestone.delayWeeks > max.delayWeeks ? milestone : max,
    delayedMilestones[0])

    return {
      weeks: mostDelayedMilestone.delayWeeks,
      delayedMilestones: delayedMilestones.length,
      milestoneNames: delayedMilestones.map(milestone => milestone.name),
    }
  }

  if (milestones.length > 0 || !project?.end_date) return emptyDelaySummary()

  const endDate = new Date(project.end_date)
  if (Number.isNaN(endDate.getTime())) return emptyDelaySummary()

  const todayStart = new Date(today.getFullYear(), today.getMonth(), today.getDate())
  const endStart = new Date(endDate.getFullYear(), endDate.getMonth(), endDate.getDate())
  const diffDays = Math.ceil((todayStart.getTime() - endStart.getTime()) / (24 * 60 * 60 * 1000))

  return { weeks: diffDays > 0 ? Math.ceil(diffDays / 7) : 0, delayedMilestones: 0, milestoneNames: [] }
}

/**
 * الإنجاز المخطَّط (Planned %): خط أساس بسيط = نسبة الوقت المنقضي من مدة
 * المشروع الكلية (أسبوع منقضٍ / إجمالي الأسابيع). هذا تقريب خطي قياسي في
 * إدارة المشاريع، وليس منحنى S حقيقيًا، لكنه كافٍ لعرض "أين كان يجب أن نكون".
 */
export function getPlannedProgress(project: Project | null, today: Date): number {
  if (!project?.start_date || !project.end_date) return 0
  const start = new Date(project.start_date)
  const end = new Date(project.end_date)
  if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime())) return 0
  const elapsedDays = Math.max(0, today.getTime() - start.getTime())
  const totalDays = end.getTime() - start.getTime()
  if (totalDays <= 0) return today >= end ? 100 : 0
  const ratio = Math.min(elapsedDays, totalDays) / totalDays
  return Math.round(Math.max(0, Math.min(1, ratio)) * 100)
}

/** الانحراف = الفعلي - المخطَّط. سالب يعني تأخرًا عن الخطة. */
export function getProgressDeviation(actualProgress: number, plannedProgress: number): number {
  return actualProgress - plannedProgress
}

export type DeviationTone = 'success' | 'warning' | 'error'

/** حدود لونية لتصنيف الانحراف: ضمن الخطة / يحتاج متابعة / متأخر. */
export function getDeviationTone(deviation: number): DeviationTone {
  if (deviation >= -5) return 'success'
  if (deviation >= -15) return 'warning'
  return 'error'
}

