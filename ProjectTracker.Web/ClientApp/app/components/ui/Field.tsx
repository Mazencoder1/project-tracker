'use client'

import { useEffect, useRef } from 'react'
import { AlertCircle } from 'lucide-react'

export interface FieldShellProps {
  id: string
  label: string
  required?: boolean
  help?: string
  error?: string
  className?: string
  children: (aria: {
    id: string
    'aria-describedby'?: string
    'aria-invalid'?: true
    'aria-required'?: true
  }) => React.ReactNode
}

/** الإطار المشترك لكل حقل: تسمية فوقه، مساعد وخطأ تحته، وربط ARIA كامل. */
export function FieldShell({
  id,
  label,
  required = false,
  help,
  error,
  className = '',
  children,
}: Readonly<FieldShellProps>) {
  const helpId = help ? `${id}-help` : undefined
  const errorId = error ? `${id}-error` : undefined
  const describedBy = [helpId, errorId].filter(Boolean).join(' ') || undefined

  return (
    <div className={`flex flex-col gap-pc-xs ${className}`}>
      <label htmlFor={id} className="text-sm font-medium text-pc-gray-800">
        {label}
        {required && (
          <>
            {' '}
            <span aria-hidden="true" className="text-pc-error-600">
              *
            </span>
            <span className="sr-only">(حقل إلزامي)</span>
          </>
        )}
      </label>

      {children({
        id,
        'aria-describedby': describedBy,
        'aria-invalid': error ? true : undefined,
        'aria-required': required ? true : undefined,
      })}

      {help && !error && (
        <p id={helpId} className="text-sm text-pc-gray-600">
          {help}
        </p>
      )}

      {error && (
        <p
          id={errorId}
          role="alert"
          className="flex items-start gap-pc-xs text-sm font-medium text-pc-error-600"
        >
          <span aria-hidden="true" className="flex h-5 shrink-0 items-center">
            <AlertCircle className="h-4 w-4" />
          </span>
          <span>{error}</span>
        </p>
      )}
    </div>
  )
}

/** النص التوضيحي الإلزامي أعلى أي نموذج فيه حقول مطلوبة. */
export function FormRequiredHint({ className = '' }: Readonly<{ className?: string }>) {
  return (
    <p className={`text-sm text-pc-gray-600 ${className}`}>
      الحقول المعلَّمة بـ <span className="text-pc-error-600">*</span> إلزامية.
    </p>
  )
}

/**
 * ملخّص أخطاء أعلى النموذج مع روابط قفز للحقول.
 * يستقبل التركيز تلقائيًا عند ظهوره حتى يسمعه مستخدم قارئ الشاشة فورًا.
 */
export function FormErrorSummary({
  errors,
  title = 'تعذّر حفظ النموذج، صحّح الأخطاء التالية:',
  autoFocus = true,
}: Readonly<{
  errors: { fieldId: string; message: string }[]
  title?: string
  /**
   * false حين ينقل النموذج التركيز
   * بنفسه إلى أول حقل غير صالح (نوافذ admin)، فيبقى الملخّص ظاهرًا ومُعلَنًا (role="alert").
   */
  autoFocus?: boolean
}>) {
  const ref = useRef<HTMLDivElement>(null)
  const signature = errors.map(e => e.fieldId).join('|')
  useEffect(() => {
    // نقل التركيز تلقائيًا للملخّص عند ظهوره.
    if (autoFocus && signature) ref.current?.focus()
  }, [autoFocus, signature])
  if (errors.length === 0) return null
  return (
    <div
      role="alert"
      tabIndex={-1}
      ref={ref}
      className="rounded-pc-md border border-pc-error-600 bg-pc-error-50 p-pc-m outline-none"
    >
      <p className="flex items-center gap-2 text-sm font-semibold text-pc-error-800">
        <AlertCircle aria-hidden="true" className="h-5 w-5 shrink-0" />
        {title}
      </p>
      <ul className="mt-2 list-inside list-disc space-y-1 ps-1">
        {errors.map(e => (
          <li key={e.fieldId} className="text-sm text-pc-error-800">
            <a href={`#${e.fieldId}`} className="underline underline-offset-2">
              {e.message}
            </a>
          </li>
        ))}
      </ul>
    </div>
  )
}

/** الكلاسات المشتركة لأي عنصر إدخال (حدّ متباين ≥3:1، ارتفاع لمسة ≥44px جوال).
 *  border-pc-border-strong → border-pc-gray-400 (#8A8A8A، 3.45:1)
 *  بعد إلغاء التوكن من globals.css — كان الحدّ يختفي فعليًا بعد الإلغاء. */
export const controlClass = (hasError?: boolean) =>
  'w-full rounded-pc-md border bg-white px-pc-s py-pc-s text-base text-pc-gray-900 ' +
  'min-h-[2.75rem] sm:min-h-[2.5rem] placeholder:text-pc-gray-500 ' +
  'transition-colors duration-150 focus:outline-none focus-visible:outline-none ' +
  'focus:ring-2 focus:ring-offset-1 disabled:cursor-not-allowed disabled:bg-pc-gray-50 disabled:opacity-60 ' +
  (hasError
    ? 'border-pc-error-600 focus:border-pc-error-600 focus:ring-pc-error-600'
    : 'border-pc-gray-400 focus:border-pc-brand-600 focus:ring-pc-brand-600')
