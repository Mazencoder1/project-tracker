'use client'

import { useState } from 'react'
import { ChevronDown } from 'lucide-react'
import { BidiText, Button, Card, DateText } from '@/app/components/ui'
import { formatCount } from '@/lib/arabicCount'
import type { ExecutiveStatus } from '@/lib/portfolio/executive'
import { projectStatusBadgeClass } from '@/lib/statusStyles'
import type { Project } from '../types'
import type { DeviationTone } from '../utils'
import { ExecStatusBadge } from './dashboard/ExecStatusBadge'
import { ProjectSelector } from './ProjectSelector'

interface ProjectDashboardProps {
  selected: Project
  projects: Project[]
  onSelectProject: (project: Project) => void
  statusLabel: Record<string, string>
  assignedPmNames: string[]
  completedMilestones: number
  milestonesCount: number
  actualProgress: number
  deviationTone: DeviationTone
  timelineLabel: string
  executiveStatus?: ExecutiveStatus
  currentMilestoneName?: string | null
  /**
   * false: التفاصيل ظاهرة دائمًا بلا زر طي. تستخدمها شاشة «تفاصيل التنفيذ» لأن نسختها تُصوَّر
   * كاملةً في تقرير PDF التفصيلي (html2canvas يحترم hidden، فالطي كان سيحذف الحقول من الملف).
   */
  collapsibleDetails?: boolean
}

const DEVIATION_TONE_CLASS: Record<DeviationTone, { text: string; badgeBg: string; badgeText: string; label: string }> = {
  success: { text: 'text-pc-success-600', badgeBg: 'bg-pc-success-100', badgeText: 'text-pc-success-800', label: 'ضمن الخطة' },
  warning: { text: 'text-pc-warning-600', badgeBg: 'bg-pc-warning-100', badgeText: 'text-pc-warning-800', label: 'يحتاج متابعة' },
  error: { text: 'text-pc-error-600', badgeBg: 'bg-pc-error-100', badgeText: 'text-pc-error-800', label: 'متأخر' },
}

const EXEC_PROGRESS_TEXT: Record<ExecutiveStatus, string> = {
  on_track: 'text-pc-success-600',
  completed: 'text-pc-success-600',
  needs_attention: 'text-pc-warning-600',
  delayed: 'text-pc-error-600',
  stalled: 'text-pc-error-600',
  unknown: 'text-pc-gray-700',
}

const DETAILS_PANEL_ID = 'project-summary-details'

export function ProjectDashboard({
  selected,
  projects,
  onSelectProject,
  statusLabel,
  assignedPmNames,
  completedMilestones,
  milestonesCount,
  actualProgress,
  deviationTone,
  timelineLabel,
  executiveStatus,
  currentMilestoneName,
  collapsibleDetails = true,
}: Readonly<ProjectDashboardProps>) {
  const [detailsOpen, setDetailsOpen] = useState(false)
  const detailsHidden = collapsibleDetails && !detailsOpen
  const tone = DEVIATION_TONE_CLASS[deviationTone]
  const progressText = executiveStatus ? EXEC_PROGRESS_TEXT[executiveStatus] : tone.text

  return (
    <Card as="section" padded={false} className="p-pc-m">
      <div className="grid gap-pc-m lg:grid-cols-[minmax(0,1fr)_minmax(15rem,17.5rem)_auto] lg:items-start">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-pc-s">
            <h2 className="text-lg font-extrabold text-pc-gray-900"><BidiText>{selected.name}</BidiText></h2>
            <span className={`whitespace-nowrap rounded-full px-pc-s py-1 text-xs font-bold ${projectStatusBadgeClass(selected.status)}`}>
              {statusLabel[selected.status] || selected.status}
            </span>
            {collapsibleDetails && (
              <Button
                variant="ghost"
                size="sm"
                onClick={() => setDetailsOpen(open => !open)}
                aria-expanded={detailsOpen}
                aria-controls={DETAILS_PANEL_ID}
                icon={<ChevronDown aria-hidden="true" className={`h-4 w-4 transition-transform ${detailsOpen ? 'rotate-180' : ''}`} />}
              >
                {detailsOpen ? 'إخفاء التفاصيل' : 'عرض التفاصيل'}
              </Button>
            )}
          </div>

          <div id={DETAILS_PANEL_ID} hidden={detailsHidden}>
            {selected.description && (
              <p className="mt-pc-xs max-w-2xl text-sm leading-6 text-pc-gray-600">{selected.description}</p>
            )}

            <dl className="mt-pc-s grid gap-x-pc-l gap-y-pc-xs text-sm sm:grid-cols-2 xl:grid-cols-3">
              <div><dt className="inline text-pc-gray-600">مسؤول المشروع: </dt><dd className="inline font-semibold text-pc-gray-800">{assignedPmNames.length > 0 ? assignedPmNames.join('، ') : 'غير محدد'}</dd></div>
              <div><dt className="inline text-pc-gray-600">الإدارة: </dt><dd className="inline font-semibold text-pc-gray-800">{selected.department || 'غير محددة'}</dd></div>
              <div><dt className="inline text-pc-gray-600">المدة: </dt><dd className="inline font-semibold text-pc-gray-800">{selected.total_weeks > 0 ? formatCount(selected.total_weeks, 'week') : 'غير محددة'}</dd></div>
              <div><dt className="inline text-pc-gray-600">الفترة: </dt><dd className="inline font-semibold text-pc-gray-800"><DateText value={selected.start_date} /> — <DateText value={selected.end_date} /></dd></div>
              <div><dt className="inline text-pc-gray-600">موضع اليوم: </dt><dd className="inline font-semibold text-pc-gray-800">{timelineLabel}</dd></div>
              <div><dt className="inline text-pc-gray-600">المرحلة الحالية: </dt><dd className="inline font-semibold text-pc-gray-800">{currentMilestoneName || 'لا توجد مراحل'}</dd></div>
            </dl>
          </div>
        </div>

        <ProjectSelector
          projects={projects}
          selected={selected}
          onSelectProject={onSelectProject}
          statusLabel={statusLabel}
        />

        <div className="flex min-w-[9.375rem] flex-col items-start gap-pc-xs border-pc-gray-100 lg:border-s lg:ps-pc-m">
          <div className={`text-4xl font-black tabular-nums ${progressText}`}>{actualProgress}%</div>
          <p className="text-sm text-pc-gray-600">الإنجاز الكلي</p>
          {executiveStatus ? (
            <ExecStatusBadge status={executiveStatus} className="px-pc-m py-pc-xs text-sm" />
          ) : (
            <span className={`whitespace-nowrap rounded-full px-pc-s py-pc-xs text-xs font-bold ${tone.badgeBg} ${tone.badgeText}`}>{tone.label}</span>
          )}
          <p className="text-sm text-pc-gray-600">مراحل مكتملة: {completedMilestones} من {milestonesCount}</p>
        </div>
      </div>
    </Card>
  )
}
