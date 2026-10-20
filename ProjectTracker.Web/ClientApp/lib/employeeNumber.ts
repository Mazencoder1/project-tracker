export function normalizeEmployeeNumber(value: unknown): string {
  if (typeof value !== 'string' && typeof value !== 'number') return ''
  return String(value)
    .trim()
    .replace(/[٠-٩]/g, digit => String('٠١٢٣٤٥٦٧٨٩'.indexOf(digit)))
    .replace(/[۰-۹]/g, digit => String('۰۱۲۳۴۵۶۷۸۹'.indexOf(digit)))
}

export function employeeNumberProblem(value: unknown): string | null {
  const normalized = normalizeEmployeeNumber(value)
  if (!normalized) return 'الرقم الوظيفي مطلوب.'
  if (!/^\d{1,20}$/.test(normalized)) return 'الرقم الوظيفي يجب أن يتكون من أرقام فقط وبحد أقصى 20 رقمًا.'
  return null
}

