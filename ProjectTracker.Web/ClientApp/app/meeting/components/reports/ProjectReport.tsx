'use client'

import { type ReactNode } from 'react'
import { BidiText } from '@/app/components/ui'
import { formatCount } from '@/lib/arabicCount'
import { SUPPORT_KIND_LABEL, SUPPORT_SOURCE_LABEL, SUPPORT_STATUS_LABEL, type SupportRequest } from '@/lib/portfolio/executive'
import type { Milestone, Project, Task } from '../../types'
import { ReportControls } from './ReportControls'
import { ReportPage, reportFileDate } from './ReportPage'

interface PagePart {
  key: string
  sectionTitle: string
  content: ReactNode
}

interface TimelineItem {
  id: string
  name: string
  start: number
  end: number
  progress: number
  status?: string
}

function chunks<T>(items: T[], size: number) {
  const result: T[][] = []
  for (let index = 0; index < items.length; index += size) result.push(items.slice(index, index + size))
  return result
}

/**
 * اسم المشروع الطويل كان يلتف ثلاثة أسطر في ترويسة الصفحة الثابتة الارتفاع فيُقص سطره الأخير
 * تحت الخط الأخضر. الترويسة تعرض أول 60 حرفًا، والاسم الكامل موجود في بطاقة بيانات المشروع.
 */
function reportTitleName(name: string) {
  const trimmed = name.trim()
  return trimmed.length > 60 ? `${trimmed.slice(0, 57).trim()}…` : trimmed
}

function formatDateValue(value?: string) {
  if (!value) return 'غير متوفر'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return 'غير متوفر'
  return date.toLocaleDateString('ar-SA-u-ca-gregory-nu-latn', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).replace(/[\u200e\u200f\u061c]/g, '')
}

function itemState(item: TimelineItem, currentWeek: number | null) {
  if (item.progress >= 100 || item.status === 'completed') return 'مكتملة'
  if (currentWeek !== null && currentWeek > item.end) return 'متأخرة'
  if ((currentWeek !== null && currentWeek >= item.start && currentWeek <= item.end) || item.status === 'in_progress') return 'جارية'
  return 'لم تبدأ'
}

function ProjectMetadata({
  project,
  managerNames,
  statusLabel,
  progress,
  progressAvailable,
  timelineLabel,
}: Readonly<{
  project: Project
  managerNames: string[]
  statusLabel: string
  progress: number
  progressAvailable: boolean
  timelineLabel: string
}>) {
  const fields = [
    ['اسم المشروع', project.name],
    ['مدير أو مسؤول المشروع', managerNames.join('، ') || 'غير متوفر'],
    ['حالة المشروع', statusLabel || 'غير متوفر'],
    ['نسبة الإنجاز', progressAvailable ? `${Math.round(progress)}%` : 'غير متوفر'],
    ['تاريخ البداية', formatDateValue(project.start_date)],
    ['تاريخ النهاية', formatDateValue(project.end_date)],
    ['الموضع الزمني', timelineLabel || 'غير متوفر'],
    ['مدة المشروع المسجلة', project.total_weeks > 0 ? formatCount(project.total_weeks, 'week') : 'غير متوفر'],
  ]
  return (
    <dl className="mb-pc-m grid grid-cols-4 rounded-pc-md border-2 border-pc-gray-400">
      {fields.map(([label, value], index) => (
        <div key={label} className={`min-h-[66px] border-pc-gray-300 px-pc-s py-2 ${index >= 4 ? 'border-t' : ''} ${index % 4 !== 3 ? 'border-l' : ''}`}>
          <dt className="text-xs font-semibold text-pc-gray-600">{label}</dt>
          <dd className="mt-1 break-words text-sm font-bold leading-6 text-pc-gray-900"><BidiText>{value}</BidiText></dd>
        </div>
      ))}
    </dl>
  )
}

function WeekTimeline({
  items,
  totalWeeks,
  currentWeek,
  markerWeek,
  timelineLabel,
  label,
  emptyText,
}: Readonly<{
  items: TimelineItem[]
  totalWeeks: number
  currentWeek: number | null
  markerWeek: number | null
  timelineLabel: string
  label: string
  emptyText: string
}>) {
  const weeks = Math.max(1, totalWeeks)
  const showCurrent = markerWeek !== null && markerWeek > 0 && markerWeek <= weeks
  const ticks = Array.from(new Set([1, Math.max(1, Math.ceil(weeks * 0.25)), Math.max(1, Math.ceil(weeks * 0.5)), Math.max(1, Math.ceil(weeks * 0.75)), weeks]))

  const edgeTicks = weeks > 4
  const tickRight = (week: number) => {
    if (edgeTicks && week === 1) return '0%'
    if (edgeTicks && week === weeks) return '100%'
    return `${((week - 0.5) / weeks) * 100}%`
  }
  const tickTransform = (week: number) => {
    if (edgeTicks && week === 1) return 'none'
    if (edgeTicks && week === weeks) return 'translateX(100%)'
    return 'translateX(50%)'
  }

  return (
    <div className="rounded-pc-md border-2 border-pc-gray-400">
      <div className="grid rounded-t-pc-md bg-pc-brand-900 text-sm font-bold text-white" style={{ gridTemplateColumns: '290px minmax(0, 1fr)' }}>
        <div className="border-l border-white/30 px-pc-s py-2">{label}</div>
        <div className="relative h-11">
          {ticks.map(week => (
            <span
              key={week}
              className="absolute top-0 flex h-full items-center whitespace-nowrap px-1 text-xs"
              style={{ right: tickRight(week), transform: tickTransform(week) }}
            >
              أسبوع {week}
            </span>
          ))}
        </div>
      </div>
      <div className="relative">
        {showCurrent && (
          <div
            data-current-time-line="true"
            aria-hidden="true"
            className="absolute bottom-0 top-0 z-10 w-0.5 bg-pc-error-600"
            style={{ right: `calc(290px + (100% - 290px) * ${((markerWeek ?? 1) - 0.5) / weeks})` }}
          />
        )}
        {items.length > 0 ? items.map((item, index) => {
          const start = Math.max(1, Math.min(weeks, item.start))
          const end = Math.max(start, Math.min(weeks, item.end))
          const rawOffset = ((start - 1) / weeks) * 100
          const width = Math.max(5, ((end - start + 1) / weeks) * 100)
          const offset = Math.min(100 - width, rawOffset)
          // الشريط الأضيق من نص النسبة تُكتب نسبته بجانبه بدل قصّها داخله (يسار الشريط، أو يمينه عند نهاية المخطط).
          const labelOutside = width < 8
          const labelAtEnd = offset + width > 90
          const progress = Math.max(0, Math.min(100, Math.round(item.progress || 0)))
          const state = itemState(item, currentWeek)
          return (
            <div key={item.id} className={`grid min-h-[70px] border-t border-pc-gray-300 ${index % 2 === 0 ? 'bg-white' : 'bg-pc-gray-50'}`} style={{ gridTemplateColumns: '290px minmax(0, 1fr)' }}>
              <div className="flex flex-col justify-center border-l border-pc-gray-400 px-pc-s py-1">
                <p className="break-words text-sm font-bold"><BidiText>{item.name}</BidiText></p>
                <p className="text-xs text-pc-gray-700">أسبوع {item.start} — {item.end} · {state}</p>
              </div>
              <div className="relative">
                <div className={`absolute top-1/2 h-8 -translate-y-1/2 overflow-hidden rounded-pc-md border-2 border-pc-brand-900 ${progress === 100 ? 'bg-pc-brand-100' : 'bg-pc-brand-600'}`} style={{ right: `${offset}%`, width: `${width}%` }}>
                  {progress < 100 && <div className="h-full bg-pc-brand-900" style={{ width: `${progress}%` }} />}
                  {!labelOutside && (progress === 100 ? (
                    <span className="absolute inset-0 flex items-center justify-center text-xs font-black text-pc-gray-900">{progress}%</span>
                  ) : (
                    <span className="absolute inset-0 flex items-center justify-center text-xs font-black text-white" style={{ textShadow: '0 1px 2px rgba(0, 0, 0, 0.55)' }}>{progress}%</span>
                  ))}
                </div>
                {labelOutside && (
                  <span
                    className="absolute top-1/2 -translate-y-1/2 whitespace-nowrap px-1 text-xs font-black text-pc-gray-900"
                    style={labelAtEnd ? { right: `${offset}%`, transform: 'translateX(100%)' } : { right: `${offset + width}%` }}
                  >
                    {progress}%
                  </span>
                )}
              </div>
            </div>
          )
        }) : (
          <p className="p-pc-l text-center text-sm font-semibold text-pc-gray-700">{emptyText}</p>
        )}
      </div>
      <div className="flex items-center justify-between rounded-b-pc-md border-t border-pc-gray-400 px-pc-s py-2 text-xs font-semibold leading-5 text-pc-gray-700">
        <span>الجزء الداكن داخل الشريط يوضح نسبة الإنجاز.</span>
        <span className="flex items-center gap-pc-xs text-pc-error-800">
          {showCurrent ? <><span aria-hidden="true" className="h-0.5 w-6 bg-pc-error-600" /> {timelineLabel}</> : timelineLabel}
        </span>
      </div>
    </div>
  )
}

interface ProjectReportProps {
  project: Project
  managerNames: string[]
  statusLabel: string
  progress: number
  progressAvailable: boolean
  timelineLabel: string
  currentWeek: number | null
  markerWeek: number | null
  milestones: Milestone[]
  currentMilestone: Milestone | undefined
  currentTasks: Task[]
  /** طلبات «الدعم المطلوب من الإدارة العليا» — الصفحة الأخيرة في التقرير. */
  supportRequests?: SupportRequest[]
}

/** جدول طلبات الدعم في تقرير المشروع (نفس بيانات القسم البارز في الشاشة). */
function SupportRequestsTable({ rows }: Readonly<{ rows: SupportRequest[] }>) {
  return (
    <table className="w-full table-fixed border-collapse text-sm">
      <thead>
        <tr className="bg-pc-warning-600 text-white">
          <th className="w-[12%] border border-pc-gray-400 px-pc-s py-2 text-start">نوع الطلب</th>
          <th className="border border-pc-gray-400 px-pc-s py-2 text-start">وصف الدعم</th>
          <th className="w-[16%] border border-pc-gray-400 px-pc-s py-2 text-start">الجهة المعنية</th>
          <th className="w-[11%] border border-pc-gray-400 px-pc-s py-2 text-start">تاريخ الطلب</th>
          <th className="w-[10%] border border-pc-gray-400 px-pc-s py-2 text-start">الحالة</th>
          <th className="w-[16%] border border-pc-gray-400 px-pc-s py-2 text-start">المصدر</th>
        </tr>
      </thead>
      <tbody>
        {rows.length === 0 ? (
          <tr><td colSpan={6} className="border border-pc-gray-400 px-pc-s py-pc-m text-center font-semibold text-pc-gray-700">لا يوجد دعم مطلوب حاليًا</td></tr>
        ) : rows.map((row, index) => (
          <tr key={row.id} className={index % 2 === 0 ? 'bg-white' : 'bg-pc-warning-50'}>
            <td className="border border-pc-gray-400 px-pc-s py-1.5 font-bold">{SUPPORT_KIND_LABEL[row.kind]}</td>
            <td className="border border-pc-gray-400 px-pc-s py-1.5"><span className="break-words"><BidiText>{row.description}</BidiText></span></td>
            <td className="border border-pc-gray-400 px-pc-s py-1.5">{row.party ?? 'غير محددة'}</td>
            <td className="border border-pc-gray-400 px-pc-s py-1.5 tabular-nums">{row.requestedAt ? formatDateValue(row.requestedAt) : 'غير مسجل'}</td>
            <td className="border border-pc-gray-400 px-pc-s py-1.5">{SUPPORT_STATUS_LABEL[row.status] ?? row.status}</td>
            <td className="border border-pc-gray-400 px-pc-s py-1.5"><span className="break-words">{SUPPORT_SOURCE_LABEL[row.source]}{row.milestoneName ? ` — ${row.milestoneName}` : ''}</span></td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

export function ProjectReport(props: Readonly<ProjectReportProps>) {
  return (
    <ReportControls fileName={preparedAt => `تقرير-متابعة-${props.project.name.trim().replace(/\s+/g, '-')}-${reportFileDate(preparedAt)}`}>
      {preparedAt => <ProjectReportPages {...props} preparedAt={preparedAt} />}
    </ReportControls>
  )
}

function ProjectReportPages({
  project,
  managerNames,
  statusLabel,
  progress,
  progressAvailable,
  timelineLabel,
  currentWeek,
  markerWeek,
  milestones,
  currentMilestone,
  currentTasks,
  supportRequests,
  preparedAt,
}: ProjectReportProps & { preparedAt: Date }) {
  const projectWeeks = Math.max(project.total_weeks || 0, ...milestones.map(item => item.week_end || 0), 1)
  const milestoneRows: TimelineItem[] = milestones.map(item => ({ id: item.id, name: item.name, start: item.week_start, end: item.week_end, progress: item.progress }))
  const taskRows: TimelineItem[] = currentTasks.map(item => ({ id: item.id, name: item.name, start: item.week_start, end: item.week_end, progress: item.progress, status: item.status }))
  const parts: PagePart[] = []
  const firstMilestones = milestoneRows.slice(0, 4)
  parts.push({
    key: 'summary',
    sectionTitle: 'بيانات المشروع والمخطط الزمني للمشروع',
    content: (
      <>
        <ProjectMetadata project={project} managerNames={managerNames} statusLabel={statusLabel} progress={progress} progressAvailable={progressAvailable} timelineLabel={timelineLabel} />
        <WeekTimeline items={firstMilestones} totalWeeks={projectWeeks} currentWeek={currentWeek} markerWeek={markerWeek} timelineLabel={timelineLabel} label="المرحلة الرئيسية" emptyText="لا توجد مراحل مسجلة لهذا المشروع." />
      </>
    ),
  })
  chunks(milestoneRows.slice(4), 6).forEach((rows, index) => parts.push({
    key: `milestones-${index}`,
    sectionTitle: `المخطط الزمني للمشروع — متابعة المراحل ${index + 2}`,
    content: <WeekTimeline items={rows} totalWeeks={projectWeeks} currentWeek={currentWeek} markerWeek={markerWeek} timelineLabel={timelineLabel} label="المرحلة الرئيسية" emptyText="لا توجد مراحل مسجلة لهذا المشروع." />,
  }))

  const taskChunks = chunks(taskRows, 6)
  ;(taskChunks.length > 0 ? taskChunks : [[]]).forEach((rows, index, array) => parts.push({
    key: `tasks-${index}`,
    sectionTitle: 'الخط الزمني لمهام المرحلة الحالية' + (array.length > 1 ? ` — الجزء ${index + 1} من ${array.length}` : ''),
    content: (
      <>
        <p className="mb-pc-s text-sm font-semibold text-pc-gray-700">
          المرحلة: <strong className="text-pc-brand-900"><BidiText>{currentMilestone?.name || 'غير متوفر'}</BidiText></strong>
        </p>
        <WeekTimeline items={rows} totalWeeks={projectWeeks} currentWeek={currentWeek} markerWeek={markerWeek} timelineLabel={timelineLabel} label="المهمة" emptyText="لا توجد مهام مسجلة داخل المرحلة الحالية." />
      </>
    ),
  }))

  if (supportRequests) {
    const supportChunks = chunks(supportRequests, 7)
    ;(supportChunks.length > 0 ? supportChunks : [[]]).forEach((rows, index, array) => parts.push({
      key: `support-${index}`,
      sectionTitle: 'الدعم المطلوب من الإدارة العليا' + (array.length > 1 ? ` — الجزء ${index + 1} من ${array.length}` : ''),
      content: <SupportRequestsTable rows={rows} />,
    }))
  }

  return (
    <>
      {parts.map((part, index) => (
        <ReportPage
          key={part.key}
          title={`تقرير متابعة مشروع: ${reportTitleName(project.name)}`}
          preparedAt={preparedAt}
          page={index + 1}
          totalPages={parts.length}
          sectionTitle={part.sectionTitle}
        >
          {part.content}
        </ReportPage>
      ))}
    </>
  )
}
