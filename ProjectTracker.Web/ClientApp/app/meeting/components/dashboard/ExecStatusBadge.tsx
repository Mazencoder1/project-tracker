import { AlertCircle, AlertTriangle, CheckCircle2, CircleCheckBig, Clock, HelpCircle, type LucideIcon } from 'lucide-react'
import { EXECUTIVE_STATUS_LABEL, type ExecutiveStatus } from '@/lib/portfolio/executive'
import type { ChartColor } from '@/app/components/charts/ApexChart'

export const EXEC_STATUS_STYLE: Record<ExecutiveStatus, { badge: string; icon: LucideIcon; chart: ChartColor; text: string }> = {
  on_track: { badge: 'bg-pc-success-100 text-pc-success-800', icon: CheckCircle2, chart: 'brandLight', text: 'text-pc-success-600' },
  needs_attention: { badge: 'bg-pc-warning-100 text-pc-warning-800', icon: AlertCircle, chart: 'warning', text: 'text-pc-warning-600' },
  delayed: { badge: 'bg-pc-error-100 text-pc-error-800', icon: Clock, chart: 'danger', text: 'text-pc-error-600' },
  stalled: { badge: 'bg-pc-error-600 text-white', icon: AlertTriangle, chart: 'dangerDark', text: 'text-pc-error-800' },
  completed: { badge: 'bg-pc-brand-50 text-pc-brand-800', icon: CircleCheckBig, chart: 'brandDark', text: 'text-pc-brand-800' },
  unknown: { badge: 'bg-pc-gray-100 text-pc-gray-700', icon: HelpCircle, chart: 'gray', text: 'text-pc-gray-600' },
}

export function ExecStatusBadge({ status, className = '' }: Readonly<{ status: ExecutiveStatus; className?: string }>) {
  const { badge, icon: Icon } = EXEC_STATUS_STYLE[status]
  return (
    <span className={`inline-flex items-center gap-1 whitespace-nowrap rounded-full px-pc-s py-1 text-xs font-bold ${badge} ${className}`}>
      <Icon aria-hidden="true" className="h-3.5 w-3.5" />
      {EXECUTIVE_STATUS_LABEL[status]}
    </span>
  )
}

/** شريط إنجاز مختصر مع النسبة، أو «غير متوفر». */
export function ProgressCell({ value, label }: Readonly<{ value: number | null; label: string }>) {
  if (value === null) return <span className="text-sm text-pc-gray-600">غير متوفر</span>
  const progress = Math.max(0, Math.min(100, Math.round(value)))
  return (
    <div className="flex min-w-[7.5rem] items-center gap-pc-s">
      <div aria-hidden="true" className="h-2 flex-1 overflow-hidden rounded-full border border-pc-gray-300 bg-pc-gray-50">
        <div className="h-full rounded-full bg-pc-brand-600" style={{ width: `${progress}%` }} />
      </div>
      <progress className="sr-only" aria-label={`نسبة إنجاز ${label}`} value={progress} max={100} />
      <span className="w-10 shrink-0 text-sm font-bold tabular-nums text-pc-gray-900">{progress}%</span>
    </div>
  )
}
