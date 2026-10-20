'use client'

import { Eye, Mail, Save } from 'lucide-react'
import { Button, FormErrorSummary, FormRequiredHint, Input, TextArea } from '@/app/components/ui'
import type { EmailTemplate, TemplateDraft } from '../types'

const FIELD_IDS: Record<string, string> = {
  nameAr: 'tpl-name',
  subject: 'tpl-subject',
  messageText: 'tpl-message',
}

export function TemplateEditor({
  template,
  draft,
  errors,
  dirty,
  saving,
  previewing,
  onChange,
  onSave,
  onPreview,
  onTest,
}: Readonly<{
  template: EmailTemplate
  draft: TemplateDraft
  errors: Record<string, string>
  dirty: boolean
  saving: boolean
  previewing: boolean
  onChange: (patch: Partial<TemplateDraft>) => void
  onSave: () => void
  onPreview: () => void
  onTest: () => void
}>) {
  const summary = Object.entries(errors)
    .filter(([field]) => FIELD_IDS[field])
    .map(([field, message]) => ({ fieldId: FIELD_IDS[field], message }))

  return (
    <form
      onSubmit={e => {
        e.preventDefault()
        onSave()
      }}
      noValidate
      className="flex flex-col gap-pc-m rounded-pc-md border border-pc-gray-200 bg-white p-pc-m shadow-pc-card"
      aria-labelledby="tpl-editor-title"
    >
      <div>
        <h2 id="tpl-editor-title" className="text-pc-h4 text-pc-gray-900">تعديل القالب: {template.nameAr}</h2>
        <p className="mt-1 text-sm text-pc-gray-600">
          المفتاح <code dir="ltr">{template.templateKey}</code> ثابت ولا يتغيّر. آخر تعديل:{' '}
          {new Date(template.updatedAt).toLocaleString('ar-SA')}
          {template.updatedByName ? ` — ${template.updatedByName}` : ''}
        </p>
      </div>

      <FormRequiredHint />
      {summary.length > 0 && <FormErrorSummary errors={summary} />}

      <Input
        id="tpl-name"
        label="اسم القالب"
        required
        maxLength={200}
        value={draft.nameAr}
        onChange={e => onChange({ nameAr: e.target.value })}
        error={errors.nameAr}
      />
      <Input
        id="tpl-subject"
        label="عنوان البريد (Subject)"
        required
        maxLength={300}
        value={draft.subject}
        onChange={e => onChange({ subject: e.target.value })}
        error={errors.subject}
        help="نص عادي، ويمكن أن يحتوي متغيرات مثل {{ProjectName}}."
      />
      <TextArea
        id="tpl-message"
        label="نص الرسالة"
        required
        rows={8}
        maxLength={4000}
        value={draft.messageText}
        onChange={e => onChange({ messageText: e.target.value })}
        error={errors.messageText}
        help="نص عادي فقط — اكتب ما تريد أن يقرأه المستلم في بداية الرسالة. باقي الرسالة (جدول بيانات المشروع والزر) ثابت ويُضاف تلقائيًا. افصل بين الفقرات بسطر فارغ، ويمكن استخدام المتغيرات المعروضة بجانب المحرر."
      />

      <div className="flex items-start gap-pc-s">
        <input
          id="tpl-enabled"
          aria-describedby="tpl-enabled-help"
          type="checkbox"
          checked={draft.isEnabled}
          onChange={e => onChange({ isEnabled: e.target.checked })}
          className="mt-1 h-5 w-5 shrink-0 accent-pc-brand-600"
        />
        <div>
          <label htmlFor="tpl-enabled" className="block text-sm font-medium text-pc-gray-800">تفعيل هذه الرسالة</label>
          <p id="tpl-enabled-help" className="text-sm text-pc-gray-600">عند التعطيل لا يُرسل النظام هذا النوع من الرسائل، ولا يتأثر أي إجراء آخر على المشاريع.</p>
        </div>
      </div>

      <div className="flex flex-wrap gap-pc-s border-t border-pc-gray-100 pt-pc-m">
        <Button type="submit" icon={<Save aria-hidden="true" className="h-4 w-4" />} loading={saving} loadingLabel="جارٍ الحفظ…" disabled={!dirty} disabledReason="لا توجد تعديلات لم تُحفظ">
          حفظ التعديلات
        </Button>
        <Button variant="secondary" icon={<Eye aria-hidden="true" className="h-4 w-4" />} loading={previewing} loadingLabel="جارٍ التحضير…" onClick={onPreview}>
          معاينة الرسالة
        </Button>
        <Button variant="secondary" icon={<Mail aria-hidden="true" className="h-4 w-4" />} onClick={onTest}>
          إرسال بريد تجريبي
        </Button>
      </div>
    </form>
  )
}
