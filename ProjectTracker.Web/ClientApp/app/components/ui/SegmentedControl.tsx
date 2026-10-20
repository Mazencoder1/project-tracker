'use client'

export interface SegmentedOption<T extends string> {
  key: T
  label: string
}

export function SegmentedControl<T extends string>({
  label,
  options,
  value,
  onChange,
  className = '',
}: Readonly<{
  /** اسم وصول المجموعة، مثل «مستوى تكبير المخطط». */
  label: string
  options: SegmentedOption<T>[]
  value: T
  onChange: (key: T) => void
  className?: string
}>) {
  return (
    <fieldset
      aria-label={label}
      className={`m-0 inline-flex min-w-0 items-center gap-1 rounded-pc-md border border-pc-gray-200 bg-pc-gray-50 p-1 ${className}`}
    >
      {options.map(option => {
        const active = option.key === value
        return (
          <button
            key={option.key}
            type="button"
            aria-pressed={active}
            onClick={() => onChange(option.key)}
            className={`inline-flex min-h-[2.75rem] min-w-[2.75rem] items-center justify-center rounded-pc-md px-pc-s text-sm font-semibold transition-colors duration-200 focus:outline-none focus-visible:ring-2 focus-visible:ring-pc-brand-700 focus-visible:ring-offset-1 sm:min-h-[2.25rem] ${
              active ? 'bg-pc-brand-600 text-white shadow-pc-card' : 'text-pc-gray-700 hover:bg-white hover:text-pc-brand-700'
            }`}
          >
            {option.label}
          </button>
        )
      })}
    </fieldset>
  )
}
