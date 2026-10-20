import Image from 'next/image'
import { SYSTEM_NAME } from '@/lib/org'

// Note: شعار النظام بنسختين — استبدل الملفين في public/brand لوضع شعار جهتك:
//   • full   — الرمز مع اسم النظام بالإنجليزية (public/brand/logo.svg). للمساحات الواسعة على خلفية فاتحة، مثل شاشة الدخول.
//   • emblem — الرمز فقط (public/brand/emblem.svg). للمساحات الصغيرة: الشريط الجانبي وبطاقة الدخول وترويسة PDF.
// يُوضع الرمز داخل دائرة بيضاء في الشريط الجانبي وترويسة PDF ليبقى واضحًا فوق الخلفيات الداكنة.
export const APP_LOGO_SRC = '/brand/logo.svg'
export const APP_EMBLEM_SRC = '/brand/emblem.svg'

const VARIANTS = {
  // النسبة = الارتفاع ÷ العرض حسب viewBox كل ملف.
  full: { src: APP_LOGO_SRC, ratio: 210 / 280 },
  emblem: { src: APP_EMBLEM_SRC, ratio: 1 },
} as const

export function AppLogo({
  variant = 'emblem',
  size = 40,
  className = '',
  decorative = false,
}: Readonly<{
  variant?: keyof typeof VARIANTS
  /** العرض بالبكسل؛ الارتفاع يُحسب من نسبة الملف. */
  size?: number
  className?: string
  /** true عندما يجاوره اسم النظام نصًا، حتى لا يُقرأ الاسم مرتين. */
  decorative?: boolean
}>) {
  const { src, ratio } = VARIANTS[variant]
  return (
    <Image
      src={src}
      alt={decorative ? '' : `شعار ${SYSTEM_NAME}`}
      aria-hidden={decorative || undefined}
      width={size}
      height={Math.round(size * ratio)}
      unoptimized
      priority
      // Note: العرض بالـ rem ليكبر الشعار مع الواجهة على الشاشات الكبيرة، إلا إن حدّد className عرضًا.
      style={/(^|\s|:)w-/.test(className) ? undefined : { width: `${size / 16}rem` }}
      className={`h-auto shrink-0 ${className}`}
    />
  )
}
