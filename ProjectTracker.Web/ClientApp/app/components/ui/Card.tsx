'use client'

import Link from 'next/link'

interface CardProps {
  title?: string
  /** مستوى عنوان البطاقة ضمن تسلسل الصفحة — لا تخطِّ المستويات. */
  titleAs?: 'h2' | 'h3' | 'h4'
  subtitle?: string
  /** إجراء في رأس البطاقة (زر/رابط) — لا يُستخدم مع بطاقة قابلة للنقر بالكامل. */
  action?: React.ReactNode
  children?: React.ReactNode
  className?: string
  bodyClassName?: string
  /** حشوة داخلية افتراضية لجسم البطاقة (false للبطاقات ذات التخطيط الداخلي الخاص). */
  padded?: boolean
  /** تخطيط وظيفي فقط (gridColumn/sticky…) — لا ألوان ولا مسافات هنا. */
  style?: React.CSSProperties
  /** الوسم الدلالي للبطاقة غير التفاعلية. */
  as?: 'div' | 'section' | 'article'
  /** يجعل كامل البطاقة قابلة للنقر (إجراء واحد فقط). */
  onClick?: () => void
  href?: string
  /** اسم الوصول عند كون البطاقة كلها قابلة للنقر. */
  ariaLabel?: string
  selected?: boolean
}

const shell =
  'rounded-pc-md border border-pc-gray-200 bg-white shadow-pc-card transition-[box-shadow,border-color,transform] duration-200 hover:border-pc-brand-100 hover:shadow-pc-float'

export function Card({
  title,
  titleAs: Heading = 'h3',
  subtitle,
  action,
  children,
  className = '',
  bodyClassName = '',
  padded = true,
  style,
  as: Tag = 'div',
  onClick,
  href,
  ariaLabel,
  selected = false,
}: Readonly<CardProps>) {
  const body = padded ? `p-pc-m ${bodyClassName}` : bodyClassName
  const inner = (
    <>
      {(title || action) && (
        <div className="flex items-start justify-between gap-pc-s border-b border-pc-gray-100 px-pc-m py-pc-s">
          <div className="min-w-0">
            {title && (
              <Heading className="truncate text-base font-semibold text-pc-gray-900">
                {title}
              </Heading>
            )}
            {subtitle && <p className="mt-1 text-sm text-pc-gray-600">{subtitle}</p>}
          </div>
          {action && <div className="shrink-0">{action}</div>}
        </div>
      )}
      {children !== undefined && children !== null && (body ? <div className={body}>{children}</div> : children)}
    </>
  )

  const interactive =
    'text-start w-full hover:-translate-y-0.5 hover:shadow-pc-float focus:outline-none focus-visible:ring-2 focus-visible:ring-pc-brand-600 focus-visible:ring-offset-2 active:translate-y-0'
  const selectedCls = selected ? 'border-pc-brand-600 ring-1 ring-pc-brand-600' : ''

  if (href) {
    return (
      <Link
        href={href}
        aria-label={ariaLabel}
        style={style}
        className={`block ${shell} ${interactive} ${selectedCls} ${className}`}
      >
        {inner}
      </Link>
    )
  }

  if (onClick) {
    return (
      <button
        type="button"
        onClick={onClick}
        aria-label={ariaLabel}
        aria-pressed={selected || undefined}
        style={style}
        className={`${shell} ${interactive} ${selectedCls} ${className}`}
      >
        {inner}
      </button>
    )
  }

  return (
    <Tag style={style} className={`${shell} ${selectedCls} ${className}`}>
      {inner}
    </Tag>
  )
}
