type DateValue = string | Date | null | undefined

const arabicDateFormatter = new Intl.DateTimeFormat('ar-SA-u-ca-gregory-nu-latn', {
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
})

/**
 * قيم الأعمدة من نوع DATE تصل «yyyy-MM-dd» أو منتصف ليل UTC («…T00:00:00.000Z») — هي تاريخ تقويمي لا لحظة،
 * فتُقرأ كتاريخ محلي بنفس اليوم في أي منطقة زمنية (كانت تظهر باليوم السابق في المناطق ذات الإزاحة السالبة).
 * في السعودية (UTC+3) النتيجة مطابقة لما سبق، والطوابع الزمنية الكاملة تبقى بالتوقيت المحلي كما كانت.
 */
const CALENDAR_DATE = /^(\d{4})-(\d{2})-(\d{2})(?:T00:00:00(?:\.0+)?Z)?$/

export function parseCalendarDate(value: string): Date | null {
  const m = CALENDAR_DATE.exec(value)
  return m ? new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3])) : null
}

function toDate(value: DateValue) {
  if (!value) return null
  const date = value instanceof Date ? value : parseCalendarDate(value) ?? new Date(value)
  return Number.isNaN(date.getTime()) ? null : date
}

/**
 * المنسّق العربي يُدرج علامات اتجاه خفية (U+200F/U+061C) بين أجزاء التاريخ.
 * مع الأرقام اللاتينية داخل عنصر dir="ltr" تعيد هذه العلامات ترتيب الأجزاء فيظهر «162026/02/».
 * تُحذف العلامات فيبقى «16/02/2026».
 */
function formatDate(date: Date) {
  return arabicDateFormatter.format(date).replace(/[‎‏؜]/g, '')
}

export function formatArabicDate(value: DateValue) {
  const date = toDate(value)
  return date ? formatDate(date) : '—'
}

export function BidiText({
  children,
  direction = 'auto',
  className = '',
}: Readonly<{
  children: React.ReactNode
  direction?: 'auto' | 'ltr' | 'rtl'
  className?: string
}>) {
  return (
    <bdi dir={direction} className={className}>
      {children}
    </bdi>
  )
}

export function DateText({ value, className = '' }: Readonly<{ value: DateValue; className?: string }>) {
  const date = toDate(value)
  return (
    <time
      dateTime={date?.toISOString()}
      dir="ltr"
      className={`inline-block whitespace-nowrap text-start [unicode-bidi:isolate] ${className}`}
    >
      {date ? formatDate(date) : '—'}
    </time>
  )
}

export function DateRange({
  start,
  end,
  className = '',
}: Readonly<{
  start: DateValue
  end: DateValue
  className?: string
}>) {
  return (
    <span className={`inline-flex flex-wrap items-center gap-1 ${className}`}>
      <DateText value={start} />
      <span aria-hidden="true" className="text-pc-gray-400">
        —
      </span>
      <DateText value={end} />
    </span>
  )
}
