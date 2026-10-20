'use client'
import { useMemo, useRef, useState } from 'react'
import {
  Alert,
  FormErrorSummary,
  FormRequiredHint,
  Input,
  Modal,
  Select,
  TextArea,
  useToast,
} from '@/app/components/ui'
import {
  RISK_PROBABILITY,
  RISK_PROBABILITY_ORDER,
  RISK_SEVERITY,
  RISK_SEVERITY_ORDER,
  RISK_STATUS,
  RISK_STATUS_ORDER,
  RISK_TYPE,
  RISK_TYPE_ORDER,
} from '@/lib/portfolio/labels'
import type { RiskIssue, RiskProbability, RiskSeverity, RiskStatus, RiskType } from '@/lib/portfolio/types'
import { validateRisk } from '@/lib/portfolio/validation'
import { ApiError, apiJson, errorMessage } from './api'
import { usePeople } from './hooks'

interface RiskFormProps {
  projectId: string
  projectName?: string
  milestones: { id: string; name: string }[]
  /** عند التعديل. */
  risk?: RiskIssue | null
  onClose: () => void
  onSaved: (risk: RiskIssue) => void
}

type FormState = {
  type: RiskType | ''
  title: string
  description: string
  severity: RiskSeverity | ''
  probability: RiskProbability | ''
  status: RiskStatus
  owner_id: string
  milestone_id: string
  due_date: string
  mitigation: string
}

const ORDER: (keyof FormState)[] = ['type', 'title', 'description', 'severity', 'probability', 'status', 'owner_id', 'milestone_id', 'due_date', 'mitigation']
const LABEL: Record<keyof FormState, string> = {
  type: 'النوع',
  title: 'العنوان',
  description: 'الوصف',
  severity: 'الخطورة',
  probability: 'الاحتمالية',
  status: 'الحالة',
  owner_id: 'المسؤول',
  milestone_id: 'المرحلة المرتبطة',
  due_date: 'تاريخ الاستحقاق',
  mitigation: 'الإجراء العلاجي',
}
const fid = (k: string) => `risk-${k}`

export function RiskForm({ projectId, projectName, milestones, risk, onClose, onSaved }: Readonly<RiskFormProps>) {
  const toast = useToast()
  const { people, error: peopleError } = usePeople()
  const editing = !!risk
  const [form, setForm] = useState<FormState>(() => ({
    type: risk?.type ?? '',
    title: risk?.title ?? '',
    description: risk?.description ?? '',
    severity: risk?.severity ?? '',
    probability: risk?.probability ?? '',
    status: risk?.status ?? 'open',
    owner_id: risk?.owner_id ?? '',
    milestone_id: risk?.milestone_id ?? '',
    due_date: (risk?.due_date ?? '').slice(0, 10),
    mitigation: risk?.mitigation ?? '',
  }))
  const [tried, setTried] = useState(false)
  const [serverErrors, setServerErrors] = useState<Record<string, string>>({})
  const [serverMessage, setServerMessage] = useState('')
  const [saving, setSaving] = useState(false)
  const lockRef = useRef(false)

  function payload() {
    const t = (v: string) => (v.trim() === '' ? null : v)
    return {
      project_id: projectId,
      type: form.type || null,
      title: form.title,
      description: t(form.description),
      severity: form.severity || null,
      probability: form.probability || null,
      status: form.status,
      owner_id: t(form.owner_id),
      milestone_id: t(form.milestone_id),
      due_date: t(form.due_date),
      mitigation: t(form.mitigation),
    }
  }

  const clientErrors = useMemo<Record<string, string>>(() => {
    if (!tried) return {}
    const r = validateRisk(payload(), { partial: false })
    return r.ok ? {} : r.errors
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tried, form])
  const errors: Record<string, string> = { ...serverErrors, ...clientErrors }
  const summary = ORDER.filter(k => errors[k]).map(k => ({ fieldId: fid(k), message: `${LABEL[k]}: ${errors[k]}` }))

  function set<K extends keyof FormState>(k: K, v: FormState[K]) {
    setForm(f => ({ ...f, [k]: v }))
    if (serverErrors[k]) setServerErrors(prev => { const next = { ...prev }; delete next[k]; return next })
  }

  async function save() {
    if (saving || lockRef.current) return
    setTried(true)
    setServerMessage('')
    const body = payload()
    const r = validateRisk(body, { partial: false })
    if (!r.ok) {
      const first = ORDER.find(k => r.errors[k])
      if (first) document.getElementById(fid(first))?.focus()
      return
    }
    lockRef.current = true
    setSaving(true)
    try {
      const data = await apiJson<{ risk: RiskIssue }>(editing ? `/api/risks/${risk!.id}` : '/api/risks', {
        method: editing ? 'PATCH' : 'POST',
        body: JSON.stringify(body),
      })
      toast.success(editing ? 'تم تحديث السجل.' : 'تم تسجيل الخطر أو التعثر.')
      onSaved(data.risk)
    } catch (e) {
      if (e instanceof ApiError) setServerErrors(e.fieldErrors)
      setServerMessage(errorMessage(e))
    } finally {
      lockRef.current = false
      setSaving(false)
    }
  }

  const peopleOptions = [{ value: '', label: 'بدون مسؤول محدد' }, ...people.map(p => ({ value: p.id, label: p.full_name }))]

  return (
    <Modal
      title={editing ? 'تعديل خطر أو تعثر' : 'تسجيل خطر أو تعثر'}
      subtitle={projectName}
      onClose={onClose}
      maxWidth={640}
      onSave={save}
      saveLabel={editing ? 'حفظ التعديل' : 'تسجيل'}
      saveLoading={saving}
    >
      <div className="flex flex-col gap-pc-m">
        <FormRequiredHint />
        {serverMessage && <Alert tone="danger" title="لم يُحفظ السجل">{serverMessage}</Alert>}
        {peopleError && <Alert tone="warning">{peopleError} يمكنك الحفظ دون تحديد مسؤول.</Alert>}
        <FormErrorSummary errors={summary} autoFocus={false} />

        <div className="grid grid-cols-1 gap-pc-m sm:grid-cols-2">
          <Select
            id={fid('type')}
            label={LABEL.type}
            required
            value={form.type}
            onChange={v => set('type', v as RiskType)}
            options={RISK_TYPE_ORDER.map(t => ({ value: t, label: RISK_TYPE[t].label }))}
            error={errors.type}
            help="خطر: قد يحدث. تعثر: حدث ويؤثر. عائق: يمنع التنفيذ."
          />
          <Select
            id={fid('severity')}
            label={LABEL.severity}
            required
            value={form.severity}
            onChange={v => set('severity', v as RiskSeverity)}
            options={RISK_SEVERITY_ORDER.map(s => ({ value: s, label: RISK_SEVERITY[s].label }))}
            error={errors.severity}
          />
        </div>
        <Input
          id={fid('title')}
          label={LABEL.title}
          required
          maxLength={300}
          value={form.title}
          onChange={e => set('title', e.target.value)}
          error={errors.title}
          help="من 3 إلى 300 حرف."
        />
        <TextArea
          id={fid('description')}
          label={LABEL.description}
          rows={3}
          value={form.description}
          onChange={e => set('description', e.target.value)}
          error={errors.description}
        />
        <div className="grid grid-cols-1 gap-pc-m sm:grid-cols-2">
          <Select
            id={fid('probability')}
            label={LABEL.probability}
            value={form.probability}
            onChange={v => set('probability', v as RiskProbability | '')}
            options={RISK_PROBABILITY_ORDER.map(p => ({ value: p, label: RISK_PROBABILITY[p] }))}
            placeholder="غير محددة"
            error={errors.probability}
          />
          <Select
            id={fid('status')}
            label={LABEL.status}
            required
            value={form.status}
            onChange={v => set('status', (v || 'open') as RiskStatus)}
            options={RISK_STATUS_ORDER.map(s => ({ value: s, label: RISK_STATUS[s].label }))}
            error={errors.status}
            help={editing ? 'اختر «مغلق» لإغلاق السجل — لا يُحذف من التاريخ.' : undefined}
          />
          <Select
            id={fid('owner_id')}
            label={LABEL.owner_id}
            value={form.owner_id}
            onChange={v => set('owner_id', v)}
            options={peopleOptions}
            placeholder="بدون مسؤول محدد"
            error={errors.owner_id}
          />
          <Select
            id={fid('milestone_id')}
            label={LABEL.milestone_id}
            value={form.milestone_id}
            onChange={v => set('milestone_id', v)}
            options={[{ value: '', label: 'المشروع كاملًا' }, ...milestones.map(m => ({ value: m.id, label: m.name }))]}
            placeholder="المشروع كاملًا"
            error={errors.milestone_id}
          />
        </div>
        <Input
          id={fid('due_date')}
          label={LABEL.due_date}
          type="date"
          value={form.due_date}
          onChange={e => set('due_date', e.target.value)}
          error={errors.due_date}
        />
        <TextArea
          id={fid('mitigation')}
          label={LABEL.mitigation}
          rows={3}
          value={form.mitigation}
          onChange={e => set('mitigation', e.target.value)}
          error={errors.mitigation}
          help="ما الذي سيُنفَّذ لمعالجة الخطر أو تجاوز التعثر، ومتى."
        />
      </div>
    </Modal>
  )
}
