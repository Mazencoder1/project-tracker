'use client'

import { useMemo, useState } from 'react'
import { ArrowDown, ArrowUp, ArrowUpDown } from 'lucide-react'
import { Alert } from './Alert'
import { Button } from './Button'
import { EmptyState, TableSkeleton } from './Feedback'
import { PAGINATION_THRESHOLD, Pagination, usePagination } from './Pagination'
import type { CountUnit } from '@/lib/arabicCount'

export type TableSort = { key: string; dir: 'asc' | 'desc' }

function ariaSortValue(dir: TableSort['dir']) {
  return dir === 'asc' ? 'ascending' : 'descending'
}

function SortIcon({ active, dir }: Readonly<{ active: boolean; dir?: TableSort['dir'] }>) {
  if (!active) return <ArrowUpDown aria-hidden="true" className="h-4 w-4 text-pc-gray-500" />
  return dir === 'asc' ? <ArrowUp aria-hidden="true" className="h-4 w-4" /> : <ArrowDown aria-hidden="true" className="h-4 w-4" />
}

function sortHint(active: boolean, dir?: TableSort['dir']) {
  if (!active) return '(اضغط للفرز)'
  return dir === 'asc' ? '(مفروز تصاعديًا)' : '(مفروز تنازليًا)'
}

/**
 * وضع التصفّح من الخادم. `rows` حينها هي الصفحة الحالية فقط، والعدد الكلي
 * ورقم الصفحة يأتيان من الرد (انظر lib/useServerList.ts). الفرز من رأس الجدول يُمرَّر للخادم عبر
 * onSortChange — فرز الصفحة الحالية وحدها كان سيوهم بترتيب لا يشمل بقية الصفحات، لذلك بلا
 * onSortChange تُعطَّل أزرار الفرز في هذا الوضع.
 */
export interface ServerTablePaging {
  page: number
  pageSize: number
  total: number
  onPageChange: (page: number) => void
  sort?: TableSort | null
  onSortChange?: (sort: TableSort) => void
  /** جلب صفحة جديدة جارٍ: الصفوف الحالية تبقى معروضة بشفافية بدل هيكل التحميل. */
  refreshing?: boolean
}

export interface Column<T> {
  key: string
  header: string
  /** عرض الخلية. */
  cell: (row: T) => React.ReactNode
  /** قيمة الفرز — وجودها يجعل العمود قابلًا للفرز. */
  sortValue?: (row: T) => string | number
  /** عمود الإجراءات: يُثبَّت في أقصى اليسار (نهاية الصف في RTL) ولا يُفرز. */
  isActions?: boolean
  /** إخفاء العمود من بطاقة الجوال (مثال: عمود تكراري). */
  hideOnCard?: boolean
  className?: string
}

interface TableProps<T> {
  /** عنوان وصفي للجدول — إلزامي لقارئ الشاشة (caption). */
  caption: string
  columns: Column<T>[]
  rows: T[]
  rowKey: (row: T) => string
  loading?: boolean
  error?: string
  onRetry?: () => void
  emptyTitle?: string
  emptyDescription?: string
  emptyAction?: React.ReactNode
  /** حجم الصفحة — إرشادات التصميم يفرض تصفّحًا فوق 25 صفًا. */
  pageSize?: number
  /** عنوان البطاقة على الجوال (مفتاح العمود المستخدَم كعنوان). */
  cardTitleKey?: string
  className?: string
  /** false عند وضع الجدول داخل بطاقة لها رأس خاص (منع الحدّ المزدوج). */
  framed?: boolean
  /** عدد أعمدة البطاقات من md حتى xl (عمودان يختصران طول الصفحة على التابلت). */
  cardColumns?: 1 | 2
  /** تصفّح من الخادم بدل تقسيم `rows` محليًا. */
  server?: ServerTablePaging
  /** وحدة العدّ في شريط التصفّح («إجمالي 30 مشروعًا»). */
  unit?: CountUnit
}

export function Table<T>({
  caption,
  columns,
  rows,
  rowKey,
  loading = false,
  error,
  onRetry,
  emptyTitle = 'لا توجد بيانات لعرضها',
  emptyDescription,
  emptyAction,
  pageSize = PAGINATION_THRESHOLD,
  cardTitleKey,
  className = '',
  framed = true,
  cardColumns = 1,
  server,
  unit,
}: Readonly<TableProps<T>>) {
  const frame = framed ? 'rounded-pc-md border border-pc-gray-200 bg-white shadow-pc-card' : 'bg-white'

  const [localSort, setLocalSort] = useState<TableSort | null>(null)
  const sort = server ? server.sort ?? null : localSort

  const sorted = useMemo(() => {
    if (server || !sort) return rows
    const col = columns.find(c => c.key === sort.key)
    if (!col?.sortValue) return rows
    const copy = [...rows]
    copy.sort((a, b) => {
      const av = col.sortValue!(a)
      const bv = col.sortValue!(b)
      if (av === bv) return 0
      const res = av > bv ? 1 : -1
      return sort.dir === 'asc' ? res : -res
    })
    return copy
  }, [rows, sort, columns, server])

  const local = usePagination(sorted, pageSize, PAGINATION_THRESHOLD)
  // وضع الخادم: الصفوف هي الصفحة نفسها، والعدد والصفحات من الرد.
  const { visible, page, pageCount, setPage, paginated, total } = server
    ? {
        visible: rows,
        page: server.page,
        pageCount: Math.max(1, Math.ceil(server.total / server.pageSize)),
        setPage: server.onPageChange,
        paginated: server.total > server.pageSize,
        total: server.total,
      }
    : local
  const canSort = !server || !!server.onSortChange

  function toggleSort(key: string) {
    const next: TableSort =
      sort?.key === key ? { key, dir: sort.dir === 'asc' ? 'desc' : 'asc' } : { key, dir: 'asc' }
    if (server) {
      // تغيير الفرز يغيّر الرابط في useServerList فيعود للصفحة الأولى تلقائيًا.
      server.onSortChange?.(next)
      return
    }
    setPage(0)
    setLocalSort(next)
  }

  if (error) {
    return (
      <Alert
        tone="danger"
        title="تعذّر تحميل البيانات"
        action={
          onRetry ? (
            <Button variant="secondary" size="sm" onClick={onRetry}>
              إعادة المحاولة
            </Button>
          ) : undefined
        }
      >
        {error}
      </Alert>
    )
  }

  if (loading) {
    return (
      <div className={frame}>
        <TableSkeleton cols={columns.length} />
      </div>
    )
  }

  if (rows.length === 0) {
    return (
      <div className={frame}>
        <EmptyState title={emptyTitle} description={emptyDescription} action={emptyAction} />
      </div>
    )
  }

  return (
    <div
      className={`${className} ${server?.refreshing ? 'opacity-60 transition-opacity' : ''}`}
      aria-busy={server?.refreshing || undefined}
    >
      {/* ── سطح المكتب الواسع: جدول دلالي كامل (≥1280px) ───────────────────── */}
      <div className={`hidden overflow-clip xl:block ${frame}`}>
        <div>
          <table className="w-full border-collapse text-start text-sm">
            <caption className="sr-only">{caption}</caption>
            <thead className="sticky top-0 z-10 bg-pc-brand-50">
              <tr>
                {columns.map(col => {
                  const isSorted = sort?.key === col.key
                  const sortable = canSort && !!col.sortValue && !col.isActions
                  return (
                    <th
                      key={col.key}
                      scope="col"
                      aria-sort={isSorted ? ariaSortValue(sort!.dir) : undefined}
                      className={`border-b border-pc-gray-200 px-pc-s py-pc-s text-start font-semibold text-pc-brand-900 ${
                        col.isActions ? 'text-end' : ''
                      } ${col.className ?? ''}`}
                    >
                      {sortable ? (
                        <button
                          type="button"
                          onClick={() => toggleSort(col.key)}
                          className="inline-flex min-h-[2.5rem] items-center gap-1 rounded-pc-md px-1 hover:text-pc-brand-700 focus:outline-none focus-visible:ring-2 focus-visible:ring-pc-brand-600"
                        >
                          {col.header}
                          <SortIcon active={isSorted} dir={sort?.dir} />
                          <span className="sr-only">{sortHint(isSorted, sort?.dir)}</span>
                        </button>
                      ) : (
                        col.header
                      )}
                    </th>
                  )
                })}
              </tr>
            </thead>
            <tbody>
              {visible.map((row, i) => (
                <tr
                  key={rowKey(row)}
                  className={`border-b border-pc-gray-100 last:border-b-0 ${
                    i % 2 === 1 ? 'bg-pc-gray-50' : 'bg-white'
                  }`}
                >
                  {columns.map(col => (
                    <td
                      key={col.key}
                      className={`px-pc-s py-pc-s align-middle text-pc-gray-800 ${
                        col.isActions ? 'text-end' : ''
                      } ${col.className ?? ''}`}
                    >
                      {col.cell(row)}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* ── الجوال (<768px): بطاقات بدل تمرير أفقي قسري ── */}
      <ul className={`grid grid-cols-1 gap-pc-s xl:hidden ${cardColumns === 2 ? 'md:grid-cols-2' : ''} ${framed ? '' : 'p-pc-s'}`}>
        {visible.map(row => {
          const titleCol = cardTitleKey ? columns.find(c => c.key === cardTitleKey) : columns[0]
          const bodyCols = columns.filter(
            c => c.key !== titleCol?.key && !c.isActions && !c.hideOnCard
          )
          const actionsCol = columns.find(c => c.isActions)
          return (
            <li
              key={rowKey(row)}
              className="rounded-pc-md border border-pc-gray-200 bg-white p-pc-m shadow-pc-card"
            >
              {titleCol && (
                <p className="text-base font-semibold text-pc-gray-900">{titleCol.cell(row)}</p>
              )}
              <dl className="mt-pc-s space-y-pc-xs">
                {bodyCols.map(c => (
                  <div key={c.key} className="flex items-start justify-between gap-pc-s">
                    <dt className="shrink-0 text-sm text-pc-gray-600">{c.header}</dt>
                    <dd className="min-w-0 text-start text-sm text-pc-gray-900">{c.cell(row)}</dd>
                  </div>
                ))}
              </dl>
              {actionsCol && (
                <div className="mt-pc-s flex flex-wrap items-center gap-pc-xs border-t border-pc-gray-100 pt-pc-s">
                  {actionsCol.cell(row)}
                </div>
              )}
            </li>
          )
        })}
      </ul>

      {/* ── التصفّح: إلزامي فوق 25 صفًا (شريط مشترك — Pagination.tsx) ──────── */}
      {paginated && (
        <Pagination
          page={page}
          pageCount={pageCount}
          total={total}
          onPageChange={setPage}
          unit={unit}
          ariaLabel={`تصفّح صفحات ${caption}`}
        />
      )}

    </div>
  )
}
