
interface CountForms {
  /** المفرد: أسبوع */
  one: string
  /** المثنى كاملًا: أسبوعان */
  two: string
  /** جمع القلة 3–10: أسابيع */
  few: string
  /** المفرد المنصوب 11–99: أسبوعًا */
  many: string
  /** صفة «واحد/واحدة» بعد المفرد. */
  single: string
}

const UNITS = {
  week: { one: 'أسبوع', two: 'أسبوعان', few: 'أسابيع', many: 'أسبوعًا', single: 'واحد' },
  day: { one: 'يوم', two: 'يومان', few: 'أيام', many: 'يومًا', single: 'واحد' },
  hour: { one: 'ساعة', two: 'ساعتان', few: 'ساعات', many: 'ساعة', single: 'واحدة' },
  minute: { one: 'دقيقة', two: 'دقيقتان', few: 'دقائق', many: 'دقيقة', single: 'واحدة' },
  second: { one: 'ثانية', two: 'ثانيتان', few: 'ثوانٍ', many: 'ثانية', single: 'واحدة' },
  task: { one: 'مهمة', two: 'مهمتان', few: 'مهام', many: 'مهمة', single: 'واحدة' },
  project: { one: 'مشروع', two: 'مشروعان', few: 'مشاريع', many: 'مشروعًا', single: 'واحد' },
  employee: { one: 'موظف', two: 'موظفان', few: 'موظفين', many: 'موظفًا', single: 'واحد' },
  record: { one: 'سجل', two: 'سجلان', few: 'سجلات', many: 'سجلًا', single: 'واحد' },
  milestone: { one: 'مرحلة', two: 'مرحلتان', few: 'مراحل', many: 'مرحلة', single: 'واحدة' },
  note: { one: 'ملاحظة', two: 'ملاحظتان', few: 'ملاحظات', many: 'ملاحظة', single: 'واحدة' },
  risk: { one: 'مخاطرة', two: 'مخاطرتان', few: 'مخاطر', many: 'مخاطرة', single: 'واحدة' },
  update: { one: 'تحديث', two: 'تحديثان', few: 'تحديثات', many: 'تحديثًا', single: 'واحد' },
  request: { one: 'طلب', two: 'طلبان', few: 'طلبات', many: 'طلبًا', single: 'واحد' },
  point: { one: 'نقطة', two: 'نقطتان', few: 'نقاط', many: 'نقطة', single: 'واحدة' },
  attempt: { one: 'محاولة', two: 'محاولتان', few: 'محاولات', many: 'محاولة', single: 'واحدة' },
} satisfies Record<string, CountForms>

export type CountUnit = keyof typeof UNITS

/** «3 أسابيع»، «أسبوع واحد»، «أسبوعان»، «0 أسابيع». */
export function formatCount(value: number, unit: CountUnit): string {
  const n = Math.max(0, Math.round(Number.isFinite(value) ? value : 0))
  const f = UNITS[unit]
  if (n === 1) return `${f.one} ${f.single}`
  if (n === 2) return f.two
  if (n === 0) return `0 ${f.few}`
  const tail = n % 100
  if (tail >= 3 && tail <= 10) return `${n} ${f.few}`
  if (tail >= 11) return `${n} ${f.many}`
  return `${n} ${f.one}`
}

