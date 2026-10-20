
import type { AppSettings, MeetingAction, ProjectUpdate } from './types'

export type ExecutiveStatus = 'on_track' | 'needs_attention' | 'delayed' | 'stalled' | 'completed' | 'unknown'

export const EXECUTIVE_STATUS_LABEL: Record<ExecutiveStatus, string> = {
  on_track: 'ضمن الخطة',
  needs_attention: 'يحتاج متابعة',
  delayed: 'متأخر',
  stalled: 'متعثر',
  completed: 'مكتمل',
  unknown: 'غير متوفر',
}

/** ترتيب عرض الحالات في الرسم والتقارير. */
export const EXECUTIVE_STATUS_ORDER: ExecutiveStatus[] = ['on_track', 'needs_attention', 'delayed', 'stalled', 'completed', 'unknown']

/* ─────────────────────────── الدعم المطلوب ─────────────────────────── */

export type SupportKind = 'support' | 'decision' | 'escalation'
export type SupportSource = 'weekly_update' | 'meeting_action' | 'phase_note'

export const SUPPORT_KIND_LABEL: Record<SupportKind, string> = {
  support: 'طلب دعم',
  decision: 'اعتماد قرار',
  escalation: 'تصعيد',
}

export const SUPPORT_SOURCE_LABEL: Record<SupportSource, string> = {
  weekly_update: 'التحديث الأسبوعي',
  meeting_action: 'إجراءات الاجتماع',
  phase_note: 'ملاحظة المرحلة',
}

export const SUPPORT_STATUS_LABEL: Record<string, string> = {
  open: 'مفتوح',
  new: 'جديد',
  in_progress: 'قيد التنفيذ',
  under_review: 'قيد الدراسة',
}

export interface SupportRequest {
  id: string
  kind: SupportKind
  source: SupportSource
  description: string
  /** الجهة المعنية بتقديم الدعم أو اتخاذ القرار، أو null إن لم تُسجَّل. */
  party: string | null
  /** تاريخ الطلب YYYY-MM-DD، أو null (ملاحظات المراحل بلا تاريخ). */
  requestedAt: string | null
  dueDate: string | null
  status: string
  /** اسم المرحلة لملاحظات الدعم. */
  milestoneName: string | null
}

type UpdateSupportFields = Pick<
  ProjectUpdate,
  'id' | 'reporting_date' | 'support_required' | 'support_from' | 'support_due_date' | 'decision_required' | 'decision_from' | 'decision_needed_by' | 'decision_status'
>
type ActionSupportFields = Pick<MeetingAction, 'id' | 'action_type' | 'description' | 'supporting_department' | 'meeting_date' | 'due_date' | 'status'>

/**
 * يجمع طلبات الدعم المفتوحة لمشروع من مصادره الفعلية الثلاثة:
 *   • آخر تحديث أسبوعي: support_required، و decision_required ما لم يكن decision_status = taken.
 *   • إجراءات الاجتماع المفتوحة (new/in_progress) من نوع دعم أو قرار أو تصعيد.
 *   • ملاحظات «الدعم» على المراحل غير المكتملة (المرحلة المكتملة لم يعد دعمها قائمًا).
 * لا يوجد في قاعدة البيانات تصنيف لنوع الدعم (مالي/مورد/…)، فيُعرض النوع حسب المصدر فقط.
 */
export function supportRequestsFrom(input: {
  latestUpdate: UpdateSupportFields | null
  actions: ActionSupportFields[]
  supportNotes: { id: string; content: string; milestoneName: string | null }[]
}): SupportRequest[] {
  const requests: SupportRequest[] = []
  const u = input.latestUpdate
  if (u?.decision_required?.trim() && u.decision_status !== 'taken') {
    requests.push({
      id: `update-decision-${u.id}`,
      kind: 'decision',
      source: 'weekly_update',
      description: u.decision_required.trim(),
      party: u.decision_from?.trim() || null,
      requestedAt: u.reporting_date || null,
      dueDate: u.decision_needed_by,
      status: u.decision_status ?? 'new',
      milestoneName: null,
    })
  }
  if (u?.support_required?.trim()) {
    requests.push({
      id: `update-support-${u.id}`,
      kind: 'support',
      source: 'weekly_update',
      description: u.support_required.trim(),
      party: u.support_from?.trim() || null,
      requestedAt: u.reporting_date || null,
      dueDate: u.support_due_date,
      status: 'open',
      milestoneName: null,
    })
  }
  const openActions = input.actions
    .filter(a => (a.status === 'new' || a.status === 'in_progress') && (a.action_type === 'support' || a.action_type === 'decision' || a.action_type === 'escalation'))
    .sort((a, b) => (a.due_date ?? '9999').localeCompare(b.due_date ?? '9999'))
  for (const a of openActions) {
    requests.push({
      id: `action-${a.id}`,
      kind: a.action_type as SupportKind,
      source: 'meeting_action',
      description: a.description,
      party: a.supporting_department?.trim() || null,
      requestedAt: a.meeting_date || null,
      dueDate: a.due_date,
      status: a.status,
      milestoneName: null,
    })
  }
  for (const note of input.supportNotes) {
    const content = note.content?.trim()
    if (!content) continue
    requests.push({
      id: `note-${note.id}`,
      kind: 'support',
      source: 'phase_note',
      description: content,
      party: null,
      requestedAt: null,
      dueDate: null,
      status: 'open',
      milestoneName: note.milestoneName,
    })
  }
  return requests
}

/* ─────────────────────────── تصنيف المشروع ─────────────────────────── */

export interface ExecutiveProject {
  id: string
  name: string
  description: string | null
  project_code: string | null
  department: string | null
  status: string
  start_date: string | null
  end_date: string | null
  total_weeks: number | null
  managers: string[]
  execStatus: ExecutiveStatus
  actualProgress: number | null
  plannedProgress: number | null
  plannedEstimated: boolean
  variance: number | null
  daysToEnd: number | null
  isOverdue: boolean
  currentMilestoneName: string | null
  tasksCount: number
  delayedTasks: number
  supportRequests: SupportRequest[]
  needsSupport: boolean
  /** غير مكتمل وينتهي خلال nearDeadlineDays (من اليوم حتى الموعد). */
  nearDeadline: boolean
  /** ملتزم بالجدول: انحراف ضمن حد «ضمن الخطة» وبلا تجاوز للموعد. null = مكتمل أو غير قابل للحساب. */
  scheduleCompliant: boolean | null
  attentionReasons: string[]
  /** أولوية التنبيه (1 أعلى)، أو null إن لم يستدعِ المشروع الانتباه. */
  attentionScore: number | null
}

/* ─────────────────────────── المؤشرات ─────────────────────────── */

export interface ExecutiveKpis {
  total: number
  completed: number
  onTrack: number
  needsAttention: number
  delayed: number
  stalled: number
  unknown: number
  needsSupport: number
  nearDeadline: number
  /** مجموع المهام المتأخرة (لم تكتمل وتجاوز أسبوع نهايتها الأسبوع الحالي للمشروع). */
  delayedTasks: number
  projectsWithDelayedTasks: number
  /** متوسط الإنجاز الفعلي للمشاريع ذات القيمة المتوفرة، أو null. */
  averageProgress: number | null
  progressAvailable: number
  /** نسبة المشاريع الملتزمة بالجدول من المشاريع غير المكتملة القابلة للقياس، أو null. */
  scheduleCompliance: number | null
  complianceEligible: number
  compliant: number
  attention: number
}

interface DepartmentProgress {
  /** null = مشاريع لم تُحدَّد إدارتها. */
  department: string | null
  averageProgress: number
  projects: number
}

interface ProgressTrendPoint {
  /** YYYY-MM */
  month: string
  averageProgress: number
  /** عدد المشاريع التي لها تحديث أسبوعي حتى نهاية الشهر. */
  reportedProjects: number
}

export interface ExecutiveDashboardResponse {
  generatedAt: string
  /** اليوم المحلي للخادم YYYY-MM-DD (أساس حسابات المواعيد). */
  today: string
  settings: AppSettings
  kpis: ExecutiveKpis
  projects: ExecutiveProject[]
  departments: DepartmentProgress[]
  trend: ProgressTrendPoint[]
}
