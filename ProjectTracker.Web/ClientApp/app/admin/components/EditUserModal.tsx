'use client'
import { useState } from 'react'
import { Alert, FormErrorSummary, FormRequiredHint, Input, Modal, Select, useToast } from '@/app/components/ui'
import { ApiError, apiJson, errorMessage } from '@/app/components/executive/api'
import { USER_DEPARTMENT_MAX, USER_NAME_MAX, passwordProblem, validateUserProfile } from '@/lib/userRules'

export interface EditableUser {
  id: string
  full_name: string
  email: string
  role: string
  department: string | null
  is_self?: boolean
}

/** ترتيب الحقول كما تظهر في النافذة (يحدد ترتيب ملخص الأخطاء والتركيز). */
const FIELD_IDS = {
  full_name: 'edit-user-name',
  department: 'edit-user-dept',
  role: 'edit-user-role',
  password: 'edit-user-password',
  confirm: 'edit-user-confirm',
} as const
type FieldKey = keyof typeof FIELD_IDS
const FIELD_ORDER = Object.keys(FIELD_IDS) as FieldKey[]

export function EditUserModal({
  user,
  onClose,
  onSaved,
}: Readonly<{
  user: EditableUser
  onClose: () => void
  onSaved: (message: string) => void
}>) {
  const toast = useToast()
  const [fullName, setFullName] = useState(user.full_name)
  const [department, setDepartment] = useState(user.department ?? '')
  const [role, setRole] = useState(user.role)
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [tried, setTried] = useState(false)
  const [serverErrors, setServerErrors] = useState<Record<string, string>>({})
  const [message, setMessage] = useState('')
  const [saving, setSaving] = useState(false)

  function validate(): Partial<Record<FieldKey, string>> {
    const errors: Partial<Record<FieldKey, string>> = {}
    Object.assign(errors, validateUserProfile({ full_name: fullName, department, role }, { partial: true }).errors)
    // كلمة المرور اختيارية هنا: تُتحقق فقط إن بدأ المدير كتابتها.
    if (password || confirm) {
      const problem = passwordProblem(password)
      if (problem) errors.password = problem
      else if (confirm !== password) errors.confirm = 'كلمتا المرور غير متطابقتين — أعد كتابة كلمة المرور الجديدة بنفس الحروف.'
    }
    return errors
  }

  const errors: Partial<Record<FieldKey, string>> = { ...serverErrors, ...(tried ? validate() : {}) }
  const summary = FIELD_ORDER.filter(key => errors[key]).map(key => ({ fieldId: FIELD_IDS[key], message: errors[key] as string }))

  function edited<T>(setter: (value: T) => void) {
    return (value: T) => {
      setter(value)
      if (Object.keys(serverErrors).length > 0) setServerErrors({})
    }
  }

  async function save() {
    if (saving) return
    setTried(true)
    setMessage('')
    setServerErrors({})
    const firstInvalid = FIELD_ORDER.find(key => validate()[key])
    if (firstInvalid) {
      requestAnimationFrame(() => document.getElementById(FIELD_IDS[firstInvalid])?.focus())
      return
    }

    const body: Record<string, string> = {}
    if (fullName.trim() !== user.full_name) body.full_name = fullName.trim()
    if (department.trim() !== (user.department ?? '')) body.department = department.trim()
    if (!user.is_self && role !== user.role) body.role = role
    if (password) body.password = password
    if (Object.keys(body).length === 0) {
      toast.info('لا توجد تغييرات لحفظها.')
      onClose()
      return
    }

    setSaving(true)
    try {
      await apiJson(`/api/users/${user.id}`, { method: 'PATCH', body: JSON.stringify(body) })
      const endsSessions = body.password !== undefined || body.role !== undefined
      onSaved(endsSessions
        ? 'تم تحديث بيانات الموظف، وأُنهيت جلساته المفتوحة ليدخل بالبيانات الجديدة.'
        : 'تم تحديث بيانات الموظف.')
    } catch (e) {
      if (e instanceof ApiError) setServerErrors(e.fieldErrors)
      setMessage(errorMessage(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      title="تعديل بيانات الموظف"
      subtitle={user.email}
      maxWidth={560}
      onClose={onClose}
      onSave={save}
      saveLabel="حفظ التعديل"
      saveLoading={saving}
    >
      <div className="flex flex-col gap-pc-m">
        <FormRequiredHint />
        {message && <Alert tone="danger" title="لم تُحفظ التعديلات">{message}</Alert>}
        <FormErrorSummary errors={summary} autoFocus={false} />
        <Input
          id={FIELD_IDS.full_name}
          label="الاسم الكامل"
          required
          maxLength={USER_NAME_MAX}
          value={fullName}
          onChange={e => edited(setFullName)(e.target.value)}
          error={errors.full_name}
        />
        <Input
          id={FIELD_IDS.department}
          label="القسم (اختياري)"
          maxLength={USER_DEPARTMENT_MAX}
          value={department}
          onChange={e => edited(setDepartment)(e.target.value)}
          error={errors.department}
        />
        <Select
          id={FIELD_IDS.role}
          label="الدور"
          required
          value={role}
          onChange={edited(setRole)}
          disabled={user.is_self}
          help={user.is_self
            ? 'لا يمكنك تغيير دور حسابك بنفسك — اطلب ذلك من مدير نظام آخر.'
            : 'تغيير الدور يُنهي جلسات الموظف المفتوحة ليدخل بصلاحياته الجديدة.'}
          error={errors.role}
          options={[
            { value: 'pm', label: 'مدير مشروع' },
            { value: 'deputy', label: 'المدير التنفيذي' },
            { value: 'admin', label: 'مدير النظام' },
          ]}
        />
        <fieldset className="rounded-pc-md border border-pc-gray-200 p-pc-m">
          <legend className="px-pc-xs text-sm font-semibold text-pc-gray-900">إعادة تعيين كلمة المرور (اختياري)</legend>
          <p className="mb-pc-s text-sm text-pc-gray-600">
            اتركه فارغًا للإبقاء على كلمة المرور الحالية. عند التعيين تُنهى جلسات الموظف المفتوحة.
          </p>
          <div className="flex flex-col gap-pc-s">
            <Input
              id={FIELD_IDS.password}
              label="كلمة المرور الجديدة"
              type="password"
              autoComplete="new-password"
              help="8 محارف على الأقل، وليست مسافات فقط."
              value={password}
              onChange={e => edited(setPassword)(e.target.value)}
              error={errors.password}
            />
            <Input
              id={FIELD_IDS.confirm}
              label="تأكيد كلمة المرور الجديدة"
              type="password"
              autoComplete="new-password"
              value={confirm}
              onChange={e => edited(setConfirm)(e.target.value)}
              error={errors.confirm}
            />
          </div>
        </fieldset>
      </div>
    </Modal>
  )
}
