'use client'

import { useCallback, useEffect, useMemo, useState } from 'react'
import { Alert, AppShell, Button, CardSkeleton, useToast } from '@/app/components/ui'
import { ApiError, apiJson, errorMessage } from '@/app/components/executive/api'
import { PreviewModal } from './components/PreviewModal'
import { TemplateEditor } from './components/TemplateEditor'
import { TemplateList } from './components/TemplateList'
import { TestEmailModal } from './components/TestEmailModal'
import { VariablesPanel } from './components/VariablesPanel'
import { WeeklyReminderCard } from './components/WeeklyReminderCard'
import type {
  EmailPreview,
  EmailTemplate,
  EmailTemplatesResponse,
  TemplateDraft,
  WeeklyReminderSettings,
} from './types'

const toDraft = (template: EmailTemplate): TemplateDraft => ({
  nameAr: template.nameAr,
  subject: template.subject,
  messageText: template.messageText,
  isEnabled: template.isEnabled,
})

export default function EmailTemplatesPage() {
  const toast = useToast()
  const [data, setData] = useState<EmailTemplatesResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<{ message: string; status: number } | null>(null)

  const [selectedKey, setSelectedKey] = useState('')
  const [draft, setDraft] = useState<TemplateDraft | null>(null)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [saving, setSaving] = useState(false)
  const [previewing, setPreviewing] = useState(false)
  const [preview, setPreview] = useState<EmailPreview | null>(null)

  const [testOpen, setTestOpen] = useState(false)
  const [testEmail, setTestEmail] = useState('')
  const [testError, setTestError] = useState('')
  const [testSending, setTestSending] = useState(false)

  const [weekly, setWeekly] = useState<WeeklyReminderSettings | null>(null)
  const [weeklyErrors, setWeeklyErrors] = useState<Record<string, string>>({})
  const [weeklySaving, setWeeklySaving] = useState(false)

  const selected = useMemo(() => data?.templates.find(t => t.templateKey === selectedKey) ?? null, [data, selectedKey])
  const dirty = !!selected && !!draft && JSON.stringify(toDraft(selected)) !== JSON.stringify(draft)
  const weeklyDirty = !!data && !!weekly &&
    (weekly.enabled !== data.weeklyReminder.enabled || weekly.day !== data.weeklyReminder.day || weekly.time !== data.weeklyReminder.time)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const response = await apiJson<EmailTemplatesResponse>('/api/email-templates')
      setData(response)
      setWeekly(response.weeklyReminder)
      const first = response.templates[0]
      if (first) {
        setSelectedKey(first.templateKey)
        setDraft(toDraft(first))
      }
      setLoadError(null)
    } catch (e) {
      setLoadError({ message: errorMessage(e, 'تعذّر تحميل قوالب البريد.'), status: e instanceof ApiError ? e.status : 0 })
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    load()
  }, [load])

  function select(key: string) {
    if (key === selectedKey) return
    if (dirty && !window.confirm('توجد تعديلات لم تُحفظ في هذا القالب. هل تريد تجاهلها؟')) return
    const template = data?.templates.find(t => t.templateKey === key)
    if (!template) return
    setSelectedKey(key)
    setDraft(toDraft(template))
    setErrors({})
  }

  function patchDraft(patch: Partial<TemplateDraft>) {
    setDraft(current => (current ? { ...current, ...patch } : current))
    setErrors(current => {
      const next = { ...current }
      for (const key of Object.keys(patch)) delete next[key]
      return next
    })
  }

  async function save() {
    if (!draft || !selected) return
    setSaving(true)
    setErrors({})
    try {
      const response = await apiJson<{ template: EmailTemplate }>(`/api/email-templates/${selected.templateKey}`, {
        method: 'PUT',
        body: JSON.stringify(draft),
      })
      setData(current => current && { ...current, templates: current.templates.map(t => (t.templateKey === response.template.templateKey ? response.template : t)) })
      setDraft(toDraft(response.template))
      toast.success('تم حفظ القالب.')
    } catch (e) {
      if (e instanceof ApiError && Object.keys(e.fieldErrors).length > 0) setErrors(e.fieldErrors)
      else toast.error(errorMessage(e, 'تعذّر حفظ القالب.'))
    } finally {
      setSaving(false)
    }
  }

  async function showPreview() {
    if (!draft || !selected) return
    setPreviewing(true)
    setErrors({})
    try {
      setPreview(await apiJson<EmailPreview>(`/api/email-templates/${selected.templateKey}/preview`, {
        method: 'POST',
        body: JSON.stringify({ subject: draft.subject, messageText: draft.messageText }),
      }))
    } catch (e) {
      if (e instanceof ApiError && Object.keys(e.fieldErrors).length > 0) setErrors(e.fieldErrors)
      else toast.error(errorMessage(e, 'تعذّرت معاينة الرسالة.'))
    } finally {
      setPreviewing(false)
    }
  }

  async function sendTest() {
    if (!draft || !selected) return
    setTestSending(true)
    setTestError('')
    try {
      await apiJson(`/api/email-templates/${selected.templateKey}/test`, {
        method: 'POST',
        body: JSON.stringify({ to: testEmail.trim(), subject: draft.subject, messageText: draft.messageText }),
      })
      toast.success(`أُرسل البريد التجريبي إلى ${testEmail.trim()}.`)
      setTestOpen(false)
    } catch (e) {
      if (e instanceof ApiError && e.fieldErrors.to) setTestError(e.fieldErrors.to)
      else if (e instanceof ApiError && (e.fieldErrors.subject || e.fieldErrors.messageText)) {
        setTestOpen(false)
        setErrors(e.fieldErrors)
      } else setTestError(errorMessage(e, 'تعذّر إرسال البريد التجريبي.'))
    } finally {
      setTestSending(false)
    }
  }

  async function saveWeekly() {
    if (!weekly) return
    setWeeklySaving(true)
    setWeeklyErrors({})
    try {
      const response = await apiJson<{ weeklyReminder: WeeklyReminderSettings }>('/api/email-settings/weekly-reminder', {
        method: 'PUT',
        body: JSON.stringify({ enabled: weekly.enabled, day: weekly.day, time: weekly.time }),
      })
      setData(current => current && { ...current, weeklyReminder: response.weeklyReminder })
      setWeekly(response.weeklyReminder)
      toast.success('تم حفظ جدولة التذكير الأسبوعي.')
    } catch (e) {
      if (e instanceof ApiError && Object.keys(e.fieldErrors).length > 0) setWeeklyErrors(e.fieldErrors)
      else toast.error(errorMessage(e, 'تعذّر حفظ الجدولة.'))
    } finally {
      setWeeklySaving(false)
    }
  }

  const forbidden = loadError?.status === 403

  return (
    <AppShell
      title="قوالب البريد الإلكتروني"
      subtitle="عدّل عنوان الرسائل التلقائية ومحتواها دون تغيير في النظام."
      breadcrumbs={[{ label: 'الإعدادات', href: '/settings' }, { label: 'قوالب البريد الإلكتروني' }]}
    >
      {loading && (
        <output className="grid gap-pc-m lg:grid-cols-[18rem_minmax(0,1fr)]" aria-label="جارٍ تحميل القوالب">
          <CardSkeleton />
          <CardSkeleton />
        </output>
      )}

      {!loading && loadError && (
        <Alert
          tone="danger"
          title={forbidden ? 'هذه الصفحة لمدير النظام فقط' : 'تعذّر تحميل قوالب البريد'}
          action={!forbidden && <Button variant="secondary" onClick={load}>إعادة المحاولة</Button>}
        >
          {forbidden ? 'لا تملك صلاحية عرض قوالب البريد الإلكتروني أو تعديلها.' : loadError.message}
        </Alert>
      )}

      {!loading && data && draft && selected && weekly && (
        <div className="flex flex-col gap-pc-m">
          {!data.mail.enabled && (
            <Alert tone="warning" title="إرسال البريد الإلكتروني متوقف">
              الإعداد <code dir="ltr">Email:Enabled</code> مضبوط على false، لذلك لن تُرسل أي رسالة (ولا البريد التجريبي) حتى يُفعَّل في إعدادات الخادم.
            </Alert>
          )}
          {!data.mail.hasBaseUrl && (
            <Alert tone="warning" title="عنوان النظام غير مضبوط">
              الإعداد <code dir="ltr">Application:BaseUrl</code> غير مضبوط، فلن يحتوي المتغير <code dir="ltr">{'{{ProjectUrl}}'}</code> على رابط فعلي في الرسائل.
            </Alert>
          )}

          <div className="grid items-start gap-pc-m lg:grid-cols-[18rem_minmax(0,1fr)]">
            <div className="flex flex-col gap-pc-m">
              <TemplateList templates={data.templates} selectedKey={selectedKey} onSelect={select} />
              <WeeklyReminderCard
                settings={weekly}
                errors={weeklyErrors}
                dirty={weeklyDirty}
                saving={weeklySaving}
                onChange={patch => {
                  setWeekly(current => (current ? { ...current, ...patch } : current))
                  setWeeklyErrors({})
                }}
                onSave={saveWeekly}
              />
            </div>
            <div className="grid items-start gap-pc-m xl:grid-cols-[minmax(0,1fr)_20rem]">
              <TemplateEditor
                template={selected}
                draft={draft}
                errors={errors}
                dirty={dirty}
                saving={saving}
                previewing={previewing}
                onChange={patchDraft}
                onSave={save}
                onPreview={showPreview}
                onTest={() => {
                  setTestError('')
                  setTestOpen(true)
                }}
              />
              <VariablesPanel variables={data.variables} />
            </div>
          </div>
        </div>
      )}

      {preview && <PreviewModal preview={preview} onClose={() => setPreview(null)} />}
      {testOpen && (
        <TestEmailModal
          email={testEmail}
          error={testError}
          sending={testSending}
          onEmailChange={value => {
            setTestEmail(value)
            setTestError('')
          }}
          onSend={sendTest}
          onClose={() => setTestOpen(false)}
        />
      )}
    </AppShell>
  )
}
