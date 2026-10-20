'use client'
import { useEffect, useMemo, useRef, useState } from 'react'
import { Paperclip, ShieldAlert } from 'lucide-react'
import {
  Alert,
  Button,
  FormErrorSummary,
  FormRequiredHint,
  Input,
  Modal,
  Select,
  Skeleton,
  TextArea,
  useToast,
} from '@/app/components/ui'
import {
  HEALTH,
  HEALTH_REQUIRES_REASON,
  REPORTED_HEALTH_ORDER,
} from '@/lib/portfolio/labels'
import type { ProjectUpdate, ReportedHealth } from '@/lib/portfolio/types'
import { validateProjectUpdate } from '@/lib/portfolio/validation'
import { ApiError, apiJson, errorMessage, todayIso } from './api'

interface MilestoneOption {
  id: string
  name: string
}

interface ProjectUpdateFormProps {
  projectId: string
  projectName: string
  milestones: MilestoneOption[]
  /** آخر تحديث معروف؛ undefined ← يجلبه النموذج بنفسه، null ← لا يوجد تحديث سابق. */
  previous?: ProjectUpdate | null
  onClose: () => void
  onSaved: (update: ProjectUpdate) => void
  /** يفتح نموذج تسجيل خطر/تعثر جديد (حقل «المخاطر أو التعثرات الجديدة»). */
  onRequestRisk?: () => void
}

type FormState = {
  reporting_date: string
  executive_summary: string
  actual_progress: string
  planned_progress: string
  health_status: ReportedHealth | ''
  current_milestone_id: string
  next_milestone_id: string
  forecast_end_date: string
  delay_reason: string
  support_required: string
  support_from: string
  support_impact: string
  support_due_date: string
}

const FIELD_ORDER: (keyof FormState)[] = [
  'reporting_date',
  'executive_summary',
  'actual_progress',
  'planned_progress',
  'health_status',
  'delay_reason',
  'current_milestone_id',
  'next_milestone_id',
  'forecast_end_date',
  'support_required',
  'support_from',
  'support_impact',
  'support_due_date',
]

const FIELD_LABEL: Record<keyof FormState, string> = {
  reporting_date: 'تاريخ التقرير',
  executive_summary: 'الملخص التنفيذي',
  actual_progress: 'الإنجاز الفعلي',
  planned_progress: 'الإنجاز المخطط',
  health_status: 'صحة التنفيذ',
  current_milestone_id: 'المرحلة الحالية',
  next_milestone_id: 'الإنجاز القادم',
  forecast_end_date: 'تاريخ الانتهاء المتوقع',
  delay_reason: 'سبب التأخير',
  support_required: 'الدعم المطلوب',
  support_from: 'الجهة المطلوب منها الدعم',
  support_impact: 'الأثر عند عدم توفير الدعم',
  support_due_date: 'تاريخ استحقاق الدعم',
}

const fid = (field: string) => `pu-${field}`

function fromPrevious(prev: ProjectUpdate | null): FormState {
  const s = (v: string | number | null | undefined) => (v === null || v === undefined ? '' : String(v))
  return {
    reporting_date: todayIso(),
    executive_summary: s(prev?.executive_summary),
    actual_progress: s(prev?.actual_progress),
    planned_progress: s(prev?.planned_progress),
    health_status: prev?.health_status ?? '',
    current_milestone_id: s(prev?.current_milestone_id),
    next_milestone_id: s(prev?.next_milestone_id),
    forecast_end_date: s(prev?.forecast_end_date).slice(0, 10),
    delay_reason: s(prev?.delay_reason),
    support_required: s(prev?.support_required),
    support_from: s(prev?.support_from),
    support_impact: s(prev?.support_impact),
    support_due_date: s(prev?.support_due_date).slice(0, 10),
  }
}

/** النصوص الفارغة ← null، والنسب ← أعداد (أو null). التقليم النهائي في validateProjectUpdate. */
function toPayload(projectId: string, f: FormState) {
  const text = (v: string) => (v.trim() === '' ? null : v)
  const num = (v: string) => (v.trim() === '' ? null : Number(v))
  return {
    project_id: projectId,
    reporting_date: f.reporting_date,
    executive_summary: text(f.executive_summary),
    actual_progress: num(f.actual_progress),
    planned_progress: num(f.planned_progress),
    health_status: f.health_status || null,
    current_milestone_id: text(f.current_milestone_id),
    next_milestone_id: text(f.next_milestone_id),
    forecast_end_date: text(f.forecast_end_date),
    delay_reason: text(f.delay_reason),
    support_required: text(f.support_required),
    support_from: text(f.support_from),
    support_impact: text(f.support_impact),
    support_due_date: text(f.support_due_date),
  }
}

export function ProjectUpdateForm({
  projectId,
  projectName,
  milestones,
  previous,
  onClose,
  onSaved,
  onRequestRisk,
}: Readonly<ProjectUpdateFormProps>) {
  const toast = useToast()
  const [loadingPrev, setLoadingPrev] = useState(previous === undefined)
  const [form, setForm] = useState<FormState>(() => fromPrevious(previous ?? null))
  const [hasPrevious, setHasPrevious] = useState(!!previous)
  const [tried, setTried] = useState(false)
  const [serverErrors, setServerErrors] = useState<Record<string, string>>({})
  const [serverMessage, setServerMessage] = useState('')
  const [saving, setSaving] = useState(false)
  const lockRef = useRef(false)

  // جلب آخر تحديث عند عدم تمريره (مثل الفتح من /pm).
  useEffect(() => {
    if (previous !== undefined) return
    let cancelled = false
    apiJson<{ updates: ProjectUpdate[] }>(`/api/project-updates?project_id=${projectId}&page=1&pageSize=1`)
      .then(data => {
        if (cancelled) return
        const last = data.updates?.[0] ?? null
        setHasPrevious(!!last)
        setForm(fromPrevious(last))
      })
      .catch(() => {
        // لا يمنع الإدخال: يبدأ النموذج فارغًا.
      })
      .finally(() => {
        if (!cancelled) setLoadingPrev(false)
      })
    return () => {
      cancelled = true
    }
  }, [previous, projectId])

  const clientErrors = useMemo<Record<string, string>>(() => {
    if (!tried) return {}
    const result = validateProjectUpdate(toPayload(projectId, form), todayIso())
    return result.ok ? {} : result.errors
  }, [tried, form, projectId])

  const errors: Record<string, string> = { ...serverErrors, ...clientErrors }
  const summary = FIELD_ORDER.filter(k => errors[k]).map(k => ({ fieldId: fid(k), message: `${FIELD_LABEL[k]}: ${errors[k]}` }))
  // أخطاء خادم لحقول غير معروفة للنموذج (مثل project_id).
  Object.keys(errors)
    .filter(k => !FIELD_ORDER.includes(k as keyof FormState))
    .forEach(k => summary.push({ fieldId: fid('reporting_date'), message: errors[k] }))

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm(f => ({ ...f, [key]: value }))
    if (serverErrors[key]) setServerErrors(prev => { const next = { ...prev }; delete next[key]; return next })
  }

  const reasonRequired = !!form.health_status && HEALTH_REQUIRES_REASON.includes(form.health_status as ReportedHealth)

  async function save() {
    if (saving || lockRef.current) return
    setTried(true)
    setServerMessage('')
    const payload = toPayload(projectId, form)
    const result = validateProjectUpdate(payload, todayIso())
    if (!result.ok) {
      const first = FIELD_ORDER.find(k => result.errors[k])
      if (first) document.getElementById(fid(first))?.focus()
      return
    }
    lockRef.current = true
    setSaving(true)
    try {
      const data = await apiJson<{ update: ProjectUpdate }>('/api/project-updates', {
        method: 'POST',
        body: JSON.stringify(payload),
      })
      toast.success('تم حفظ التحديث الأسبوعي كسجل جديد.')
      onSaved(data.update)
    } catch (e) {
      if (e instanceof ApiError) {
        setServerErrors(e.fieldErrors)
        const first = FIELD_ORDER.find(k => e.fieldErrors[k])
        if (first) document.getElementById(fid(first))?.focus()
      }
      setServerMessage(errorMessage(e))
    } finally {
      lockRef.current = false
      setSaving(false)
    }
  }

  const milestoneOptions = milestones.map(m => ({ value: m.id, label: m.name }))

  return (
    <Modal
      title="التحديث الأسبوعي للمشروع"
      subtitle={projectName}
      onClose={onClose}
      maxWidth={760}
      onSave={save}
      saveLabel="حفظ التحديث"
      saveLoading={saving}
    >
      {loadingPrev ? (
        <output className="flex flex-col gap-pc-s" aria-label="جارٍ تحميل آخر تحديث">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-10 w-2/3" />
        </output>
      ) : (
        <div className="flex flex-col gap-pc-l">
          <FormRequiredHint />
          <Alert tone="info">
            {hasPrevious
              ? 'القيم معبّأة من آخر تحديث مسجّل. الحفظ ينشئ سجلًا جديدًا ويبقى التحديث السابق محفوظًا في السجل.'
              : 'هذا أول تحديث أسبوعي لهذا المشروع. الحفظ ينشئ سجلًا جديدًا في تاريخ المشروع.'}
          </Alert>
          {serverMessage && <Alert tone="danger" title="لم يُحفظ التحديث">{serverMessage}</Alert>}
          <FormErrorSummary errors={summary} autoFocus={false} />

          {/* ── بيانات التقرير ── */}
          <fieldset className="flex flex-col gap-pc-m">
            <legend className="mb-pc-s text-base font-bold text-pc-gray-900">بيانات التقرير</legend>
            <Input
              id={fid('reporting_date')}
              label={FIELD_LABEL.reporting_date}
              type="date"
              required
              max={todayIso()}
              value={form.reporting_date}
              onChange={e => set('reporting_date', e.target.value)}
              error={errors.reporting_date}
              help="لا يمكن أن يكون بعد تاريخ اليوم."
            />
            <TextArea
              id={fid('executive_summary')}
              label={FIELD_LABEL.executive_summary}
              rows={4}
              maxLength={2000}
              value={form.executive_summary}
              onChange={e => set('executive_summary', e.target.value)}
              error={errors.executive_summary}
              help="من سطرين إلى أربعة: ماذا ينجز المشروع، أين وصل، أبرز مشكلة، وما المطلوب من الإدارة."
            />
          </fieldset>

          {/* ── الإنجاز والصحة ── */}
          <fieldset className="flex flex-col gap-pc-m">
            <legend className="mb-pc-s text-base font-bold text-pc-gray-900">الإنجاز وصحة التنفيذ</legend>
            <div className="grid grid-cols-1 gap-pc-m sm:grid-cols-2">
              <Input
                id={fid('actual_progress')}
                label={`${FIELD_LABEL.actual_progress} (%)`}
                type="number"
                inputMode="numeric"
                dir="ltr"
                min={0}
                max={100}
                step={1}
                value={form.actual_progress}
                onChange={e => set('actual_progress', e.target.value)}
                error={errors.actual_progress}
                help="من 0 إلى 100. عند وجود مهام يُحسب الإنجاز الفعلي منها تلقائيًا."
              />
              <Input
                id={fid('planned_progress')}
                label={`${FIELD_LABEL.planned_progress} (%)`}
                type="number"
                inputMode="numeric"
                dir="ltr"
                min={0}
                max={100}
                step={1}
                value={form.planned_progress}
                onChange={e => set('planned_progress', e.target.value)}
                error={errors.planned_progress}
                help="حسب الخطة المعتمدة حتى تاريخ التقرير. اتركه فارغًا إن لم توجد خطة معتمدة."
              />
            </div>

            <div
              role="radiogroup"
              aria-labelledby={`${fid('health_status')}-label`}
              aria-describedby={errors.health_status ? `${fid('health_status')}-error` : undefined}
              aria-required="true"
              className="flex flex-col gap-pc-xs"
            >
              <p id={`${fid('health_status')}-label`} className="text-sm font-medium text-pc-gray-800">
                {FIELD_LABEL.health_status}{' '}
                <span aria-hidden="true" className="text-pc-error-600">*</span>
                <span className="sr-only">(حقل إلزامي)</span>
              </p>
              <div className="grid grid-cols-1 gap-pc-s sm:grid-cols-3 lg:grid-cols-5">
                {REPORTED_HEALTH_ORDER.map((h, i) => {
                  const s = HEALTH[h]
                  const Icon = s.icon
                  const checked = form.health_status === h
                  return (
                    <label
                      key={h}
                      className={`flex min-h-[2.75rem] cursor-pointer items-center gap-pc-s rounded-pc-md border px-pc-s py-pc-s text-sm font-semibold transition-colors has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-pc-brand-600 ${
                        checked ? 'border-pc-brand-600 bg-pc-brand-50 text-pc-brand-800' : 'border-pc-gray-400 bg-white text-pc-gray-800 hover:bg-pc-gray-50'
                      }`}
                    >
                      <input
                        type="radio"
                        name="pu-health"
                        id={i === 0 ? fid('health_status') : undefined}
                        value={h}
                        checked={checked}
                        onChange={() => set('health_status', h)}
                        className="sr-only"
                      />
                      <span aria-hidden="true" className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full ${s.badge}`}>
                        <Icon className="h-4 w-4" />
                      </span>
                      {s.label}
                    </label>
                  )
                })}
              </div>
              {errors.health_status && (
                <p id={`${fid('health_status')}-error`} role="alert" className="text-sm font-medium text-pc-error-600">
                  {errors.health_status}
                </p>
              )}
            </div>

            <TextArea
              id={fid('delay_reason')}
              label={FIELD_LABEL.delay_reason}
              required={reasonRequired}
              rows={3}
              value={form.delay_reason}
              onChange={e => set('delay_reason', e.target.value)}
              error={errors.delay_reason}
              help={reasonRequired ? 'إلزامي عند اختيار «متأخر» أو «متعثر» أو «متوقف».' : 'اختياري عند سير المشروع ضمن الخطة.'}
            />
          </fieldset>

          {/* ── المراحل والموعد ── */}
          <fieldset className="flex flex-col gap-pc-m">
            <legend className="mb-pc-s text-base font-bold text-pc-gray-900">المرحلة الحالية والإنجاز القادم</legend>
            {milestones.length === 0 && (
              <Alert tone="info">لا توجد مراحل مسجّلة لهذا المشروع بعد — تُضاف من صفحة «مشاريعي».</Alert>
            )}
            <div className="grid grid-cols-1 gap-pc-m sm:grid-cols-2">
              <Select
                id={fid('current_milestone_id')}
                label={FIELD_LABEL.current_milestone_id}
                value={form.current_milestone_id}
                onChange={v => set('current_milestone_id', v)}
                options={milestoneOptions}
                placeholder="تُحدَّد تلقائيًا (أول مرحلة غير مكتملة)"
                disabled={milestones.length === 0}
                error={errors.current_milestone_id}
              />
              <Select
                id={fid('next_milestone_id')}
                label={FIELD_LABEL.next_milestone_id}
                value={form.next_milestone_id}
                onChange={v => set('next_milestone_id', v)}
                options={milestoneOptions}
                placeholder="تُحدَّد تلقائيًا (المرحلة التالية)"
                disabled={milestones.length === 0}
                error={errors.next_milestone_id}
              />
            </div>
            <Input
              id={fid('forecast_end_date')}
              label={FIELD_LABEL.forecast_end_date}
              type="date"
              value={form.forecast_end_date}
              onChange={e => set('forecast_end_date', e.target.value)}
              error={errors.forecast_end_date}
              help="التاريخ الذي تتوقع فيه انتهاء المشروع فعليًا حسب الوضع الحالي."
            />
          </fieldset>

          {/* ── الدعم ── */}
          <fieldset className="flex flex-col gap-pc-m">
            <legend className="mb-pc-s text-base font-bold text-pc-gray-900">الدعم المطلوب</legend>
            <TextArea
              id={fid('support_required')}
              label={FIELD_LABEL.support_required}
              rows={3}
              value={form.support_required}
              onChange={e => set('support_required', e.target.value)}
              error={errors.support_required}
              help="اتركه فارغًا إن لم يكن هناك دعم مطلوب."
            />
            <div className="grid grid-cols-1 gap-pc-m sm:grid-cols-2">
              <Input
                id={fid('support_from')}
                label={FIELD_LABEL.support_from}
                value={form.support_from}
                maxLength={200}
                onChange={e => set('support_from', e.target.value)}
                error={errors.support_from}
              />
              <Input
                id={fid('support_due_date')}
                label={FIELD_LABEL.support_due_date}
                type="date"
                value={form.support_due_date}
                onChange={e => set('support_due_date', e.target.value)}
                error={errors.support_due_date}
              />
            </div>
            <TextArea
              id={fid('support_impact')}
              label={FIELD_LABEL.support_impact}
              rows={2}
              value={form.support_impact}
              onChange={e => set('support_impact', e.target.value)}
              error={errors.support_impact}
            />
          </fieldset>

          {/* ── المخاطر والمرفقات ── */}
          <fieldset className="flex flex-col gap-pc-s">
            <legend className="mb-pc-s text-base font-bold text-pc-gray-900">المخاطر والمرفقات</legend>
            <div className="flex flex-wrap items-center justify-between gap-pc-s rounded-pc-md border border-pc-gray-200 bg-pc-gray-50 p-pc-s">
              <p className="flex items-center gap-pc-s text-sm text-pc-gray-700">
                <ShieldAlert aria-hidden="true" className="h-5 w-5 shrink-0 text-pc-warning-600" />
                المخاطر والتعثرات الجديدة تُسجَّل في سجل المخاطر لتظهر في لوحة الاجتماع.
              </p>
              {onRequestRisk && (
                <Button variant="secondary" size="sm" onClick={onRequestRisk}>
                  تسجيل خطر أو تعثر جديد
                </Button>
              )}
            </div>
            <p className="flex items-center gap-pc-s text-sm text-pc-gray-600">
              <Paperclip aria-hidden="true" className="h-4 w-4 shrink-0" />
              المرفقات غير متاحة في هذا الإصدار.
            </p>
          </fieldset>
        </div>
      )}
    </Modal>
  )
}
