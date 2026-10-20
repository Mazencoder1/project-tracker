import { AppLogo } from '@/app/components/ui'
import { ORG_NAME } from '@/lib/org'

const REPORT_PAGE_WIDTH = 1120
const REPORT_PAGE_HEIGHT = 792

function formatReportDateTime(date: Date) {
  return date.toLocaleString('ar-SA-u-ca-gregory-nu-latn', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  }).replace(/[\u200e\u200f\u061c]/g, '')
}

export function reportFileDate(date: Date) {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

export function ReportPage({
  title,
  preparedAt,
  page,
  totalPages,
  sectionTitle,
  children,
}: Readonly<{
  title: string
  preparedAt: Date
  page: number
  totalPages: number
  sectionTitle?: string
  children: React.ReactNode
}>) {
  return (
    <section
      data-pdf-page="true"
      className="meeting-print-page relative overflow-hidden bg-white text-pc-gray-900"
      dir="rtl"
      style={{ width: REPORT_PAGE_WIDTH, height: REPORT_PAGE_HEIGHT, padding: '28px 32px 38px' }}
    >
      <header className="flex h-[70px] items-center justify-between gap-pc-m border-b-2 border-pc-brand-700 pb-pc-s">
        <div className="flex w-[300px] items-center gap-pc-s">
          <AppLogo variant="emblem" size={48} decorative />
          <div>
            <p className="text-base font-black text-pc-brand-900">{ORG_NAME}</p>
            <p className="text-sm text-pc-gray-600">منصة متابعة المشاريع</p>
          </div>
        </div>
        <h1 className="flex-1 text-center text-xl font-black leading-tight text-pc-brand-900">{title}</h1>
        <div className="w-[300px] text-left text-sm text-pc-gray-700" dir="rtl">
          <p className="font-semibold">تاريخ ووقت إعداد التقرير</p>
          <time className="tabular-nums" dateTime={preparedAt.toISOString()}>{formatReportDateTime(preparedAt)}</time>
        </div>
      </header>

      <main className="pt-pc-m">
        {sectionTitle && <h2 className="mb-pc-s text-lg font-black text-pc-brand-900">{sectionTitle}</h2>}
        {children}
      </main>

      <footer className="absolute inset-x-8 bottom-3 flex items-center justify-between border-t border-pc-gray-300 pt-2 text-xs text-pc-gray-700">
        <span>{ORG_NAME}</span>
        <span className="tabular-nums">صفحة {page} من {totalPages}</span>
      </footer>
    </section>
  )
}
