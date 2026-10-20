'use client'

import { useEffect, useState } from 'react'
import { useRouter } from 'next/navigation'
import { Eye, EyeOff, KeyRound, Mail, MailQuestion, ScrollText, ShieldCheck } from 'lucide-react'
import {
  Alert,
  AppShell,
  Button,
  ButtonLink,
  Card,
  FormErrorSummary,
  FormRequiredHint,
  hasInternalHistory,
  Input,
  useToast,
} from '@/app/components/ui'

export default function SettingsPage() {
  const router = useRouter()
  const toast = useToast()
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [visiblePasswords, setVisiblePasswords] = useState<Record<string, boolean>>({})
  const [isAdmin, setIsAdmin] = useState(false)
  useEffect(() => {
    let cancelled = false
    fetch('/api/auth/me', { cache: 'no-store' })
      .then(res => (res.ok ? res.json() : null))
      .then((me: { role?: string } | null) => {
        if (!cancelled && me?.role === 'admin') setIsAdmin(true)
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [])
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})

  /** تحقّق من جانب العميل فقط — التحقق من جانب الخادم يبقى إلزاميًا كما هو. */
  function validate(): Record<string, string> {
    const errs: Record<string, string> = {}
    if (!currentPassword) errs['settings-current'] = 'أدخل كلمة المرور الحالية'
    if (!newPassword) errs['settings-new'] = 'أدخل كلمة المرور الجديدة'
    else if (!newPassword.trim()) errs['settings-new'] = 'كلمة المرور لا يمكن أن تتكون من مسافات فقط'
    else if (newPassword.length < 8)
      errs['settings-new'] = 'كلمة المرور الجديدة يجب ألا تقل عن 8 محارف'
    if (!confirmPassword) errs['settings-confirm'] = 'أعد كتابة كلمة المرور الجديدة للتأكيد'
    else if (newPassword && confirmPassword !== newPassword)
      errs['settings-confirm'] = 'كلمتا المرور غير متطابقتين'
    return errs
  }

  // Note: الفورم يرسل كلمة المرور فقط للـ API، والـ API يحدد المستخدم الحالي من كوكي user_id.
  async function changePassword(e: React.FormEvent) {
    e.preventDefault()
    const errs = validate()
    setFieldErrors(errs)
    if (Object.keys(errs).length > 0) return

    setLoading(true)
    setMessage('')
    setError('')

    try {
      const res = await fetch('/api/auth/change-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ currentPassword, newPassword, confirmPassword }),
      })
      const data = await res.json()

      if (!res.ok) {
        setError(data.error || 'تعذّر تغيير كلمة المرور')
        return
      }

      setMessage(data.message || 'تم تغيير كلمة المرور بنجاح')
      toast.success(data.message || 'تم تغيير كلمة المرور بنجاح')
      setCurrentPassword('')
      setNewPassword('')
      setConfirmPassword('')
    } catch {
      setError('تعذّر الاتصال بالخادم')
    } finally {
      setLoading(false)
    }
  }

  const [resetConfirm, setResetConfirm] = useState(false)
  const [resetLoading, setResetLoading] = useState(false)
  const [resetMessage, setResetMessage] = useState('')
  const [resetError, setResetError] = useState('')

  async function sendNewPassword() {
    setResetLoading(true)
    setResetError('')
    setResetMessage('')
    try {
      const res = await fetch('/api/auth/reset-password', { method: 'POST' })
      const data = await res.json()
      if (!res.ok) {
        setResetError(data.error || 'تعذّر إرسال كلمة مرور جديدة')
        return
      }
      setResetMessage(data.message)
      toast.success('أُرسلت كلمة مرور جديدة إلى بريدك')
      setResetConfirm(false)
    } catch {
      setResetError('تعذّر الاتصال بالخادم')
    } finally {
      setResetLoading(false)
    }
  }

  const summaryErrors = Object.entries(fieldErrors).map(([fieldId, m]) => ({
    fieldId,
    message: m,
  }))

  const passwordChecks = [
    newPassword.length >= 8,
    /[A-Zأ-ي]/.test(newPassword) && /[a-zأ-ي]/.test(newPassword),
    /\d/.test(newPassword),
    /[^\w\sأ-ي]/.test(newPassword),
  ]
  const passwordScore = passwordChecks.filter(Boolean).length
  const confirmMismatch =
    confirmPassword && newPassword && confirmPassword !== newPassword &&
    (confirmPassword.length >= newPassword.length || !newPassword.startsWith(confirmPassword))
      ? 'كلمتا المرور غير متطابقتين'
      : undefined
  const strengthLabel = ['ضعيفة جدًا', 'ضعيفة', 'متوسطة', 'جيدة', 'قوية'][passwordScore]
  let strengthColor = 'bg-pc-success-600'
  if (passwordScore <= 1) strengthColor = 'bg-pc-error-600'
  else if (passwordScore <= 2) strengthColor = 'bg-pc-warning-600'

  function passwordToggle(field: string, label: string) {
    const visible = Boolean(visiblePasswords[field])
    return (
      <button
        type="button"
        aria-label={visible ? `إخفاء ${label}` : `إظهار ${label}`}
        aria-pressed={visible}
        onClick={() => setVisiblePasswords(current => ({ ...current, [field]: !visible }))}
        className="flex h-10 w-10 items-center justify-center rounded-pc-md text-pc-gray-600 hover:bg-pc-gray-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-pc-brand-600"
      >
        {visible ? <EyeOff aria-hidden="true" className="h-5 w-5" /> : <Eye aria-hidden="true" className="h-5 w-5" />}
      </button>
    )
  }

  function clearFieldError(fieldId: string) {
    setFieldErrors(current => {
      if (!current[fieldId]) return current
      const next = { ...current }
      delete next[fieldId]
      return next
    })
  }

  return (
    <AppShell
      title="إعدادات الحساب"
      subtitle="حدّث كلمة مرورك بأمان من مكان واحد."
      breadcrumbs={[{ label: 'الإعدادات' }]}
    >
      <div className={`grid w-full items-start gap-pc-m ${isAdmin ? 'max-w-[95rem] lg:grid-cols-2 xl:grid-cols-3' : 'max-w-[40rem]'}`}>
          <Card as="section" padded={false} className="overflow-hidden shadow-pc-float">
            <div className="flex items-start gap-pc-s border-b border-pc-gray-100 bg-pc-gray-50 px-pc-m py-pc-m">
              <span aria-hidden="true" className="flex h-10 w-10 shrink-0 items-center justify-center rounded-pc-md bg-pc-brand-100 text-pc-brand-800">
                <KeyRound className="h-5 w-5" />
              </span>
              <div>
                <h2 className="text-pc-h4 text-pc-gray-900">تغيير كلمة المرور</h2>
                <p className="mt-1 text-sm text-pc-gray-600">استخدم كلمة مرور لا تستخدمها في حسابات أخرى.</p>
              </div>
            </div>
            <form onSubmit={changePassword} className="flex flex-col gap-pc-m p-pc-m" noValidate>
              <label htmlFor="settings-username" className="sr-only">اسم المستخدم</label>
              <input
                id="settings-username"
                name="username"
                type="text"
                autoComplete="username"
                tabIndex={-1}
                className="sr-only"
              />
              <FormRequiredHint />

              {summaryErrors.length > 0 && <FormErrorSummary errors={summaryErrors} />}

              <Input
                id="settings-current"
                name="current-password"
                label="كلمة المرور الحالية"
                type={visiblePasswords.current ? 'text' : 'password'}
                dir="ltr"
                inputClassName="text-left"
                required
                value={currentPassword}
                onChange={e => { setCurrentPassword(e.target.value); clearFieldError('settings-current') }}
                error={fieldErrors['settings-current']}
                autoComplete="current-password"
                endAdornment={passwordToggle('current', 'كلمة المرور الحالية')}
              />
              <Input
                id="settings-new"
                name="new-password"
                label="كلمة المرور الجديدة"
                type={visiblePasswords.new ? 'text' : 'password'}
                dir="ltr"
                inputClassName="text-left"
                required
                value={newPassword}
                onChange={e => { setNewPassword(e.target.value); clearFieldError('settings-new') }}
                error={fieldErrors['settings-new']}
                help="8 محارف على الأقل."
                autoComplete="new-password"
                endAdornment={passwordToggle('new', 'كلمة المرور الجديدة')}
              />
              {newPassword && (
                <div className="rounded-pc-md border border-pc-gray-200 bg-pc-gray-50 p-pc-s" aria-live="polite">
                  <div className="flex items-center justify-between gap-pc-s text-sm">
                    <span className="flex items-center gap-pc-xs font-medium text-pc-gray-700">
                      <ShieldCheck aria-hidden="true" className="h-4 w-4" /> قوة كلمة المرور
                    </span>
                    <span className="text-pc-gray-600">{strengthLabel}</span>
                  </div>
                  <div className="mt-pc-s grid grid-cols-4 gap-pc-xs" aria-hidden="true">
                    {[0, 1, 2, 3].map(index => (
                      <span key={index} className={`h-1.5 rounded-full ${index < passwordScore ? strengthColor : 'bg-pc-gray-200'}`} />
                    ))}
                  </div>
                  <p className="mt-pc-s text-xs text-pc-gray-600">لزيادة القوة امزج بين الحروف والأرقام والرموز.</p>
                </div>
              )}
              <Input
                id="settings-confirm"
                name="confirm-password"
                label="تأكيد كلمة المرور الجديدة"
                type={visiblePasswords.confirm ? 'text' : 'password'}
                dir="ltr"
                inputClassName="text-left"
                required
                value={confirmPassword}
                onChange={e => { setConfirmPassword(e.target.value); clearFieldError('settings-confirm') }}
                error={fieldErrors['settings-confirm'] || confirmMismatch}
                success={confirmPassword && confirmPassword === newPassword && !fieldErrors['settings-confirm'] ? 'كلمتا المرور متطابقتان' : undefined}
                autoComplete="new-password"
                endAdornment={passwordToggle('confirm', 'تأكيد كلمة المرور')}
              />

              {message && (
                <Alert tone="success" title="تم تغيير كلمة المرور" onDismiss={() => setMessage('')}>
                  {message} — استخدم كلمة المرور الجديدة في تسجيل الدخول القادم.
                </Alert>
              )}

              {error && (
                <Alert tone="danger" title="تعذّر تغيير كلمة المرور">
                  {error}
                </Alert>
              )}

              <div className="flex flex-col gap-pc-s sm:flex-row">
                <Button type="submit" loading={loading} loadingLabel="جارٍ الحفظ…">
                  حفظ كلمة المرور
                </Button>
                <Button variant="ghost" onClick={() => (hasInternalHistory() ? router.back() : window.location.assign('/'))}>
                  إلغاء والرجوع
                </Button>
              </div>
            </form>

            <div className="flex flex-col gap-pc-s border-t border-pc-gray-100 bg-pc-gray-50 p-pc-m">
              <h3 className="flex items-center gap-pc-xs text-base font-bold text-pc-gray-900">
                <MailQuestion aria-hidden="true" className="h-5 w-5 text-pc-brand-800" />
                نسيت كلمة المرور الحالية؟
              </h3>
              <p className="text-sm text-pc-gray-600">نرسل كلمة مرور جديدة إلى بريدك الإلكتروني المسجّل، وتتوقف الحالية عن العمل.</p>
              {resetMessage && (
                <Alert tone="success" title="تم الإرسال" onDismiss={() => setResetMessage('')}>{resetMessage}</Alert>
              )}
              {resetError && (
                <Alert tone="danger" title="تعذّر الإرسال" onDismiss={() => setResetError('')}>{resetError}</Alert>
              )}
              {resetConfirm ? (
                <div className="flex flex-col gap-pc-s sm:flex-row">
                  <Button onClick={sendNewPassword} loading={resetLoading} loadingLabel="جارٍ الإرسال…">
                    تأكيد الإرسال إلى بريدي
                  </Button>
                  <Button variant="ghost" onClick={() => setResetConfirm(false)}>إلغاء</Button>
                </div>
              ) : (
                <div>
                  <Button variant="secondary" onClick={() => { setResetConfirm(true); setResetError('') }}>
                    إرسال كلمة مرور جديدة إلى بريدي
                  </Button>
                </div>
              )}
            </div>
          </Card>

          {isAdmin && (
            <Card as="section" padded={false} className="overflow-hidden shadow-pc-float">
              <div className="flex items-start gap-pc-s px-pc-m py-pc-m">
                <span aria-hidden="true" className="flex h-10 w-10 shrink-0 items-center justify-center rounded-pc-md bg-pc-brand-100 text-pc-brand-800">
                  <Mail className="h-5 w-5" />
                </span>
                <div className="min-w-0 flex-1">
                  <h2 className="text-pc-h4 text-pc-gray-900">قوالب البريد الإلكتروني</h2>
                  <p className="mt-1 text-sm text-pc-gray-600">عدّل عنوان الرسائل التلقائية للمشاريع ومحتواها وجدولة التذكير الأسبوعي. متاح لمدير النظام فقط.</p>
                  <div className="mt-pc-s">
                    <ButtonLink href="/settings/email-templates" variant="secondary">فتح قوالب البريد</ButtonLink>
                  </div>
                </div>
              </div>
            </Card>
          )}

          {isAdmin && (
            <Card as="section" padded={false} className="overflow-hidden shadow-pc-float">
              <div className="flex items-start gap-pc-s px-pc-m py-pc-m">
                <span aria-hidden="true" className="flex h-10 w-10 shrink-0 items-center justify-center rounded-pc-md bg-pc-brand-100 text-pc-brand-800">
                  <ScrollText className="h-5 w-5" />
                </span>
                <div className="min-w-0 flex-1">
                  <h2 className="text-pc-h4 text-pc-gray-900">سجلات النظام</h2>
                  <p className="mt-1 text-sm text-pc-gray-600">تتبّع إرسال البريد الإلكتروني والرسائل النصية وإنشاء المشاريع وإسنادها. متاح لمدير النظام فقط.</p>
                  <div className="mt-pc-s">
                    <ButtonLink href="/settings/logs" variant="secondary">فتح السجلات</ButtonLink>
                  </div>
                </div>
              </div>
            </Card>
          )}
      </div>
    </AppShell>
  )
}
