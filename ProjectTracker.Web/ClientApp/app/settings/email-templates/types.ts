export interface EmailTemplate {
  templateKey: string
  nameAr: string
  subject: string
  /** فقرة نص عادية بلا وسوم HTML؛ الإطار الثابت (الترويسة والجدول والزر) في الخادم ولا يظهر هنا. */
  messageText: string
  isEnabled: boolean
  whenSent: string
  updatedAt: string
  updatedByName: string | null
}

export interface EmailVariable {
  name: string
  label: string
  sample: string
  isHtml: boolean
}

export interface WeeklyReminderSettings {
  enabled: boolean
  /** 0 = الأحد … 6 = السبت */
  day: number
  /** HH:mm بتوقيت النظام */
  time: string
  timeZone: string
}

export interface EmailTemplatesResponse {
  templates: EmailTemplate[]
  variables: EmailVariable[]
  weeklyReminder: WeeklyReminderSettings
  mail: { enabled: boolean; hasBaseUrl: boolean }
}

export interface EmailPreview {
  subject: string
  /** الرسالة الكاملة المُصيَّرة (إطار ثابت + النص + قيم حقيقية) لعرضها في المعاينة فقط. */
  bodyHtml: string
}

/** ما يحرّره مدير النظام في القالب المحدّد. */
export interface TemplateDraft {
  nameAr: string
  subject: string
  messageText: string
  isEnabled: boolean
}

export const WEEK_DAYS = [
  { value: '0', label: 'الأحد' },
  { value: '1', label: 'الاثنين' },
  { value: '2', label: 'الثلاثاء' },
  { value: '3', label: 'الأربعاء' },
  { value: '4', label: 'الخميس' },
  { value: '5', label: 'الجمعة' },
  { value: '6', label: 'السبت' },
]
