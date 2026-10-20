
export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'danger-outline'
export type ButtonSize = 'sm' | 'md'

const base =
  'inline-flex items-center justify-center gap-2 rounded-pc-md font-medium ' +
  'transition-[color,background-color,border-color,box-shadow,transform] duration-200 select-none active:translate-y-0 ' +
  'focus:outline-none focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 ' +
  'disabled:cursor-not-allowed disabled:opacity-40'

const sizes: Record<ButtonSize, string> = {
  // ≥44px على الجوال ثم ≥40px من sm فأعلى (نقطة كسر الجوال في إرشادات التصميم 768px،
  // لكن sm=640px أقرب المتاح في Tailwind ويبقى ضمن الشرط لأنه يرفع الحدّ الأدنى فقط).
  sm: 'min-h-[2.75rem] sm:min-h-[2.5rem] px-3 text-sm',
  md: 'min-h-[2.75rem] sm:min-h-[2.5rem] px-4 text-sm',
}

const variants: Record<ButtonVariant, string> = {
  // أزرق أساسي #143A6C (pc-brand-600) — نص أبيض، تباين 7.5:1.
  primary:
    'bg-pc-brand-600 text-white shadow-pc-card hover:-translate-y-px hover:bg-pc-brand-700 hover:shadow-pc-float active:bg-pc-brand-800 focus-visible:ring-pc-brand-700',
  // ثانوي: حدّ بلون متباين ≥3:1 (pc-brand-600) لا رمادي فاتح.
  secondary:
    'bg-white text-pc-brand-700 border border-pc-brand-600 shadow-pc-card hover:-translate-y-px hover:bg-pc-brand-50 hover:shadow-pc-float active:bg-pc-brand-100 focus-visible:ring-pc-brand-700',
  ghost:
    'bg-transparent text-pc-gray-700 hover:bg-pc-gray-100 active:bg-pc-gray-200 focus-visible:ring-pc-gray-600',
  // أحمر الخطأ #9B2226 (pc-error-600) — للإجراءات المدمّرة فقط.
  danger:
    'bg-pc-error-600 text-white hover:bg-pc-error-800 active:bg-pc-error-800 focus-visible:ring-pc-error-800',
  // حذف داخل القوائم والصفوف: إطار أحمر بدل تعبئة كاملة حتى لا يطغى على المحتوى المتكرر
  // (النمط الذي كان مكتوبًا يدويًا لزر «إزالة» في نافذة الإسناد ولزري الحذف في /pm).
  'danger-outline':
    'bg-white text-pc-error-800 border border-pc-error-600 hover:bg-pc-error-50 active:bg-pc-error-100 focus-visible:ring-pc-error-600',
}

export function buttonClassName({
  variant = 'primary',
  size = 'md',
  block = false,
  className = '',
}: {
  variant?: ButtonVariant
  size?: ButtonSize
  block?: boolean
  className?: string
} = {}): string {
  return [base, sizes[size], variants[variant], block ? 'w-full' : '', className].filter(Boolean).join(' ')
}
