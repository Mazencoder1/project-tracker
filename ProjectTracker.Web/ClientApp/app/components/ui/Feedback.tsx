'use client'

import { Inbox, Loader2 } from 'lucide-react'

export function EmptyState({
  title,
  description,
  action,
  icon,
  className = '',
}: Readonly<{
  title: string
  description?: string
  /** دعوة لإجراء — مطلوبة في إرشادات التصميم كلما كان هناك إجراء ممكن فعلًا. */
  action?: React.ReactNode
  icon?: React.ReactNode
  className?: string
}>) {
  return (
    <div
      className={`flex flex-col items-center justify-center gap-pc-s px-pc-m py-pc-xl text-center ${className}`}
    >
      <span className="flex h-12 w-12 items-center justify-center rounded-full bg-pc-brand-50 text-pc-brand-700">
        {icon ?? <Inbox aria-hidden="true" className="h-6 w-6" />}
      </span>
      <p className="text-base font-semibold text-pc-gray-900">{title}</p>
      {description && <p className="max-w-md text-sm text-pc-gray-600">{description}</p>}
      {action && <div className="mt-pc-xs">{action}</div>}
    </div>
  )
}

/** سطر/كتلة هيكلية للتحميل. aria-hidden لأنها زخرفية بحتة. */
export function Skeleton({ className = '' }: Readonly<{ className?: string }>) {
  return (
    <span
      aria-hidden="true"
      className={`block animate-pulse rounded-pc-md bg-pc-gray-200 ${className}`}
    />
  )
}

/** هيكل تحميل جدول (يُستخدمه Table تلقائيًا في حالة loading). */
export function TableSkeleton({ rows = 5, cols = 4 }: Readonly<{ rows?: number; cols?: number }>) {
  return (
    <div className="space-y-2 p-pc-m">
      <output className="sr-only">
        جارٍ تحميل البيانات
      </output>
      {skeletonKeys('row', rows).map(rowKey => (
        <div key={rowKey} className="flex gap-pc-s">
          {skeletonKeys(`${rowKey}-col`, cols).map(colKey => (
            <Skeleton key={colKey} className="h-6 flex-1" />
          ))}
        </div>
      ))}
    </div>
  )
}

/** مفاتيح ثابتة لعناصر الهيكل (لا يُعاد ترتيبها فلا حاجة لمعرّف حقيقي). */
function skeletonKeys(prefix: string, length: number): string[] {
  return Array.from({ length }, (_, n) => `${prefix}-${n}`)
}

/** هيكل تحميل بطاقات. */
export function CardSkeleton({ count = 3 }: Readonly<{ count?: number }>) {
  return (
    <div className="grid gap-pc-m sm:grid-cols-2 lg:grid-cols-3">
      <output className="sr-only">
        جارٍ تحميل البيانات
      </output>
      {skeletonKeys('card', count).map(cardKey => (
        <div
          key={cardKey}
          className="rounded-pc-md border border-pc-gray-200 bg-white p-pc-m shadow-pc-card"
        >
          <Skeleton className="h-5 w-2/3" />
          <Skeleton className="mt-pc-s h-4 w-full" />
          <Skeleton className="mt-2 h-4 w-4/5" />
        </div>
      ))}
    </div>
  )
}

export function Spinner({ label = 'جارٍ التحميل' }: Readonly<{ label?: string }>) {
  return (
    <output className="inline-flex items-center gap-2 text-sm text-pc-gray-600">
      <Loader2 aria-hidden="true" className="h-4 w-4 animate-spin" />
      {label}
    </output>
  )
}
