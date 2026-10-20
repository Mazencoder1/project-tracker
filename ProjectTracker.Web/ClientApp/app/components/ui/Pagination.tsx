'use client'

import { useMemo, useState } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { formatCount, type CountUnit } from '@/lib/arabicCount'
import { Button } from './Button'

/** الحد الذي يفرض إرشادات التصميم التصفّح فوقه في الجداول. */
export const PAGINATION_THRESHOLD = 25

export interface PaginationState<T> {
  /** صفوف الصفحة الحالية (أو كل الصفوف إن لم يتجاوز العدد الحد). */
  visible: T[]
  /** رقم الصفحة الحالي بعد القصّ داخل المدى — يبدأ من 0. */
  page: number
  pageCount: number
  setPage: (page: number) => void
  /** هل تجاوز العدد الحد فعُرض شريط التصفّح. */
  paginated: boolean
  total: number
}

/**
 * يقسّم مصفوفة جاهزة (بعد الفرز والتصفية) إلى صفحات.
 * @param rows      الصفوف كاملة
 * @param pageSize  عدد الصفوف في الصفحة
 * @param threshold العدد الذي يُفعَّل التصفّح فوقه (افتراضيًا = pageSize)
 */
export function usePagination<T>(
  rows: T[],
  pageSize: number = PAGINATION_THRESHOLD,
  threshold: number = pageSize
): PaginationState<T> {
  const [page, setPage] = useState(0)

  const total = rows.length
  const paginated = total > threshold
  const pageCount = paginated ? Math.max(1, Math.ceil(total / pageSize)) : 1
  // القصّ داخل المدى: تضييق المرشحات قد يترك الحالة على صفحة لم تعد موجودة.
  const safePage = Math.min(Math.max(page, 0), pageCount - 1)

  const visible = useMemo(
    () => (paginated ? rows.slice(safePage * pageSize, (safePage + 1) * pageSize) : rows),
    [rows, paginated, safePage, pageSize]
  )

  return { visible, page: safePage, pageCount, setPage, paginated, total }
}

interface PaginationProps {
  /** رقم الصفحة الحالي (0-based) — مرّر `page` العائد من usePagination لا الحالة الخام. */
  page: number
  pageCount: number
  total: number
  onPageChange: (page: number) => void
  /** وحدة العدّ العربية في «إجمالي …» (سجل/مشروع/موظف/مهمة...). */
  unit?: CountUnit
  /** تسمية الشريط — إلزامية للتمييز حين تتعدّد القوائم في الصفحة نفسها. */
  ariaLabel?: string
  className?: string
}

/**
 * أرقام الصفحات الظاهرة (0-based) مع فجوات: الأولى والأخيرة دائمًا، والجاران حول الحالية.
 * مثال (الحالية 5 من 12): 1 … 5 6 7 … 12.
 */
function pageWindow(page: number, pageCount: number): (number | 'gap')[] {
  const keep = new Set([0, pageCount - 1, page - 1, page, page + 1])
  const items: (number | 'gap')[] = []
  for (let i = 0; i < pageCount; i++) {
    if (keep.has(i)) items.push(i)
    else if (items.at(-1) !== 'gap') items.push('gap')
  }
  return items
}

export function Pagination({
  page,
  pageCount,
  total,
  onPageChange,
  unit = 'record',
  ariaLabel = 'تصفّح صفحات القائمة',
  className = '',
}: Readonly<PaginationProps>) {
  if (pageCount <= 1) return null

  return (
    <nav
      aria-label={ariaLabel}
      className={`mt-pc-s flex flex-wrap items-center justify-between gap-pc-s ${className}`}
    >
      <p aria-live="polite" className="text-sm text-pc-gray-600">
        الصفحة {page + 1} من {pageCount} — إجمالي {formatCount(total, unit)}
      </p>
      <div className="flex items-center gap-pc-xs">
        {/* Note: في RTL السهم يمين = السابق ويسار = التالي (اتجاه القراءة نفسه). */}
        <Button
          variant="secondary"
          size="sm"
          onClick={() => onPageChange(Math.max(0, page - 1))}
          disabled={page === 0}
          disabledReason="أنت في الصفحة الأولى"
          icon={<ChevronRight aria-hidden="true" className="h-4 w-4" />}
        >
          السابق
        </Button>
        <ol className="hidden items-center gap-pc-xs sm:flex">
          {pageWindow(page, pageCount).map((item, i) =>
            item === 'gap' ? (
              <li key={i === 1 ? 'gap-start' : 'gap-end'} aria-hidden="true" className="px-1 text-sm text-pc-gray-500">…</li>
            ) : (
              <li key={item}>
                <button
                  type="button"
                  onClick={() => onPageChange(item)}
                  aria-current={item === page ? 'page' : undefined}
                  aria-label={`الصفحة ${item + 1}`}
                  className={`inline-flex h-9 min-w-9 items-center justify-center rounded-pc-md border px-2 text-sm font-medium focus:outline-none focus-visible:ring-2 focus-visible:ring-pc-brand-600 ${
                    item === page
                      ? 'border-pc-brand-600 bg-pc-brand-600 text-white'
                      : 'border-pc-gray-300 bg-white text-pc-gray-800 hover:bg-pc-brand-50'
                  }`}
                >
                  {item + 1}
                </button>
              </li>
            )
          )}
        </ol>
        <Button
          variant="secondary"
          size="sm"
          onClick={() => onPageChange(Math.min(pageCount - 1, page + 1))}
          disabled={page >= pageCount - 1}
          disabledReason="أنت في الصفحة الأخيرة"
          icon={<ChevronLeft aria-hidden="true" className="h-4 w-4" />}
        >
          التالي
        </Button>
      </div>
    </nav>
  )
}
