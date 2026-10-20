import Link from 'next/link'
import { buttonClassName, type ButtonSize, type ButtonVariant } from './buttonStyles'

type ButtonLinkProps = Omit<React.ComponentProps<typeof Link>, 'className' | 'children'> & {
  variant?: ButtonVariant
  size?: ButtonSize
  /** أيقونة قبل النص (مثل سهم الرجوع). */
  icon?: React.ReactNode
  /** أيقونة بعد النص (مثل سهم «عرض التفاصيل»). */
  iconEnd?: React.ReactNode
  block?: boolean
  className?: string
  children: React.ReactNode
}

export function ButtonLink({
  variant = 'secondary',
  size = 'md',
  icon,
  iconEnd,
  block = false,
  className = '',
  children,
  ...rest
}: ButtonLinkProps) {
  return (
    <Link {...rest} className={buttonClassName({ variant, size, block, className })}>
      {icon}
      <span>{children}</span>
      {iconEnd}
    </Link>
  )
}
