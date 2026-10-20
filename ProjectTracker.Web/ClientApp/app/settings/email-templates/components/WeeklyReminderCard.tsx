'use client'

import { CalendarClock } from 'lucide-react'
import { Button, Card, Input, Select } from '@/app/components/ui'
import { WEEK_DAYS, type WeeklyReminderSettings } from '../types'

export function WeeklyReminderCard({
  settings,
  errors,
  dirty,
  saving,
  onChange,
  onSave,
}: Readonly<{
  settings: WeeklyReminderSettings
  errors: Record<string, string>
  dirty: boolean
  saving: boolean
  onChange: (patch: Partial<WeeklyReminderSettings>) => void
  onSave: () => void
}>) {
  return (
    <Card
      as="section"
      title="جدولة التذكير الأسبوعي"
      titleAs="h2"
      subtitle={`يُرسل التذكير لكل مسؤول عن مشروع لم ينتهِ، مرة واحدة أسبوعيًا. التوقيت: ${settings.timeZone}.`}
    >
      <form
        onSubmit={e => {
          e.preventDefault()
          onSave()
        }}
        noValidate
        className="flex flex-col gap-pc-m"
      >
        <label htmlFor="weekly-enabled" className="flex items-center gap-pc-s">
          <input
            id="weekly-enabled"
            type="checkbox"
            checked={settings.enabled}
            onChange={e => onChange({ enabled: e.target.checked })}
            className="h-5 w-5 shrink-0 accent-pc-brand-600"
          />
          <span className="text-sm font-medium text-pc-gray-800">تفعيل التذكير الأسبوعي</span>
        </label>
        <div className="grid gap-pc-m sm:grid-cols-2">
          <Select
            id="weekly-day"
            label="يوم الإرسال"
            value={String(settings.day)}
            onChange={value => onChange({ day: Number(value) })}
            options={WEEK_DAYS}
            error={errors.day}
          />
          <Input
            id="weekly-time"
            label="وقت الإرسال"
            type="time"
            dir="ltr"
            value={settings.time}
            onChange={e => onChange({ time: e.target.value })}
            error={errors.time}
            help="بصيغة 24 ساعة."
          />
        </div>
        <div>
          <Button type="submit" icon={<CalendarClock aria-hidden="true" className="h-4 w-4" />} loading={saving} loadingLabel="جارٍ الحفظ…" disabled={!dirty} disabledReason="لا توجد تعديلات لم تُحفظ">
            حفظ الجدولة
          </Button>
        </div>
      </form>
    </Card>
  )
}
