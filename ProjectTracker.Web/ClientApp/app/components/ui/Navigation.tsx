'use client'

import Link from 'next/link'
import { Check, ChevronLeft } from 'lucide-react'

/* ────────────────────────── رابط التخطي للمحتوى ────────────────────────────: «رابط "التخطي إلى المحتوى الرئيسي" كأول عنصر قابل للتركيز في كل صفحة».
   التنسيق في app/globals.css تحت .pc-skip-link. */
export function SkipLink({ targetId = 'main-content' }: Readonly<{ targetId?: string }>) {
  return (
    <a href={`#${targetId}`} className="pc-skip-link">
      التخطي إلى المحتوى الرئيسي
    </a>
  )
}

/* ──────────────────────────────── Tabs ─────────────────────────────────────: «≤ 4 على الجوال، التبويب النشط متمايز بوضوح، تغيير التبويب لا يفقد
   بيانات مُدخلة». المكوّن لا يفكّ تركيب المحتوى (المستدعي يتحكّم بالإظهار)
   حتى لا تُفقد حالة النماذج — لذلك نمط tablist/tab مع aria-controls فقط. */
export interface TabItem {
  id: string
  label: string
  icon?: React.ReactNode
  /** عدّاد اختياري بجانب التسمية. */
  count?: number
}

/**
 * معالج لوحة مفاتيح مشترك لكل
 * tablist في النظام (Tabs هنا + لوحتا تفاصيل المرحلة في meeting/pm). كانت الأسهم تغيّر
 * التحديد بينما يبقى التركيز على التبويب القديم الذي صار tabIndex=-1، فيضيع مستخدم
 * لوحة المفاتيح. الآن: تغيير التحديد + focus() على التبويب الجديد، ودعم Home/End.
 * الاتجاه RTL: السهم الأيسر يتقدّم للتالي، والأيمن يرجع للسابق.
 * @param tabDomId دالة تعيد id عنصر التبويب في DOM لكل مفتاح.
 */
export function handleTabListKeyDown<K extends string>(
  e: React.KeyboardEvent,
  ids: readonly K[],
  active: K,
  onChange: (id: K) => void,
  tabDomId: (id: K) => string
) {
  if (ids.length === 0) return
  const i = Math.max(0, ids.indexOf(active))
  let next: number | null = null
  if (e.key === 'ArrowLeft') next = (i + 1) % ids.length
  else if (e.key === 'ArrowRight') next = (i - 1 + ids.length) % ids.length
  else if (e.key === 'Home') next = 0
  else if (e.key === 'End') next = ids.length - 1
  if (next === null) return
  e.preventDefault()
  const id = ids[next]
  onChange(id)
  // التبويب موجود مسبقًا في DOM (لا يُعاد تركيبه)، فالتركيز الفوري آمن.
  document.getElementById(tabDomId(id))?.focus()
}

export function Tabs({
  items,
  active,
  onChange,
  ariaLabel = 'أقسام الصفحة',
  className = '',
}: Readonly<{
  items: TabItem[]
  active: string
  onChange: (id: string) => void
  ariaLabel?: string
  className?: string
}>) {
  function onKeyDown(e: React.KeyboardEvent) {
    // RTL: السهم الأيسر يتقدّم، الأيمن يرجع — ومعها Home/End ونقل التركيز.
    handleTabListKeyDown(e, items.map(t => t.id), active, onChange, id => `tab-${id}`)
  }

  return (
    <div
      role="tablist"
      tabIndex={-1}
      aria-label={ariaLabel}
      onKeyDown={onKeyDown}
      className={`flex flex-wrap gap-pc-xs border-b border-pc-gray-200 ${className}`}
    >
      {items.map(t => {
        const isActive = t.id === active
        return (
          <button
            key={t.id}
            role="tab"
            type="button"
            id={`tab-${t.id}`}
            aria-selected={isActive}
            aria-controls={`panel-${t.id}`}
            tabIndex={isActive ? 0 : -1}
            onClick={() => onChange(t.id)}
            className={`inline-flex min-h-[2.75rem] items-center gap-2 rounded-t-pc-md px-pc-m py-pc-s text-sm font-medium transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-pc-brand-600 ${
              isActive
                ? // تمايز بثلاث إشارات لا اللون وحده: خط سفلي + سماكة + خلفية.
                  'border-b-2 border-pc-brand-600 bg-pc-brand-50 font-semibold text-pc-brand-800'
                : 'border-b-2 border-transparent text-pc-gray-600 hover:bg-pc-gray-50 hover:text-pc-gray-900'
            }`}
          >
            {t.icon}
            {t.label}
            {typeof t.count === 'number' && (
              <span className="rounded-pc-md bg-white px-2 py-1 text-sm text-pc-gray-700">
                {t.count}
              </span>
            )}
          </button>
        )
      })}
    </div>
  )
}

/** غلاف لوحة التبويب — يربطها بزرّها ويُبقيها في DOM للحفاظ على المدخلات. */
export function TabPanel({
  id,
  active,
  children,
  className = '',
}: Readonly<{
  id: string
  active: boolean
  children: React.ReactNode
  className?: string
}>) {
  return (
    <div
      role="tabpanel"
      id={`panel-${id}`}
      aria-labelledby={`tab-${id}`}
      hidden={!active}
      tabIndex={0}
      className={className}
    >
      {children}
    </div>
  )
}

/* ───────────────────────────── Breadcrumb ──────────────────────────────── */
export function Breadcrumb({
  items,
  className = '',
}: Readonly<{
  items: { label: string; href?: string }[]
  className?: string
}>) {
  return (
    <nav aria-label="مسار التنقّل" className={className}>
      <ol className="flex flex-wrap items-center gap-1 text-sm text-pc-gray-600">
        {items.map((it, i) => {
          const last = i === items.length - 1
          return (
            <li key={`${it.label}-${i}`} className="flex items-center gap-1">
              {it.href && !last ? (
                <Link
                  href={it.href}
                  className="inline-flex min-h-[2.75rem] items-center rounded-pc-md underline-offset-2 hover:text-pc-brand-700 hover:underline md:min-h-6"
                >
                  {it.label}
                </Link>
              ) : (
                <span aria-current={last ? 'page' : undefined} className="font-medium text-pc-gray-900">
                  {it.label}
                </span>
              )}
              {!last && <ChevronLeft aria-hidden="true" className="h-4 w-4 text-pc-gray-600" />}
            </li>
          )
        })}
      </ol>
    </nav>
  )
}

/* ───────────────────────────── StepIndicator ───────────────────────────────: «الخطوة 2 من 4»، حالات: مكتملة / نشطة / قادمة / بها خطأ. */
type StepState = 'error' | 'done' | 'current' | 'upcoming'

const STEP_CIRCLE: Record<StepState, string> = {
  error: 'border-pc-error-600 bg-pc-error-600 text-white',
  done: 'border-pc-brand-600 bg-pc-brand-600 text-white',
  current: 'border-pc-brand-600 bg-white text-pc-brand-700',
  upcoming: 'border-pc-gray-400 bg-white text-pc-gray-600',
}

const STEP_LABEL: Record<StepState, string> = {
  error: 'بها خطأ',
  done: 'مكتملة',
  current: 'الخطوة الحالية',
  upcoming: 'قادمة',
}

function stepState(n: number, current: number, hasError: boolean): StepState {
  if (hasError) return 'error'
  if (n < current) return 'done'
  return n === current ? 'current' : 'upcoming'
}

function stepMark(state: StepState, n: number) {
  if (state === 'done') return <Check className="h-4 w-4" />
  return state === 'error' ? '!' : n
}

export function StepIndicator({
  steps,
  current,
  errorStep,
  className = '',
}: Readonly<{
  steps: string[]
  /** رقم الخطوة الحالية ابتداءً من 1. */
  current: number
  /** رقم خطوة بها خطأ (اختياري). */
  errorStep?: number
  className?: string
}>) {
  return (
    <div className={className}>
      <p aria-live="polite" className="text-sm font-medium text-pc-gray-700">
        الخطوة {current} من {steps.length}: {steps[current - 1]}
      </p>
      <ol className="mt-pc-s flex items-center gap-pc-xs">
        {steps.map((label, i) => {
          const n = i + 1
          const hasError = errorStep === n
          const state = stepState(n, current, hasError)
          return (
            <li key={label} className="flex flex-1 items-center gap-pc-xs">
              <span
                aria-hidden="true"
                className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-full border-2 text-sm font-bold ${
                  STEP_CIRCLE[state]
                }`}
              >
                {stepMark(state, n)}
              </span>
              <span className="sr-only">
                {label} — {STEP_LABEL[state]}
              </span>
              {i < steps.length - 1 && (
                <span
                  aria-hidden="true"
                  className={`h-1 flex-1 rounded-full ${state === 'done' ? 'bg-pc-brand-600' : 'bg-pc-gray-400'}`}
                />
              )}
            </li>
          )
        })}
      </ol>
    </div>
  )
}
