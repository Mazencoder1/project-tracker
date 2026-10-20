
/** طول الرمز المرسَل. ستة أرقام: ما اعتاده المستخدم في الخدمات الحكومية. */
export const OTP_LENGTH = 6
/** صلاحية الرمز بالثواني. */
export const OTP_TTL_SEC = 5 * 60

/** الانتظار الإلزامي بين إرسالين بالثواني. */
export const OTP_RESEND_COOLDOWN_SEC = 60

/** الرمز المقبول شكلًا: أرقام لاتينية بالطول المحدد بالضبط. */
export function isValidCodeFormat(code: unknown): code is string {
  return typeof code === 'string' && new RegExp(String.raw`^\d{${OTP_LENGTH}}$`).test(code.trim())
}

/** «•••• 1234» للعرض في الواجهة. */
export function maskMobile(tail: string | null): string {
  return tail ? `•••• ${tail}` : 'رقم جوالك المسجّل'
}

