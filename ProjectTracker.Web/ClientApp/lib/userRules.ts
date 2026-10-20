
export const USER_NAME_MAX = 100
export const USER_EMAIL_MAX = 150
export const USER_DEPARTMENT_MAX = 100
const PASSWORD_MIN = 8
const PASSWORD_MAX = 255
const USER_ROLES = ['admin', 'deputy', 'pm'] as const
export type UserRole = (typeof USER_ROLES)[number]

/** التسمية العربية لكل دور — مصدر واحد للواجهة كلها (الشريط الجانبي، الإدارة، النوافذ). */
export const USER_ROLE_LABEL: Record<UserRole, string> = {
  admin: 'مدير النظام',
  deputy: 'المدير التنفيذي',
  pm: 'مدير مشروع',
}

/** صيغة بريد معقولة: جزءان حول «@» واحدة، والنطاق يحوي نقطة داخلية، ولا مسافات. (بلا regex لتفادي البطء الأُسّي.) */
export function isValidEmail(value: string): boolean {
  if (value.length > 254 || /\s/.test(value)) return false
  const parts = value.split('@')
  if (parts.length !== 2) return false
  const [local, domain] = parts
  if (!local || !domain) return false
  const dot = domain.indexOf('.')
  return dot > 0 && dot < domain.length - 1
}

/** سبب رفض كلمة المرور، أو null إن كانت مقبولة. المسافات جزء من كلمة المرور ولا تُقصّ. */
export function passwordProblem(password: unknown): string | null {
  if (typeof password !== 'string' || password.length === 0) return 'كلمة المرور مطلوبة — أدخل كلمة مرور للحساب.'
  if (password.trim().length === 0) return 'كلمة المرور لا يمكن أن تتكون من مسافات فقط — أضف حروفًا أو أرقامًا.'
  if (password.length < PASSWORD_MIN) return `كلمة المرور يجب ألا تقل عن ${PASSWORD_MIN} محارف.`
  if (password.length > PASSWORD_MAX) return `كلمة المرور يجب ألا تزيد على ${PASSWORD_MAX} محرفًا.`
  return null
}

export interface UserProfileInput {
  full_name?: unknown
  email?: unknown
  department?: unknown
  role?: unknown
}

export interface UserProfileValue {
  full_name?: string
  email?: string
  department?: string
  role?: UserRole
}

/**
 * يتحقق من حقول الموظف. partial=true (التعديل) يتحقق فقط من المفاتيح المرسلة؛ وإلا فالاسم
 * والبريد والدور مطلوبة. يعيد القيم المنظّفة (مقصوصة المسافات) وأخطاء لكل حقل.
 */
export function validateUserProfile(
  input: UserProfileInput,
  { partial = false }: { partial?: boolean } = {}
): { value: UserProfileValue; errors: Record<string, string> } {
  const value: UserProfileValue = {}
  const errors: Record<string, string> = {}
  const has = (key: keyof UserProfileInput) => input[key] !== undefined

  if (!partial || has('full_name')) checkFullName(input.full_name, value, errors)
  if (!partial || has('email')) checkEmail(input.email, value, errors)
  if (has('department')) checkDepartment(input.department, value, errors)
  if (!partial || has('role')) checkRole(input.role, value, errors)
  return { value, errors }
}

const trimmedText = (v: unknown) => (typeof v === 'string' ? v.trim() : '')

function checkFullName(raw: unknown, value: UserProfileValue, errors: Record<string, string>) {
  const name = trimmedText(raw)
  if (!name) errors.full_name = 'الاسم الكامل مطلوب — اكتب اسم الموظف كما في سجلاته.'
  else if (name.length > USER_NAME_MAX) errors.full_name = `الاسم الكامل يجب ألا يزيد على ${USER_NAME_MAX} حرف.`
  else value.full_name = name
}

function checkEmail(raw: unknown, value: UserProfileValue, errors: Record<string, string>) {
  const email = trimmedText(raw)
  if (!email) errors.email = 'البريد الإلكتروني مطلوب — يُستخدم لتسجيل دخول الموظف.'
  else if (!isValidEmail(email)) errors.email = 'صيغة البريد الإلكتروني غير صحيحة — مثال: name@example.com'
  else if (email.length > USER_EMAIL_MAX) errors.email = `البريد الإلكتروني يجب ألا يزيد على ${USER_EMAIL_MAX} حرفًا.`
  else value.email = email
}

function checkDepartment(raw: unknown, value: UserProfileValue, errors: Record<string, string>) {
  const department = trimmedText(raw)
  if (department.length > USER_DEPARTMENT_MAX) errors.department = `اسم القسم يجب ألا يزيد على ${USER_DEPARTMENT_MAX} حرف.`
  else value.department = department
}

function checkRole(raw: unknown, value: UserProfileValue, errors: Record<string, string>) {
  if ((USER_ROLES as readonly unknown[]).includes(raw)) value.role = raw as UserRole
  else errors.role = 'الدور غير صحيح — اختر «مدير مشروع» أو «المدير التنفيذي» أو «مدير النظام».'
}
