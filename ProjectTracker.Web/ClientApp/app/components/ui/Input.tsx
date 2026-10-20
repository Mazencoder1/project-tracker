'use client'

import { CheckCircle2 } from 'lucide-react'
import { FieldShell, controlClass } from './Field'

interface InputProps extends Omit<React.InputHTMLAttributes<HTMLInputElement>, 'id'> {
  id: string
  label: string
  help?: string
  error?: string
  /** رسالة نجاح تحقّق فوري للحقل (مثال: «رقم هوية صالح»). */
  success?: string
  /** عنصر بصري أو زر داخل نهاية الحقل، مثل زر إظهار كلمة المرور. */
  endAdornment?: React.ReactNode
  inputClassName?: string
}

export function Input({
  id,
  label,
  help,
  error,
  success,
  endAdornment,
  inputClassName = '',
  required,
  className = '',
  ...rest
}: Readonly<InputProps>) {
  const inferredDirection = rest.dir ?? (
    rest.type === 'password' || rest.type === 'email' || rest.type === 'url' || rest.type === 'tel'
      ? 'ltr'
      : undefined
  )

  return (
    <FieldShell id={id} label={label} required={required} help={help} error={error} className={className}>
      {aria => (
        <>
          <div className="relative">
            <input
              {...aria}
              {...rest}
              dir={inferredDirection}
              required={required}
              className={`${controlClass(!!error)} ${endAdornment ? 'pe-12' : ''} ${inputClassName}`}
            />
            {endAdornment && (
              <div className={`absolute inset-y-0 flex items-center ${inferredDirection === 'ltr' ? 'right-1' : 'end-1'}`}>
                {endAdornment}
              </div>
            )}
          </div>
          {success && !error && (
            <p className="flex items-center gap-pc-xs text-sm font-medium text-pc-success-600">
              <CheckCircle2 aria-hidden="true" className="h-4 w-4 shrink-0" />
              <span>{success}</span>
            </p>
          )}
        </>
      )}
    </FieldShell>
  )
}

interface TextAreaProps extends Omit<React.TextareaHTMLAttributes<HTMLTextAreaElement>, 'id'> {
  id: string
  label: string
  help?: string
  error?: string
  /** كلاسات إضافية للحقل نفسه (مثل font-mono لمحرر HTML) دون المساس بالتسمية والمساعد. */
  inputClassName?: string
}

export function TextArea({ id, label, help, error, required, rows = 4, inputClassName = '', ...rest }: Readonly<TextAreaProps>) {
  return (
    <FieldShell id={id} label={label} required={required} help={help} error={error}>
      {aria => (
        <textarea
          {...aria}
          {...rest}
          rows={rows}
          required={required}
          className={`${controlClass(!!error)} resize-y ${inputClassName}`}
        />
      )}
    </FieldShell>
  )
}
