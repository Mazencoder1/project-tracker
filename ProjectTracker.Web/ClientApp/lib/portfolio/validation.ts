
import type {
  DecisionStatus,
  ProjectUpdateInput,
  ReportedHealth,
  RiskInput,
  RiskProbability,
  RiskSeverity,
  RiskStatus,
  RiskType,
  ValidationResult,
} from './types'

export type { ValidationResult } from './types'

const REPORTED_HEALTH: readonly ReportedHealth[] = ['on_track', 'needs_attention', 'delayed', 'at_risk', 'stopped']
const DECISION_STATUS: readonly DecisionStatus[] = ['new', 'under_review', 'taken']
const RISK_TYPE: readonly RiskType[] = ['risk', 'issue', 'blocker']
const RISK_SEVERITY: readonly RiskSeverity[] = ['low', 'medium', 'high', 'critical']
const RISK_PROBABILITY: readonly RiskProbability[] = ['low', 'medium', 'high']
const RISK_STATUS: readonly RiskStatus[] = ['open', 'mitigating', 'closed']

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const DATE_RE = /^(\d{4})-(\d{2})-(\d{2})$/

type Obj = Record<string, unknown>

function asObject(input: unknown): Obj {
  return input !== null && typeof input === 'object' && !Array.isArray(input) ? (input as Obj) : {}
}

function has(obj: Obj, key: string): boolean {
  return Object.hasOwn(obj, key) && obj[key] !== undefined
}

/** نص مقلَّم أو null (القيم غير النصية/الرقمية تُعامل كفارغة). */
function text(v: unknown): string | null {
  if (typeof v !== 'string' && typeof v !== 'number') return null
  const s = String(v).trim()
  return s === '' ? null : s
}

/** تاريخ YYYY-MM-DD صالح (يقبل أيضًا ISO كامل ويأخذ أول 10 أحرف)، أو undefined إن كان غير صالح. */
function dateValue(v: unknown): string | null | undefined {
  const s = text(v)
  if (s === null) return null
  const candidate = s.length > 10 && /^\d{4}-\d{2}-\d{2}T/.test(s) ? s.slice(0, 10) : s
  const m = DATE_RE.exec(candidate)
  if (!m) return undefined
  const y = Number(m[1])
  const mo = Number(m[2])
  const d = Number(m[3])
  const dt = new Date(Date.UTC(y, mo - 1, d))
  if (dt.getUTCFullYear() !== y || dt.getUTCMonth() !== mo - 1 || dt.getUTCDate() !== d) return undefined
  return candidate
}

/** عدد صحيح أو null (فارغ)، أو undefined إن كان غير صالح. */
function intValue(v: unknown): number | null | undefined {
  if (v === null || v === undefined) return null
  if (typeof v === 'string') {
    const s = v.trim()
    if (s === '') return null
    if (!/^-?\d+$/.test(s)) return undefined
    return Number(s)
  }
  if (typeof v === 'number' && Number.isInteger(v)) return v
  return undefined
}

function enumValue<T extends string>(v: unknown, allowed: readonly T[]): T | null | undefined {
  const s = text(v)
  if (s === null) return null
  return (allowed as readonly string[]).includes(s) ? (s as T) : undefined
}

function uuidValue(v: unknown): string | null | undefined {
  const s = text(v)
  if (s === null) return null
  return UUID_RE.test(s) ? s : undefined
}

function result<T>(value: T, errors: Record<string, string>): ValidationResult<T> {
  return Object.keys(errors).length > 0 ? { ok: false, errors } : { ok: true, value }
}

const MSG = {
  required: 'هذا الحقل مطلوب',
  date: 'تاريخ غير صالح',
  uuid: 'معرّف غير صالح',
  choice: 'قيمة غير مسموح بها',
  percent: 'يجب أن تكون نسبة صحيحة بين 0 و100',
  integer: 'يجب أن يكون عددًا صحيحًا',
}

function maxLen(errors: Record<string, string>, field: string, value: string | null, max: number) {
  if (value !== null && value.length > max) errors[field] = `الحد الأقصى ${max} حرف`
}

// ---------------------------------------------------------------------------
// التحديث الأسبوعي
// ---------------------------------------------------------------------------

export function validateProjectUpdate(input: unknown, today: string): ValidationResult<ProjectUpdateInput> {
  const o = asObject(input)
  const errors: Record<string, string> = {}

  const reporting = dateValue(o.reporting_date)
  if (reporting === null) errors.reporting_date = 'تاريخ التقرير مطلوب'
  else if (reporting === undefined) errors.reporting_date = MSG.date
  else if (reporting > today) errors.reporting_date = 'لا يمكن أن يكون تاريخ التقرير بعد اليوم'

  const percent = (field: 'actual_progress' | 'planned_progress'): number | null => {
    const v = intValue(o[field])
    if (v === undefined || (v !== null && (v < 0 || v > 100))) {
      errors[field] = MSG.percent
      return null
    }
    return v
  }
  const actual = percent('actual_progress')
  const planned = percent('planned_progress')

  const health = enumValue(o.health_status, REPORTED_HEALTH)
  if (health === null) errors.health_status = 'حالة الصحة مطلوبة'
  else if (health === undefined) errors.health_status = MSG.choice

  const delayReason = text(o.delay_reason)
  if ((health === 'delayed' || health === 'at_risk' || health === 'stopped') && !delayReason) {
    errors.delay_reason = 'سبب التأخير أو التعثر مطلوب لهذه الحالة'
  }

  const summary = text(o.executive_summary)
  maxLen(errors, 'executive_summary', summary, 2000)

  const optionalDate = (field: string): string | null => {
    const v = dateValue(o[field])
    if (v === undefined) {
      errors[field] = MSG.date
      return null
    }
    return v
  }
  const forecast = optionalDate('forecast_end_date')
  const supportDue = optionalDate('support_due_date')

  const decisionRequired = text(o.decision_required)
  const decisionByRaw = dateValue(o.decision_needed_by)
  let decisionBy: string | null = null
  if (decisionByRaw === undefined) errors.decision_needed_by = MSG.date
  else decisionBy = decisionByRaw
  if (decisionRequired && decisionByRaw === null) {
    errors.decision_needed_by = 'حدد موعد القرار المطلوب'
  }

  let decisionStatus = enumValue(o.decision_status, DECISION_STATUS)
  if (decisionStatus === undefined) {
    errors.decision_status = MSG.choice
    decisionStatus = null
  }
  if (decisionRequired && decisionStatus === null) decisionStatus = 'new'

  const milestone = (field: string): string | null => {
    const v = uuidValue(o[field])
    if (v === undefined) {
      errors[field] = MSG.uuid
      return null
    }
    return v
  }
  const currentMilestone = milestone('current_milestone_id')
  const nextMilestone = milestone('next_milestone_id')

  const supportFrom = text(o.support_from)
  const decisionFrom = text(o.decision_from)
  maxLen(errors, 'support_from', supportFrom, 200)
  maxLen(errors, 'decision_from', decisionFrom, 200)

  return result<ProjectUpdateInput>(
    {
      reporting_date: reporting ?? '',
      executive_summary: summary,
      actual_progress: actual,
      planned_progress: planned,
      health_status: (health ?? 'on_track') as ReportedHealth,
      current_milestone_id: currentMilestone,
      next_milestone_id: nextMilestone,
      forecast_end_date: forecast,
      delay_reason: delayReason,
      support_required: text(o.support_required),
      support_from: supportFrom,
      support_impact: text(o.support_impact),
      support_due_date: supportDue,
      decision_required: decisionRequired,
      decision_from: decisionFrom,
      decision_needed_by: decisionBy,
      decision_status: decisionStatus,
    },
    errors
  )
}

// ---------------------------------------------------------------------------
// المخاطر والتعثرات
// ---------------------------------------------------------------------------

interface FieldContext {
  o: Obj
  partial: boolean
  errors: Record<string, string>
  value: Record<string, unknown>
}

const shouldCheck = (c: FieldContext, key: string) => !c.partial || has(c.o, key)

function checkChoice(c: FieldContext, key: string, allowed: readonly string[], requiredMessage?: string) {
  if (!shouldCheck(c, key)) return
  const v = enumValue(c.o[key], allowed)
  if (v === null && requiredMessage) c.errors[key] = requiredMessage
  else if (v === undefined) c.errors[key] = MSG.choice
  else c.value[key] = v
}

function checkRiskStatus(c: FieldContext) {
  if (!shouldCheck(c, 'status')) return
  const v = enumValue(c.o.status, RISK_STATUS)
  if (v === undefined) c.errors.status = MSG.choice
  else if (v !== null) c.value.status = v
  else if (c.partial) c.errors.status = 'الحالة مطلوبة'
  else c.value.status = 'open'
}

function checkTitle(c: FieldContext) {
  if (!shouldCheck(c, 'title')) return
  const v = text(c.o.title)
  if (v === null) c.errors.title = 'العنوان مطلوب'
  else if (v.length < 3) c.errors.title = 'العنوان قصير جدًا (3 أحرف على الأقل)'
  else if (v.length > 300) c.errors.title = 'الحد الأقصى 300 حرف'
  else c.value.title = v
}

function checkDate(c: FieldContext, key: string) {
  if (!shouldCheck(c, key)) return
  const v = dateValue(c.o[key])
  if (v === undefined) c.errors[key] = MSG.date
  else c.value[key] = v
}

function checkUuid(c: FieldContext, key: string) {
  if (!shouldCheck(c, key)) return
  const v = uuidValue(c.o[key])
  if (v === undefined) c.errors[key] = MSG.uuid
  else c.value[key] = v
}

export function validateRisk(
  input: unknown,
  options: { partial?: boolean } = {}
): ValidationResult<Partial<RiskInput>> {
  const c: FieldContext = { o: asObject(input), partial: !!options.partial, errors: {}, value: {} }

  checkChoice(c, 'type', RISK_TYPE, 'النوع مطلوب')
  checkTitle(c)
  checkChoice(c, 'severity', RISK_SEVERITY, 'الخطورة مطلوبة')
  checkChoice(c, 'probability', RISK_PROBABILITY)
  checkRiskStatus(c)
  checkDate(c, 'due_date')
  checkUuid(c, 'owner_id')
  checkUuid(c, 'milestone_id')
  if (shouldCheck(c, 'description')) c.value.description = text(c.o.description)
  if (shouldCheck(c, 'mitigation')) c.value.mitigation = text(c.o.mitigation)

  return result(c.value as Partial<RiskInput>, c.errors)
}

// ---------------------------------------------------------------------------
// قرارات وإجراءات الاجتماع
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// إعدادات اللوحة
// ---------------------------------------------------------------------------

