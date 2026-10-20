'use client'

import { Loader2 } from 'lucide-react'
import { buttonClassName, type ButtonSize, type ButtonVariant } from './buttonStyles'

export type { ButtonSize, ButtonVariant }

interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  size?: ButtonSize
  /** حالة التحميل: تعطّل الزر وتمنع النقر المتكرر وتعرض مؤشّرًا + نصًا بديلًا. */
  loading?: boolean
  /** النص المعروض أثناء التحميل (افتراضيًا يبقى نص الزر نفسه مع مؤشّر). */
  loadingLabel?: string
  /** سبب التعطيل — مطلوب في إرشادات التصميم: «سبب التعطيل نصيًا قريبًا منه». */
  disabledReason?: string
  /** أيقونة lucide اختيارية تسبق النص. */
  icon?: React.ReactNode
  /** يمدّد الزر لعرض الحاوية بالكامل. */
  block?: boolean
}

export function Button({
  variant = 'primary',
  size = 'md',
  loading = false,
  loadingLabel,
  disabledReason,
  icon,
  block = false,
  disabled,
  children,
  className = '',
  type = 'button',
  ...rest
}: Readonly<ButtonProps>) {
  const isDisabled = disabled || loading
  return (
    <button
      type={type}
      disabled={isDisabled}
      aria-busy={loading || undefined}
      aria-disabled={isDisabled || undefined}
      title={isDisabled && !loading ? disabledReason : rest.title}
      className={buttonClassName({ variant, size, block, className })}
      {...rest}
    >
      {loading ? (
        <Loader2 aria-hidden="true" className="h-4 w-4 shrink-0 animate-spin" />
      ) : (
        icon
      )}
      <span>{loading ? loadingLabel ?? children : children}</span>
      {/* حالة التحميل تُعلَن لقارئ الشاشة نصيًا لا بالحركة وحدها. */}
      {loading && (
        <output className="sr-only">
          جارٍ التنفيذ، يرجى الانتظار
        </output>
      )}
    </button>
  )
}

/** زر أيقوني فقط — يفرض اسم وصول صريح ومنطقة لمس ≥ 44×44px. */
interface IconButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  label: string
  children: React.ReactNode
  variant?: 'ghost' | 'danger'
}

export function IconButton({
  label,
  children,
  variant = 'ghost',
  className = '',
  type = 'button',
  ...rest
}: Readonly<IconButtonProps>) {
  return (
    <button
      type={type}
      aria-label={label}
      title={label}
      className={`inline-flex h-11 w-11 items-center justify-center rounded-pc-md transition-colors duration-150 focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-40 ${
        variant === 'danger'
          ? 'text-pc-error-600 hover:bg-pc-error-100 focus-visible:ring-pc-error-600'
          : 'text-pc-gray-600 hover:bg-pc-gray-100 focus-visible:ring-pc-gray-600'
      } ${className}`}
      {...rest}
    >
      {children}
    </button>
  )
}
