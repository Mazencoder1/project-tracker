import type { LucideIcon } from 'lucide-react'
import { History } from 'lucide-react'
import {
  HEALTH,
} from '@/lib/portfolio/labels'
import type {
  HealthStatus,
} from '@/lib/portfolio/types'

function Pill({
  icon: Icon,
  label,
  className,
  srPrefix,
  size = 'md',
}: Readonly<{
  icon?: LucideIcon
  label: string
  className: string
  /** بادئة لقارئ الشاشة مثل «صحة التنفيذ:» حتى لا تُقرأ الشارة بلا سياق. */
  srPrefix?: string
  size?: 'sm' | 'md'
}>) {
  return (
    <span
      className={`inline-flex max-w-full items-center gap-pc-xs whitespace-nowrap rounded-full font-semibold ${
        size === 'sm' ? 'px-pc-s py-0.5 text-sm' : 'px-pc-s py-pc-xs text-sm'
      } ${className}`}
    >
      {Icon && <Icon aria-hidden="true" className="h-4 w-4 shrink-0" />}
      {srPrefix && <span className="sr-only">{srPrefix} </span>}
      <span className="truncate">{label}</span>
    </span>
  )
}

export function HealthBadge({ health, size }: Readonly<{ health: HealthStatus; size?: 'sm' | 'md' }>) {
  const s = HEALTH[health] ?? HEALTH.unknown
  return <Pill icon={s.icon} label={s.label} className={s.badge} srPrefix="صحة التنفيذ:" size={size} />
}

export function StaleBadge({ lastReportDate }: Readonly<{ lastReportDate: string | null }>) {
  return (
    <Pill
      icon={History}
      label={lastReportDate ? 'بيانات قديمة' : 'لا يوجد تحديث أسبوعي'}
      className="bg-pc-warning-100 text-pc-warning-800"
      size="sm"
    />
  )
}

