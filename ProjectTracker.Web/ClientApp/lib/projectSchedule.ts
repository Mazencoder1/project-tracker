
/** الحد الأعلى المعقول لمدة مشروع (10 سنوات). */
const MAX_PROJECT_WEEKS = 520
/** يطابق projects.name NVARCHAR(200). */
export const PROJECT_NAME_MAX = 200

const DAY_MS = 86_400_000

type ScheduleField = 'start_date' | 'end_date' | 'total_weeks'
export type ScheduleErrors = Partial<Record<ScheduleField, string>>

export interface ProjectSchedule {
  start_date: string | null
  end_date: string | null
  total_weeks: number | null
}

/** يحوّل الأرقام الهندية والفارسية إلى لاتينية (لوحات المفاتيح العربية تكتبها أحيانًا). */
function normalizeDigits(value: string): string {
  return value
    .replace(/[٠-٩]/g, d => String(d.codePointAt(0)! - 0x0660))
    .replace(/[۰-۹]/g, d => String(d.codePointAt(0)! - 0x06f0))
}

/** تاريخ حقيقي بصيغة YYYY-MM-DD (يرفض 2026-02-30 مثلًا). */
function isIsoDate(value: unknown): value is string {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false
  const d = new Date(`${value}T00:00:00Z`)
  return !Number.isNaN(d.getTime()) && d.toISOString().slice(0, 10) === value
}

/**
 * عدد الأسابيع التي يغطيها المشروع من يوم بدايته حتى يوم نهايته شاملًا اليومين — نفس تقسيم
 * الأسابيع في lib/timeline.ts (الأسبوع N يبدأ بعد 7×(N-1) يومًا من البداية).
 * يعيد null إن كان أحد التاريخين غير صالح أو النهاية قبل البداية.
 */
export function calcProjectWeeks(startDate: string, endDate: string): number | null {
  if (!isIsoDate(startDate) || !isIsoDate(endDate)) return null
  const days = (Date.parse(`${endDate}T00:00:00Z`) - Date.parse(`${startDate}T00:00:00Z`)) / DAY_MS + 1
  if (days < 1) return null
  return Math.ceil(days / 7)
}

function isEmpty(value: unknown): boolean {
  return value === undefined || value === null || (typeof value === 'string' && value.trim() === '')
}

/** عدد الأسابيع من رقم أو نص (أرقام عربية مقبولة)، أو NaN إن لم يكن عددًا صحيحًا. */
function parseWeeks(input: unknown): number {
  const raw = typeof input === 'string' ? normalizeDigits(input.trim()) : input
  if (typeof raw === 'number') return raw
  return typeof raw === 'string' && /^-?\d+$/.test(raw) ? Number(raw) : Number.NaN
}

/**
 * يتحقق من التاريخين وعدد الأسابيع ويعيد القيم المنظّفة. الحقل الفارغ = null (اختياري).
 * رسائل الأخطاء تشرح كيف يُصحَّح الحقل لأنها تُعرض كما هي عند الحقل في الواجهة.
 */
export function validateProjectSchedule(input: {
  start_date?: unknown
  end_date?: unknown
  total_weeks?: unknown
}): { value: ProjectSchedule; errors: ScheduleErrors } {
  const errors: ScheduleErrors = {}
  const value: ProjectSchedule = { start_date: null, end_date: null, total_weeks: null }

  if (!isEmpty(input.start_date)) {
    if (isIsoDate(input.start_date)) value.start_date = input.start_date
    else errors.start_date = 'تاريخ البداية غير صالح — اختر التاريخ من التقويم.'
  }
  if (!isEmpty(input.end_date)) {
    if (isIsoDate(input.end_date)) value.end_date = input.end_date
    else errors.end_date = 'تاريخ النهاية غير صالح — اختر التاريخ من التقويم.'
  }
  if (value.start_date && value.end_date && value.end_date < value.start_date) {
    errors.end_date = 'تاريخ النهاية يسبق تاريخ البداية — اختر يوم البداية نفسه أو يومًا بعده.'
  }

  if (!isEmpty(input.total_weeks)) {
    const weeks = parseWeeks(input.total_weeks)
    if (Number.isInteger(weeks) && weeks >= 1 && weeks <= MAX_PROJECT_WEEKS) value.total_weeks = weeks
    else errors.total_weeks = `عدد الأسابيع يجب أن يكون رقمًا صحيحًا من 1 إلى ${MAX_PROJECT_WEEKS}.`
  }

  return { value, errors }
}
