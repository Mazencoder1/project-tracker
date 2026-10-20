
export class ApiError extends Error {
  status: number
  fieldErrors: Record<string, string>

  constructor(message: string, status: number, fieldErrors: Record<string, string> = {}) {
    super(message)
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

/** رسالة عربية واضحة حسب رمز الحالة إن لم يُرسل الخادم رسالة. */
function fallbackMessage(status: number): string {
  if (status === 401) return 'انتهت جلسة الدخول. سجّل الدخول مرة أخرى ثم أعد المحاولة.'
  if (status === 403) return 'لا تملك صلاحية تنفيذ هذا الإجراء على هذا المشروع.'
  if (status === 404) return 'تعذّر العثور على البيانات المطلوبة — ربما حُذفت أو لم تُتح بعد.'
  if (status === 409) return 'تعارضت البيانات مع سجل موجود. حدّث الصفحة ثم أعد المحاولة.'
  if (status >= 500) return 'حدث خطأ في الخادم. أعد المحاولة بعد قليل.'
  return 'تعذّر تنفيذ الطلب. راجع البيانات ثم أعد المحاولة.'
}

export async function apiJson<T>(url: string, init?: RequestInit): Promise<T> {
  let res: Response
  try {
    res = await fetch(url, {
      cache: 'no-store',
      ...init,
      headers: init?.body ? { 'Content-Type': 'application/json', ...init?.headers } : init?.headers,
    })
  } catch {
    throw new ApiError('تعذّر الاتصال بالخادم. تحقّق من الاتصال ثم أعد المحاولة.', 0)
  }
  const data = (await res.json().catch(() => null)) as (T & { error?: string; errors?: Record<string, string> }) | null
  if (!res.ok) {
    throw new ApiError(data?.error || fallbackMessage(res.status), res.status, data?.errors ?? {})
  }
  return data as T
}

/** نص خطأ مقروء من أي استثناء. */
export function errorMessage(error: unknown, fallback = 'حدث خطأ غير متوقع. أعد المحاولة.'): string {
  if (error instanceof ApiError) return error.message
  if (error instanceof Error && error.message) return error.message
  return fallback
}

/** اليوم المحلي بصيغة YYYY-MM-DD (نفس todayDateOnly في calc.ts). */
export function todayIso(now = new Date()): string {
  const y = now.getFullYear()
  const m = String(now.getMonth() + 1).padStart(2, '0')
  const d = String(now.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}
