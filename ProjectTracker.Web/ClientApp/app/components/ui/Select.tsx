'use client'

import { useEffect, useMemo, useRef, useState } from 'react'
import { Check, ChevronDown, SearchX } from 'lucide-react'
import { FieldShell, controlClass } from './Field'

export interface SelectOption {
  value: string
  label: string
  disabled?: boolean
}

interface SelectProps {
  id: string
  label: string
  value: string
  onChange: (value: string) => void
  options: SelectOption[]
  /** null = بلا خيار فارغ (قوائم الترشيح/الفرز التي فيها خيار «الكل» صريح). */
  placeholder?: string | null
  help?: string
  error?: string
  required?: boolean
  disabled?: boolean
  className?: string
  /** عتبة تفعيل البحث الفوري — إرشادات التصميم يحدّدها بـ 7. */
  searchThreshold?: number
}

export function Select({
  id,
  label,
  value,
  onChange,
  options,
  placeholder = 'اختر…',
  help,
  error,
  required,
  disabled,
  className,
  searchThreshold = 7,
}: Readonly<SelectProps>) {
  const searchable = options.length > searchThreshold

  return (
    <FieldShell
      id={id}
      label={label}
      required={required}
      help={help}
      error={error}
      className={className}
    >
      {aria =>
        searchable ? (
          <SearchableSelect
            aria={aria}
            value={value}
            onChange={onChange}
            options={options}
            placeholder={placeholder ?? ''}
            disabled={disabled}
            hasError={!!error}
          />
        ) : (
          <select
            {...aria}
            value={value}
            required={required}
            disabled={disabled}
            onChange={e => onChange(e.target.value)}
            className={controlClass(!!error)}
          >
            {placeholder !== null && <option value="">{placeholder}</option>}
            {options.map(o => (
              <option key={o.value} value={o.value} disabled={o.disabled}>
                {o.label}
              </option>
            ))}
          </select>
        )
      }
    </FieldShell>
  )
}

/** القائمة القابلة للبحث (تُستخدم تلقائيًا فوق 7 خيارات). */
function SearchableSelect({
  aria,
  value,
  onChange,
  options,
  placeholder,
  disabled,
  hasError,
}: Readonly<{
  aria: Record<string, unknown>
  value: string
  onChange: (v: string) => void
  options: SelectOption[]
  placeholder: string
  disabled?: boolean
  hasError: boolean
}>) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [activeIndex, setActiveIndex] = useState(0)
  const wrapRef = useRef<HTMLDivElement>(null)
  const searchRef = useRef<HTMLInputElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const baseId = aria.id as string
  const listId = `${baseId}-listbox`
  const optionId = (i: number) => `${baseId}-option-${i}`

  const filtered = useMemo(() => {
    const q = query.trim()
    if (!q) return options
    return options.filter(o => o.label.includes(q))
  }, [options, query])

  const selected = options.find(o => o.value === value)

  useEffect(() => {
    if (!open) return
    searchRef.current?.focus()
    function onDocClick(e: MouseEvent) {
      if (!wrapRef.current?.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onDocClick)
    return () => document.removeEventListener('mousedown', onDocClick)
  }, [open])

  function close() {
    setOpen(false)
    setQuery('')
    triggerRef.current?.focus()
  }

  function pick(o: SelectOption) {
    if (o.disabled) return
    onChange(o.value)
    close()
  }

  function onKeyDown(e: React.KeyboardEvent) {
    if (e.key === 'Escape') {
      e.preventDefault()
      close()
      return
    }
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setActiveIndex(i => Math.min(i + 1, filtered.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActiveIndex(i => Math.max(i - 1, 0))
    } else if (e.key === 'Home') {
      e.preventDefault()
      setActiveIndex(0)
    } else if (e.key === 'End') {
      e.preventDefault()
      setActiveIndex(filtered.length - 1)
    } else if (e.key === 'Enter') {
      e.preventDefault()
      const o = filtered[activeIndex]
      if (o) pick(o)
    }
  }

  const activeExists = filtered.length > 0 && activeIndex < filtered.length

  return (
    <div ref={wrapRef} className="relative">
      <button
        {...aria}
        ref={triggerRef}
        type="button"
        disabled={disabled}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={open ? listId : undefined}
        onClick={() => {
          if (!open) setActiveIndex(Math.max(0, options.findIndex(o => o.value === value)))
          setOpen(o => !o)
        }}
        className={`${controlClass(hasError)} flex items-center justify-between gap-2 text-start`}
      >
        <span className={selected ? '' : 'text-pc-gray-600'}>
          {selected ? selected.label : placeholder}
        </span>
        <ChevronDown aria-hidden="true" className="h-4 w-4 shrink-0 text-pc-gray-600" />
      </button>

      {open && (
        <div
          dir="rtl"
          className="absolute z-50 mt-1 w-full rounded-pc-md border border-pc-gray-400 bg-white shadow-pc-float"
        >
          <div className="border-b border-pc-gray-200 p-2">
            <input
              ref={searchRef}
              type="search"
              role="combobox"
              aria-expanded="true"
              aria-controls={listId}
              aria-autocomplete="list"
              aria-activedescendant={activeExists ? optionId(activeIndex) : undefined}
              value={query}
              onChange={e => {
                setQuery(e.target.value)
                setActiveIndex(0)
              }}
              onKeyDown={onKeyDown}
              aria-label="ابحث داخل الخيارات"
              placeholder="ابحث…"
              className="w-full rounded-pc-md border border-pc-gray-400 px-3 py-2 text-base focus:border-pc-brand-600 focus:outline-none focus:ring-2 focus:ring-pc-brand-600"
            />
          </div>

          {filtered.length === 0 ? (
            <p className="flex items-center gap-2 px-3 py-4 text-sm text-pc-gray-600">
              <SearchX aria-hidden="true" className="h-4 w-4 shrink-0" />
              لا توجد نتائج مطابقة
            </p>
          ) : (
            <ul id={listId} role="listbox" aria-label="الخيارات" className="max-h-60 overflow-y-auto py-1">
              {filtered.map((o, i) => (
                <li
                  key={o.value}
                  id={optionId(i)}
                  role="option"
                  aria-selected={o.value === value}
                  aria-disabled={o.disabled || undefined}
                  onMouseEnter={() => setActiveIndex(i)}
                  // منع سرقة التركيز من حقل البحث عند النقر بالفأرة.
                  onMouseDown={e => e.preventDefault()}
                  onClick={() => pick(o)}
                  onKeyDown={e => { if (e.key === 'Enter') pick(o) }}
                  className={`flex min-h-[2.75rem] cursor-pointer items-center justify-between gap-2 px-3 py-2 text-start text-sm ${
                    i === activeIndex ? 'bg-pc-brand-50' : ''
                  } ${o.value === value ? 'font-semibold text-pc-brand-700' : 'text-pc-gray-800'} ${
                    o.disabled ? 'cursor-not-allowed opacity-40' : ''
                  }`}
                >
                  <span>{o.label}</span>
                  {o.value === value && <Check aria-hidden="true" className="h-4 w-4 shrink-0" />}
                </li>
              ))}
            </ul>
          )}

          {/* نتائج البحث تُعلَن ديناميكيًا لقارئ الشاشة. */}
          <span aria-live="polite" className="sr-only">
            {filtered.length === 0 ? 'لا توجد نتائج مطابقة' : `${filtered.length} خيار متاح`}
          </span>
        </div>
      )}
    </div>
  )
}
