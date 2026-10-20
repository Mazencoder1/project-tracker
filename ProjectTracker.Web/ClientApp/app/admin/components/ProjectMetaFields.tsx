'use client'
import { useEffect, useState } from 'react'
import { Alert, FormErrorSummary, FormRequiredHint, Input, Modal, Select, TextArea, useToast } from '@/app/components/ui'
import { ApiError, apiJson, errorMessage } from '@/app/components/executive/api'
import { PRIORITY, PRIORITY_ORDER } from '@/lib/portfolio/labels'
import { formatCount } from '@/lib/arabicCount'
import { PROJECT_NAME_MAX, calcProjectWeeks, validateProjectSchedule } from '@/lib/projectSchedule'

export interface ProjectMeta {
  project_code: string
  department: string
  priority: string
}

interface DepartmentsResponse {
  source: 'config'
  departments: { id: string; name: string }[]
}

/**
 * قائمة الإدارات من `GET /api/departments` (تُضبط في Departments:List بملف الإعدادات). الحقل قائمة منسدلة دائمًا؛
 * فشل الطلب نفسه يُعامل كقائمة فارغة.
 */
function useDepartments(): { loading: boolean; names: string[] } {
  const [state, setState] = useState<{ loading: boolean; names: string[] }>({ loading: true, names: [] })
  useEffect(() => {
    let cancelled = false
    apiJson<DepartmentsResponse>('/api/departments')
      .then(res => { if (!cancelled) setState({ loading: false, names: res.departments.map(d => d.name) }) })
      .catch(() => { if (!cancelled) setState({ loading: false, names: [] }) })
    return () => { cancelled = true }
  }, [])
  return state
}

export function ProjectMetaFields({
  idPrefix,
  value,
  onChange,
  errors = {},
}: Readonly<{
  idPrefix: string
  value: ProjectMeta
  onChange: (next: ProjectMeta) => void
  errors?: Record<string, string>
}>) {
  const departments = useDepartments()
  // القيمة المحفوظة قد لا تكون في القائمة الحالية (إدارة أُلغيت أو أُدخلت قبل هذا التحديث) — نُبقيها خيارًا حتى لا تُمحى بصمت.
  const names = value.department && !departments.names.includes(value.department)
    ? [value.department, ...departments.names]
    : departments.names

  return (
    <div className="grid grid-cols-1 gap-pc-s sm:grid-cols-3">
      <Input
        id={`${idPrefix}-code`}
        label="رقم المشروع (اختياري)"
        dir="ltr"
        maxLength={50}
        value={value.project_code}
        onChange={e => onChange({ ...value, project_code: e.target.value })}
        error={errors.project_code}
      />
      <Select
        id={`${idPrefix}-department`}
        label="الإدارة المالكة (اختياري)"
        value={value.department}
        onChange={v => onChange({ ...value, department: v })}
        placeholder={departments.loading ? 'جارٍ تحميل الإدارات…' : 'اختر الإدارة'}
        options={names.map(n => ({ value: n, label: n }))}
        disabled={departments.loading}
        error={errors.department}
      />
      <Select
        id={`${idPrefix}-priority`}
        label="الأولوية (اختياري)"
        value={value.priority}
        onChange={v => onChange({ ...value, priority: v })}
        placeholder="غير محددة"
        options={PRIORITY_ORDER.map(p => ({ value: p, label: PRIORITY[p].label }))}
        error={errors.priority}
      />
    </div>
  )
}

/** يحوّل النصوص الفارغة إلى null قبل الإرسال. */
export function metaPayload(meta: ProjectMeta) {
  const t = (v: string) => (v.trim() === '' ? null : v.trim())
  return { project_code: t(meta.project_code), department: t(meta.department), priority: t(meta.priority) }
}

export interface ScheduleValue {
  start_date: string
  end_date: string
  /** نص لأن الحقل نصي بأرقام لاتينية (راجع ScheduleFields). */
  total_weeks: string
}

/** 2026-09-01 → 01/09/2026 بأرقام لاتينية. */
function latinDate(iso: string): string {
  const [y, m, d] = iso.split('-')
  return y && m && d ? `${d}/${m}/${y}` : iso
}

/**
 * حقول التاريخين وعدد الأسابيع مشتركة بين نافذتي الإنشاء والتعديل (كانت نافذة
 * التعديل لا تتيحها، فخطأ في التاريخ يستلزم حذف المشروع وإعادة إنشائه).
 *   • تغيير أي تاريخ يعيد حساب الأسابيع من lib/projectSchedule.ts (18 أسبوعًا لـ 01/09 → 31/12/2026).
 *   • المدة ناتج محسوب للقراءة فقط؛ التاريخان هما مصدر الحقيقة ولا توجد قيمة يدوية قد تخالفهما.
 */
export function ScheduleFields({
  idPrefix,
  value,
  onChange,
  errors = {},
}: Readonly<{
  idPrefix: string
  value: ScheduleValue
  onChange: (next: ScheduleValue) => void
  errors?: Partial<Record<'start_date' | 'end_date' | 'total_weeks', string>>
}>) {
  function changeDate(field: 'start_date' | 'end_date', date: string) {
    const next = { ...value, [field]: date }
    const weeks = calcProjectWeeks(next.start_date, next.end_date)
    onChange(weeks === null ? next : { ...next, total_weeks: String(weeks) })
  }
  const computed = calcProjectWeeks(value.start_date, value.end_date)

  return (
    <>
      <div className="grid grid-cols-1 gap-pc-s sm:grid-cols-2">
        <Input
          id={`${idPrefix}-start`}
          label="تاريخ البداية"
          type="date"
          value={value.start_date}
          max={value.end_date || undefined}
          onChange={e => changeDate('start_date', e.target.value)}
          error={errors.start_date}
          required
        />
        <Input
          id={`${idPrefix}-end`}
          label="تاريخ النهاية"
          type="date"
          value={value.end_date}
          min={value.start_date || undefined}
          onChange={e => changeDate('end_date', e.target.value)}
          error={errors.end_date}
          required
        />
      </div>
      <div className="rounded-pc-md border border-pc-gray-200 bg-pc-gray-50 px-pc-m py-pc-s">
        <span className="text-sm text-pc-gray-600">مدة المشروع: </span>
        <strong className="text-sm text-pc-gray-900">
          {computed === null ? 'تُحسب بعد تحديد التاريخين' : formatCount(computed, 'week')}
        </strong>
        {computed !== null && (
          <span className="ms-pc-xs text-sm text-pc-gray-500">({latinDate(value.start_date)} — {latinDate(value.end_date)})</span>
        )}
        {errors.total_weeks && <p className="mt-pc-xs text-sm text-pc-error-700">{errors.total_weeks}</p>}
      </div>
    </>
  )
}

/** يبني حقول الجدول للإرسال؛ الخادم يحسب الأسابيع من التاريخين ولا يقبل قيمة يدوية. */
export function schedulePayload(schedule: ScheduleValue) {
  return {
    start_date: schedule.start_date || null,
    end_date: schedule.end_date || null,
  }
}

export interface EditableProject {
  id: string
  name: string
  description?: string | null
  project_code?: string | null
  department?: string | null
  priority?: string | null
  start_date?: string | null
  end_date?: string | null
  total_weeks?: number | null
}

export function EditProjectModal({
  project,
  onClose,
  onSaved,
}: Readonly<{
  project: EditableProject
  onClose: () => void
  onSaved: () => void
}>) {
  const toast = useToast()
  const [name, setName] = useState(project.name)
  const [description, setDescription] = useState(project.description ?? '')
  const [meta, setMeta] = useState<ProjectMeta>({
    project_code: project.project_code ?? '',
    department: project.department ?? '',
    priority: project.priority ?? '',
  })
  const [schedule, setSchedule] = useState<ScheduleValue>({
    start_date: project.start_date ? project.start_date.slice(0, 10) : '',
    end_date: project.end_date ? project.end_date.slice(0, 10) : '',
    total_weeks: project.total_weeks ? String(project.total_weeks) : '',
  })
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [message, setMessage] = useState('')
  const [saving, setSaving] = useState(false)

  async function save() {
    if (saving) return
    setMessage('')
    const nextErrors: Record<string, string> = {}
    if (!name.trim()) nextErrors.name = 'اسم المشروع مطلوب'
    else if (name.trim().length > PROJECT_NAME_MAX) nextErrors.name = `اسم المشروع يجب ألا يزيد على ${PROJECT_NAME_MAX} حرف`
    Object.assign(nextErrors, validateProjectSchedule(schedule).errors)
    if (!schedule.start_date && !nextErrors.start_date) nextErrors.start_date = 'تاريخ البداية مطلوب.'
    if (!schedule.end_date && !nextErrors.end_date) nextErrors.end_date = 'تاريخ النهاية مطلوب.'
    if (Object.keys(nextErrors).length > 0) {
      setErrors(nextErrors)
      const first = ['name', 'start_date', 'end_date', 'total_weeks'].find(key => nextErrors[key])
      const ids: Record<string, string> = { name: 'edit-project-name', start_date: 'edit-project-start', end_date: 'edit-project-end', total_weeks: 'edit-project-weeks' }
      if (first) document.getElementById(ids[first])?.focus()
      return
    }
    setErrors({})
    setSaving(true)
    try {
      await apiJson(`/api/projects/${project.id}`, {
        method: 'PATCH',
        body: JSON.stringify({ name: name.trim(), description: description.trim(), ...metaPayload(meta), ...schedulePayload(schedule) }),
      })
      toast.success('تم تحديث بيانات المشروع.')
      onSaved()
    } catch (e) {
      if (e instanceof ApiError) setErrors(e.fieldErrors)
      setMessage(errorMessage(e))
    } finally {
      setSaving(false)
    }
  }

  const summary = [
    errors.name && { fieldId: 'edit-project-name', message: `اسم المشروع: ${errors.name}` },
    errors.project_code && { fieldId: 'edit-project-code', message: `رقم المشروع: ${errors.project_code}` },
    errors.department && { fieldId: 'edit-project-department', message: `الإدارة: ${errors.department}` },
    errors.priority && { fieldId: 'edit-project-priority', message: `الأولوية: ${errors.priority}` },
    errors.start_date && { fieldId: 'edit-project-start', message: `تاريخ البداية: ${errors.start_date}` },
    errors.end_date && { fieldId: 'edit-project-end', message: `تاريخ النهاية: ${errors.end_date}` },
    errors.total_weeks && { fieldId: 'edit-project-weeks', message: `عدد الأسابيع: ${errors.total_weeks}` },
  ].filter(Boolean) as { fieldId: string; message: string }[]

  return (
    <Modal title="تعديل بيانات المشروع" subtitle={project.name} maxWidth={620} onClose={onClose} onSave={save} saveLabel="حفظ التعديل" saveLoading={saving}>
      <div className="flex flex-col gap-pc-m">
        <FormRequiredHint />
        {message && <Alert tone="danger" title="لم تُحفظ التعديلات">{message}</Alert>}
        <FormErrorSummary errors={summary} autoFocus={false} />
        <Input
          id="edit-project-name"
          label="اسم المشروع"
          required
          maxLength={PROJECT_NAME_MAX}
          value={name}
          onChange={e => {
            setName(e.target.value)
            if (errors.name) setErrors(prev => { const next = { ...prev }; delete next.name; return next })
          }}
          error={errors.name}
        />
        <TextArea id="edit-project-desc" label="الوصف (اختياري)" rows={3} maxLength={4000} value={description} onChange={e => setDescription(e.target.value)} />
        <ProjectMetaFields idPrefix="edit-project" value={meta} onChange={setMeta} errors={errors} />
        <ScheduleFields idPrefix="edit-project" value={schedule} onChange={setSchedule} errors={errors} />
      </div>
    </Modal>
  )
}
