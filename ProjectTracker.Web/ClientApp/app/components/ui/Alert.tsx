'use client'

import { AlertTriangle, CheckCircle2, Info, OctagonAlert, X } from 'lucide-react'

export type AlertTone = 'info' | 'success' | 'warning' | 'danger'

const tones: Record<
  AlertTone,
  { cls: string; Icon: typeof Info; srLabel: string }
> = {
  info: {
    cls: 'border-pc-info-600 bg-pc-info-50 text-pc-info-800',
    Icon: Info,
    srLabel: 'معلومة:',
  },
  success: {
    cls: 'border-pc-success-600 bg-pc-success-50 text-pc-success-800',
    Icon: CheckCircle2,
    srLabel: 'نجاح:',
  },
  warning: {
    cls: 'border-pc-warning-600 bg-pc-warning-50 text-pc-warning-800',
    Icon: AlertTriangle,
    srLabel: 'تنبيه:',
  },
  danger: {
    cls: 'border-pc-error-600 bg-pc-error-50 text-pc-error-800',
    Icon: OctagonAlert,
    srLabel: 'خطأ:',
  },
}

interface AlertProps {
  tone?: AlertTone
  title?: string
  children?: React.ReactNode
  /** يُظهر زر إغلاق — للتنبيهات غير الحرجة فقط حسب إرشادات التصميم. */
  onDismiss?: () => void
  /** إجراء تصحيحي (مثال: «إعادة المحاولة»). */
  action?: React.ReactNode
  className?: string
}

export function Alert({
  tone = 'info',
  title,
  children,
  onDismiss,
  action,
  className = '',
}: Readonly<AlertProps>) {
  const { cls, Icon, srLabel } = tones[tone]
  const isAlert = tone === 'danger' || tone === 'warning'
  const Wrapper = isAlert ? 'div' : 'output'
  return (
    <Wrapper
      {...(isAlert ? { role: 'alert' } : {})}
      className={`flex items-start gap-pc-s rounded-pc-md border p-pc-m ${cls} ${className}`}
    >
      <Icon aria-hidden="true" className="h-5 w-5 shrink-0" />
      <div className="min-w-0 flex-1">
        <span className="sr-only">{srLabel}</span>
        {title && <p className="text-sm font-semibold">{title}</p>}
        {children && <div className={`text-sm ${title ? 'mt-1' : ''}`}>{children}</div>}
        {action && <div className="mt-pc-s">{action}</div>}
      </div>
      {onDismiss && (
        <button
          type="button"
          onClick={onDismiss}
          aria-label="إغلاق التنبيه"
          className="-m-pc-xs flex h-11 w-11 shrink-0 items-center justify-center rounded-pc-md hover:bg-black/5 focus:outline-none focus-visible:ring-2 focus-visible:ring-current"
        >
          <X aria-hidden="true" className="h-4 w-4" />
        </button>
      )}
    </Wrapper>
  )
}

/** شارة حالة صغيرة — دائمًا نص + (اختياريًا) أيقونة، لا لون وحده. */
export function Badge({
  tone = 'info',
  children,
  icon,
  className = '',
}: Readonly<{
  tone?: AlertTone | 'neutral' | 'accent'
  children: React.ReactNode
  icon?: React.ReactNode
  className?: string
}>) {
  const map: Record<string, string> = {
    info: 'bg-pc-info-100 text-pc-info-800',
    success: 'bg-pc-success-100 text-pc-success-800',
    warning: 'bg-pc-warning-100 text-pc-warning-800',
    danger: 'bg-pc-error-100 text-pc-error-800',
    neutral: 'bg-pc-gray-100 text-pc-gray-700',
    // لون التمييز: شارات وتفاصيل محدودة فقط.
    accent: 'bg-pc-accent-100 text-pc-accent-800',
  }
  return (
    <span
      className={`inline-flex items-center gap-1 rounded-pc-md px-2 py-1 text-sm font-medium ${map[tone]} ${className}`}
    >
      {icon}
      {children}
    </span>
  )
}
