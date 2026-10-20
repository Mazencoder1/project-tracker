'use client'

// شاشة الدخول: البريد الإلكتروني وكلمة المرور، مع رمز تحقق بالجوال إن ضُبط مزوّد رسائل (ISmsService).
// التصميم: لوحة هوية بشعار النظام وزخرفة من أشكاله، وبطاقة دخول يتصدّرها رمز الشعار.
import { useEffect, useState } from 'react'
import { useRouter } from 'next/navigation'
import { CheckCircle2, KeyRound, RefreshCw, ShieldCheck, Smartphone } from 'lucide-react'
import {
  Alert,
  AppLogo,
  BrandPattern,
  Button,
  FormErrorSummary,
  FormRequiredHint,
  Input,
  Modal,
  SkipLink,
  useToast,
} from '@/app/components/ui'
import { LOGIN_TITLE, SYSTEM_TAGLINE } from '@/lib/org'
import { OTP_LENGTH, OTP_RESEND_COOLDOWN_SEC, OTP_TTL_SEC, isValidCodeFormat, maskMobile } from '@/lib/otpRules'
import { isValidEmail } from '@/lib/userRules'

const OTP_SECONDS = OTP_TTL_SEC
const RESEND_COOLDOWN = OTP_RESEND_COOLDOWN_SEC

const BENEFITS = [
  'متابعة المراحل والمهام ونسب الإنجاز لكل مشروع',
  'لوحة الاجتماع الدوري بتقرير PDF جاهز',
  'صلاحيات مستقلة لمدير النظام ومديري المشاريع',
]

function validatePasswordLogin(email: string, password: string): { fieldId: string; message: string }[] {
  const errors: { fieldId: string; message: string }[] = []
  if (!email.trim()) errors.push({ fieldId: 'login-email', message: 'البريد الإلكتروني مطلوب — اكتب بريدك الإلكتروني.' })
  else if (!isValidEmail(email.trim())) errors.push({ fieldId: 'login-email', message: 'صيغة البريد الإلكتروني غير صحيحة — مثال: name@example.com' })
  if (!password) errors.push({ fieldId: 'login-password', message: 'كلمة المرور مطلوبة — اكتب كلمة مرور حسابك.' })
  return errors
}

// Server worst case: 15s primary SMTP + 15s fallback, plus the database work.
const RESET_REQUEST_TIMEOUT_MS = 45_000
const RESET_TIMEOUT_MESSAGE = 'خادم البريد لا يستجيب حاليًا، ولم تتغير كلمة مرورك. حاول لاحقًا أو تواصل مع مدير النظام.'

export default function LoginPage() {
  const router = useRouter()
  const toast = useToast()

  const [loginError, setLoginError] = useState('')

  /* ─────────────────── رمز التحقق (OTP) — يعلو المسارين ─────────────────── */
  // بعد كلمة المرور الصحيحة يتوقف الدخول هنا لمن له رقم جوال مسجّل (متى ضُبط مزوّد رسائل): لا جلسة حتى يُدخل الرمز.
  const [otpPending, setOtpPending] = useState(false)
  const [otpTail, setOtpTail] = useState<string | null>(null)
  const [otpCode, setOtpCode] = useState('')
  const [otpError, setOtpError] = useState('')
  const [otpLoading, setOtpLoading] = useState(false)
  const [otpSeconds, setOtpSeconds] = useState(0)
  const [resendWait, setResendWait] = useState(0)
  const [resendLoading, setResendLoading] = useState(false)

  /* ─────────────────── مسار كلمة المرور ─────────────────── */
  const [pwOpen, setPwOpen] = useState(false)
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [pwError, setPwError] = useState('')
  const [pwLoading, setPwLoading] = useState(false)
  const [pwTried, setPwTried] = useState(false)
  const pwErrors = pwTried ? validatePasswordLogin(email, password) : []
  const [forgotView, setForgotView] = useState(false)
  const [forgotError, setForgotError] = useState('')
  const [forgotMessage, setForgotMessage] = useState('')
  const [forgotLoading, setForgotLoading] = useState(false)
  const [resetToken, setResetToken] = useState('')
  const [resetMessage, setResetMessage] = useState('')
  const [resetError, setResetError] = useState('')
  const [resetLoading, setResetLoading] = useState(false)

  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const token = params.get('reset_token')
    if (!token) return
    setResetToken(token)
    params.delete('reset_token')
    const qs = params.toString()
    const search = qs ? `?${qs}` : ''
    window.history.replaceState(null, '', `${window.location.pathname}${search}`)
  }, [])

  async function confirmPasswordReset() {
    if (resetLoading) return
    setResetLoading(true)
    setResetError('')
    try {
      const res = await fetch('/api/auth/forgot-password/confirm', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token: resetToken }),
        signal: AbortSignal.timeout(RESET_REQUEST_TIMEOUT_MS),
      })
      const data = await res.json().catch(() => null)
      if (!res.ok) setResetError(data?.error || 'تعذّر إكمال الطلب')
      else setResetMessage(data?.message || '')
    } catch (err) {
      setResetError(err instanceof DOMException && err.name === 'TimeoutError' ? RESET_TIMEOUT_MESSAGE : 'تعذّر الاتصال بالخادم')
    } finally {
      setResetLoading(false)
    }
  }

  function closeResetModal() {
    setResetToken('')
    setResetMessage('')
    setResetError('')
  }

  // عدّاد صلاحية الرمز وعدّاد الانتظار قبل إعادة الإرسال.
  useEffect(() => {
    if (!otpPending) return
    const t = setInterval(() => {
      setOtpSeconds(v => (v > 0 ? v - 1 : 0))
      setResendWait(v => (v > 0 ? v - 1 : 0))
    }, 1000)
    return () => clearInterval(t)
  }, [otpPending])

  /** الانتقال بعد نجاح الدخول حسب الدور. */
  function goHome(role: string) {
    router.push(role === 'admin' || role === 'deputy' ? '/meeting' : '/pm')
  }

  /** يُعيد الشاشة إلى بدايتها بعد سقوط الرمز (انتهاء أو تجاوز المحاولات). */
  function restartLogin(message: string) {
    setOtpPending(false)
    setOtpCode('')
    setOtpTail(null)
    setOtpError('')
    setLoginError(message)
  }

  async function submitOtp(e?: React.FormEvent) {
    e?.preventDefault()
    if (otpLoading) return
    const code = otpCode.trim()
    if (!isValidCodeFormat(code)) {
      setOtpError(`أدخل رمز التحقق المكوّن من ${OTP_LENGTH} أرقام`)
      return
    }
    setOtpLoading(true)
    setOtpError('')
    try {
      const res = await fetch('/api/auth/otp/verify', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code }),
      })
      const data = await res.json()
      if (!res.ok) {
        if (data.restart) restartLogin(data.error || 'انتهت صلاحية رمز التحقق. أعد تسجيل الدخول.')
        else setOtpError(data.error || 'رمز التحقق غير صحيح')
        return
      }
      toast.success('تم التحقق بنجاح، جارٍ تحويلك…')
      goHome(data.role)
    } catch {
      setOtpError('تعذّر الاتصال بالخادم')
    } finally {
      setOtpLoading(false)
    }
  }

  async function resendOtpCode() {
    if (resendLoading || resendWait > 0) return
    setResendLoading(true)
    setOtpError('')
    try {
      const res = await fetch('/api/auth/otp/resend', { method: 'POST' })
      const data = await res.json()
      if (!res.ok) {
        if (data.restart) restartLogin(data.error || 'تعذّر إرسال رمز جديد. أعد تسجيل الدخول.')
        else {
          setOtpError(data.error || 'تعذّر إرسال رمز جديد')
          if (typeof data.waitSec === 'number') setResendWait(data.waitSec)
        }
        return
      }
      setOtpCode('')
      setOtpTail(data.mobileTail ?? otpTail)
      setOtpSeconds(OTP_SECONDS)
      setResendWait(RESEND_COOLDOWN)
      toast.success('تم إرسال رمز جديد إلى جوالك')
    } catch {
      setOtpError('تعذّر الاتصال بالخادم')
    } finally {
      setResendLoading(false)
    }
  }

  async function handlePasswordLogin(e?: React.FormEvent) {
    e?.preventDefault()
    if (pwLoading) return
    setPwTried(true)
    const errors = validatePasswordLogin(email, password)
    if (errors.length > 0) {
      const firstId = errors[0].fieldId
      requestAnimationFrame(() => document.getElementById(firstId)?.focus())
      return
    }
    setPwLoading(true)
    setPwError('')
    try {
      const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password }),
      })
      const data = await res.json()
      if (!res.ok) {
        setPwError(data.error || 'خطأ في تسجيل الدخول')
        return
      }
      if (data.otpRequired) {
        setPwOpen(false)
        setOtpPending(true)
        setOtpTail(data.mobileTail ?? null)
        setOtpCode('')
        setOtpSeconds(data.expiresInSec ?? OTP_SECONDS)
        setResendWait(RESEND_COOLDOWN)
        return
      }
      goHome(data.role)
    } catch {
      setPwError('تعذّر الاتصال بالخادم')
    } finally {
      setPwLoading(false)
    }
  }

  function closePasswordModal() {
    setPwOpen(false)
    setPwTried(false)
    setForgotView(false)
    setForgotError('')
    setForgotMessage('')
  }

  async function requestPasswordReset(e?: React.FormEvent) {
    e?.preventDefault()
    if (forgotLoading) return
    const value = email.trim()
    if (!isValidEmail(value)) {
      setForgotError('اكتب بريدك الإلكتروني المسجّل — مثال: name@example.com')
      requestAnimationFrame(() => document.getElementById('forgot-email')?.focus())
      return
    }
    setForgotLoading(true)
    setForgotError('')
    setForgotMessage('')
    try {
      const res = await fetch('/api/auth/forgot-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: value }),
        signal: AbortSignal.timeout(RESET_REQUEST_TIMEOUT_MS),
      })
      const data = await res.json()
      if (!res.ok) setForgotError(data.error || 'تعذّر إرسال الطلب')
      else setForgotMessage(data.message)
    } catch (err) {
      setForgotError(err instanceof DOMException && err.name === 'TimeoutError' ? RESET_TIMEOUT_MESSAGE : 'تعذّر الاتصال بالخادم')
    } finally {
      setForgotLoading(false)
    }
  }

  return (
    <>
      <SkipLink />
      <div className="isolate flex min-h-screen flex-col bg-pc-gray-50 lg:flex-row">
        <BrandPattern fixed fade="none" opacity={0.2} />
        {/* ───────────────── لوحة الهوية ─────────────────
            الشعار الكامل على خلفية بيضاء، لأن ألوانه تفقد تباينها فوق الخلفيات الداكنة. شريط علوي بألوان الشعار الثلاثة، وزخرفة
            هندسية من أشكال رمز الشعار نفسه (BrandPattern) تتلاشى خلف المنتصف. */}
        <div className="relative flex flex-col overflow-hidden border-b border-pc-gray-200 bg-white/75 lg:min-h-screen lg:w-[42%] lg:border-b-0 lg:border-e">
          <div aria-hidden="true" className="relative z-10 flex h-1.5 w-full">
            <span className="flex-1 bg-[#003171]" />
            <span className="flex-1 bg-[#004195]" />
            <span className="flex-1 bg-[#F2A93B]" />
          </div>

          <div className="relative flex flex-1 flex-col items-center justify-center gap-pc-m px-pc-l py-pc-l text-center lg:gap-pc-l lg:px-pc-xl lg:py-pc-xl">
            <AppLogo variant="full" size={280} className="w-[10rem] sm:w-[11.875rem] lg:w-[17.5rem]" />

            <div className="flex flex-col items-center">
              <span aria-hidden="true" className="mb-pc-m block h-px w-20 bg-pc-gray-200 lg:w-28" />
              <h1 className="text-pc-h3 leading-tight text-pc-brand-900 lg:text-pc-h2">{LOGIN_TITLE}</h1>
              <p className="mt-pc-s max-w-md text-base text-pc-gray-700 lg:text-pc-lead">
                {SYSTEM_TAGLINE}
              </p>
            </div>

            <ul className="hidden max-w-md flex-col gap-pc-s rounded-pc-md border border-pc-gray-100 bg-white/80 p-pc-m text-start shadow-pc-card lg:flex">
              {BENEFITS.map(item => (
                <li key={item} className="flex items-start gap-pc-s text-base text-pc-gray-800">
                  <CheckCircle2 aria-hidden="true" className="mt-1 h-5 w-5 shrink-0 text-[#003171]" />
                  {item}
                </li>
              ))}
            </ul>
          </div>
        </div>

        {/* ───────────────── بطاقة الدخول ───────────────── */}
        <main id="main-content" className="flex flex-1 items-center justify-center px-pc-m py-pc-xl">
          <div className="w-full max-w-md">
            <section
              aria-labelledby="login-heading"
              className="overflow-hidden rounded-pc-md border border-pc-gray-200 bg-white shadow-pc-float"
            >
              <div className="flex items-center gap-pc-s border-b border-pc-gray-100 bg-pc-brand-25 px-pc-l py-pc-m">
                <AppLogo variant="emblem" size={44} />
              </div>

              <div className="p-pc-l">
                <h2 id="login-heading" className="text-pc-h4 text-pc-gray-900">
                  تسجيل الدخول
                </h2>
                <p className="mt-1 text-base text-pc-gray-600">
                  ادخل ببريدك الإلكتروني وكلمة المرور.
                </p>

                {loginError && (
                  <Alert tone="danger" className="mt-pc-m" onDismiss={() => setLoginError('')}>
                    {loginError}
                  </Alert>
                )}

                {/* ───────────── رمز التحقق ───────────── */}
                {otpPending && (
                  <form className="mt-pc-l flex flex-col gap-pc-m" onSubmit={submitOtp}>
                    <div className="rounded-pc-md border border-pc-brand-600 bg-pc-brand-50 p-pc-m text-center">
                      <p className="flex items-center justify-center gap-2 text-base font-medium text-pc-brand-900">
                        <Smartphone aria-hidden="true" className="h-5 w-5" />
                        أُرسل رمز التحقق إلى جوالك
                      </p>
                      <p className="mt-pc-xs text-base font-bold text-pc-brand-900" dir="ltr">
                        {maskMobile(otpTail)}
                      </p>
                      <p aria-live="polite" className="mt-pc-s text-sm text-pc-gray-700">
                        {otpSeconds > 0
                          ? `صلاحية الرمز تنتهي خلال ${Math.floor(otpSeconds / 60)}:${String(otpSeconds % 60).padStart(2, '0')}`
                          : 'انتهت صلاحية الرمز — اطلب رمزًا جديدًا.'}
                      </p>
                    </div>

                    {otpError && (
                      <Alert tone="danger" onDismiss={() => setOtpError('')}>
                        {otpError}
                      </Alert>
                    )}

                    <Input
                      id="otp-code"
                      label="رمز التحقق"
                      required
                      value={otpCode}
                      onChange={e => setOtpCode(e.target.value.replace(/\D/g, '').slice(0, OTP_LENGTH))}
                      error={otpError ? ' ' : ''}
                      help={`${OTP_LENGTH} أرقام وصلتك في رسالة نصية.`}
                      inputMode="numeric"
                      autoComplete="one-time-code"
                      maxLength={OTP_LENGTH}
                      autoFocus
                      dir="ltr"
                      className="text-center"
                      inputClassName="text-center text-2xl tracking-[0.4em] font-bold"
                    />

                    <Button
                      type="submit"
                      loading={otpLoading}
                      loadingLabel="جارٍ التحقق…"
                      icon={<ShieldCheck aria-hidden="true" className="h-4 w-4" />}
                      block
                    >
                      تأكيد الدخول
                    </Button>

                    <div className="flex flex-wrap items-center justify-between gap-pc-s">
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={resendOtpCode}
                        loading={resendLoading}
                        disabled={resendWait > 0}
                        disabledReason={`انتظر ${resendWait} ثانية قبل طلب رمز جديد`}
                        icon={<RefreshCw aria-hidden="true" className="h-4 w-4" />}
                      >
                        {resendWait > 0 ? `إعادة الإرسال بعد ${resendWait} ثانية` : 'إرسال رمز جديد'}
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => restartLogin('')}
                      >
                        الرجوع لتسجيل الدخول
                      </Button>
                    </div>
                  </form>
                )}

                {!otpPending && (
                  <Button
                    variant="primary"
                    block
                    className="mt-pc-l min-h-[3.25rem] text-base"
                    onClick={() => setPwOpen(true)}
                    icon={<KeyRound aria-hidden="true" className="h-5 w-5" />}
                  >
                    الدخول بالبريد الإلكتروني
                  </Button>
                )}

              </div>
            </section>
          </div>
        </main>
      </div>

      {pwOpen && forgotView && (
        <Modal
          title="استعادة كلمة المرور"
          subtitle="نرسل رابط تأكيد إلى بريدك الإلكتروني المسجّل."
          onClose={closePasswordModal}
          maxWidth={440}
          footer={
            <div className="flex flex-col gap-pc-s sm:flex-row">
              {!forgotMessage && (
                <Button onClick={() => requestPasswordReset()} loading={forgotLoading} loadingLabel="جارٍ الإرسال…" className="flex-1">
                  إرسال رابط التأكيد
                </Button>
              )}
              <Button
                variant={forgotMessage ? 'primary' : 'ghost'}
                onClick={() => { setForgotView(false); setForgotError(''); setForgotMessage(''); setPassword('') }}
                className="flex-1"
              >
                الرجوع لتسجيل الدخول
              </Button>
            </div>
          }
        >
          <form className="flex flex-col gap-pc-m" onSubmit={requestPasswordReset} noValidate>
            {forgotMessage ? (
              <Alert tone="success" title="تم استلام طلبك">{forgotMessage}</Alert>
            ) : (
              <>
                <p className="text-base text-pc-gray-700">
                  يصلك رابط تأكيد على بريدك، وبعد فتحه تصلك كلمة مرور جديدة وتتوقف الحالية عن العمل. يمكنك تغييرها بعد الدخول من «الإعدادات».
                </p>
                <Input
                  id="forgot-email"
                  label="البريد الإلكتروني"
                  type="email"
                  required
                  value={email}
                  onChange={e => { setEmail(e.target.value); setForgotError('') }}
                  placeholder="name@example.com"
                  inputMode="email"
                  autoComplete="email"
                  error={forgotError}
                  autoFocus
                />
              </>
            )}
          </form>
        </Modal>
      )}

      {resetToken && (
        <Modal
          title="تأكيد استعادة كلمة المرور"
          subtitle="الخطوة الأخيرة من طلب «نسيت كلمة المرور»."
          onClose={closeResetModal}
          maxWidth={440}
          footer={
            <div className="flex flex-col gap-pc-s sm:flex-row">
              {!resetMessage && (
                <Button onClick={() => confirmPasswordReset()} loading={resetLoading} loadingLabel="جارٍ الإرسال…" className="flex-1">
                  تأكيد وإرسال كلمة مرور جديدة
                </Button>
              )}
              <Button variant={resetMessage ? 'primary' : 'ghost'} onClick={closeResetModal} className="flex-1">
                {resetMessage ? 'الرجوع لتسجيل الدخول' : 'إلغاء'}
              </Button>
            </div>
          }
        >
          <div className="flex flex-col gap-pc-m">
            {resetMessage ? (
              <Alert tone="success" title="تم تغيير كلمة المرور">{resetMessage}</Alert>
            ) : (
              <>
                {resetError && <Alert tone="danger">{resetError}</Alert>}
                <p className="text-base text-pc-gray-700">
                  عند التأكيد تصلك كلمة مرور جديدة على بريدك المسجّل، وتتوقف كلمة المرور الحالية عن العمل وتُنهى جلساتك المفتوحة.
                </p>
              </>
            )}
          </div>
        </Modal>
      )}

      {pwOpen && !forgotView && (
        <Modal
          title="الدخول بالبريد الإلكتروني"
          subtitle="أدخل بريدك الإلكتروني وكلمة المرور."
          onClose={closePasswordModal}
          maxWidth={440}
          footer={
            <div className="flex flex-col gap-pc-s sm:flex-row">
              <Button
                type="submit"
                form="password-login-form"
                loading={pwLoading}
                loadingLabel="جارٍ التحقق…"
                className="flex-1"
              >
                تسجيل الدخول
              </Button>
              <Button variant="ghost" onClick={closePasswordModal} className="flex-1">
                إلغاء
              </Button>
            </div>
          }
        >
          <form id="password-login-form" className="flex flex-col gap-pc-m" onSubmit={handlePasswordLogin} noValidate>
            <FormRequiredHint />
            <FormErrorSummary errors={pwErrors} autoFocus={false} title="تعذّر تسجيل الدخول، صحّح ما يلي:" />
            {pwError && <Alert tone="danger">{pwError}</Alert>}
            <Input
              id="login-email"
              label="البريد الإلكتروني"
              type="email"
              required
              value={email}
              onChange={e => setEmail(e.target.value)}
              placeholder="name@example.com"
              inputMode="email"
              autoComplete="email"
              error={pwErrors.find(x => x.fieldId === 'login-email')?.message}
            />
            <Input
              id="login-password"
              label="كلمة المرور"
              type="password"
              required
              value={password}
              onChange={e => setPassword(e.target.value)}
              autoComplete="current-password"
              error={pwErrors.find(x => x.fieldId === 'login-password')?.message}
            />
            <button
              type="button"
              onClick={() => { setForgotView(true); setPwError(''); setPwTried(false) }}
              className="self-start rounded-pc-md py-1 text-sm font-medium text-pc-brand-800 underline underline-offset-4 hover:text-pc-brand-900 focus:outline-none focus-visible:ring-2 focus-visible:ring-pc-brand-600"
            >
              نسيت كلمة المرور؟
            </button>
          </form>
        </Modal>
      )}
    </>
  )
}
