'use client'

// Note: هيكل التطبيق الموحّد (AppShell) — قائمة جانبية بدل الترويسات
// العلوية اليدوية الأربع التي كانت مكرَّرة في admin/pm/meeting/settings، بطلب صريح
// من المستخدم («القائمة العلوية تصير جانبية ومرتبة»).
// القواعد المطبَّقة من إرشادات التصميم:
//   • الشريط على **اليمين** (أول عنصر في flex داخل dir="rtl") بخلفية لون العلامة
//     الداكن pc-brand-900 (#0B213D).
//   • الشعار/اسم النظام أعلاه يقود للصفحة الرئيسية لدور المستخدم.
//   • ≤ 7 عناصر مستوى أول (يُقصّ أي زائد برمجيًا)، أيقونة lucide + نص لكل عنصر.
//   • العنصر النشط متمايز بثلاث إشارات لا باللون وحده: خلفية + مؤشر جانبي ملوّن +
//     وزن خط أثقل، مع aria-current="page".
//   • قابل للطي على سطح المكتب (أيقونات فقط) مع aria-expanded وتلميح ظاهر لكل عنصر
//     عند التحويم/التركيز، ويتحوّل إلى درج (drawer) بزر هامبرغر تحت 1024px مع
//     Focus Trap وإغلاق بـ Escape وبالنقر على الخلفية وإعادة التركيز لزر الفتح.
//   • الصلاحيات تنعكس في القائمة: لا يُعرض عنصر لا يملكه الدور. الدور يُقرأ من
//     GET /api/auth/me (للعرض فقط؛ التحقّق الفعلي يبقى في middleware.ts).
//   • رابط التخطي أول عنصر قابل للتركيز ويتخطى الشريط إلى <main id="main-content">.
//   • كل عناصر الهيكل تحمل meeting-no-pdf ولا تقع داخل div.meeting-page، حتى لا
//     تظهر في تقرير PDF لصفحة الاجتماع ولا تغيّر عرض الالتقاط.
import { useCallback, useEffect, useId, useRef, useState, useSyncExternalStore } from 'react'
import Link from 'next/link'
import { usePathname } from 'next/navigation'
import {
  ChevronsLeft,
  ChevronsRight,
  FolderKanban,
  LayoutDashboard,
  Loader2,
  LogOut,
  Menu,
  Presentation,
  Settings,
  X,
  type LucideIcon,
} from 'lucide-react'
import { Breadcrumb, SkipLink } from './Navigation'
import { AppLogo } from './AppLogo'
import { BrandPattern } from './BrandPattern'
import { recordInternalPath } from './navHistory'
import { ORG_NAME, SYSTEM_NAME } from '@/lib/org'
import { USER_ROLE_LABEL } from '@/lib/userRules'

interface SessionUser {
  role: string
  name: string
}

interface ShellNavItem {
  href: string
  label: string
  icon: LucideIcon
}

/** الحدّ الأعلى لعناصر المستوى الأول في إرشادات التصميم */
const MAX_TOP_LEVEL = 7

const FOCUSABLE =
  'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])'

/**
 * عناصر التنقّل حسب الدور. «الإعدادات» و«تسجيل الخروج» في أسفل الشريط لكل دور.
 * admin: الإدارة + لوحة الاجتماع (+ الإعدادات أسفل)
 * pm:    مشاريعي + لوحة الاجتماع (+ الإعدادات أسفل)
 * دور غير معروف بعد (أثناء التحميل): لا شيء — لا نعرض عنصرًا قبل معرفة الصلاحية.
 */
function navItemsFor(role: string | undefined): ShellNavItem[] {
  if (role === 'admin') {
    return [
      { href: '/meeting', label: 'لوحة متابعة المشاريع', icon: Presentation },
      { href: '/admin', label: 'الإدارة', icon: LayoutDashboard },
    ]
  }
  if (role === 'deputy') {
    return [
      { href: '/meeting', label: 'لوحة متابعة المشاريع', icon: Presentation },
      { href: '/admin', label: 'المشاريع والإسناد', icon: FolderKanban },
    ]
  }
  if (role === 'pm') {
    return [
      { href: '/pm', label: 'مشاريعي', icon: FolderKanban },
      { href: '/meeting', label: 'لوحة متابعة المشاريع', icon: Presentation },
    ]
  }
  return []
}

/** الصفحة الرئيسية لكل دور (نفس توجيه ما بعد تسجيل الدخول في app/login/page.tsx). */
export function homeHrefFor(role: string | undefined): string {
  if (role === 'admin') return '/meeting'
  if (role === 'deputy') return '/meeting'
  if (role === 'pm') return '/pm'
  return '/meeting'
}

const roleLabel: Record<string, string> = USER_ROLE_LABEL

function requestUrl(input: RequestInfo | URL): string {
  if (typeof input === 'string') return input
  return input instanceof URL ? input.href : input.url
}

function isActivePath(pathname: string, href: string) {
  return pathname === href || pathname.startsWith(href + '/')
}

/**
 * تفضيل طي القائمة الجانبية يُحفظ في localStorage (كان يعود للوضع الافتراضي بعد
 * كل إعادة تحميل). useSyncExternalStore يقرأ القيمة المحفوظة بعد الترطيب دون عدم تطابق مع الخادم،
 * ويبقى التبديل يعمل في الذاكرة إن كان التخزين محظورًا (نافذة خاصة/إعدادات المتصفح).
 */
const SIDEBAR_PREF_KEY = 'tracker.sidebarCollapsed'
const sidebarPrefListeners = new Set<() => void>()
let sidebarPrefMemory: boolean | null = null

function readSidebarPref(): boolean | null {
  try {
    const stored = window.localStorage.getItem(SIDEBAR_PREF_KEY)
    if (stored === '1' || stored === '0') return stored === '1'
  } catch {
    /* التخزين محظور — نعتمد على الذاكرة */
  }
  return sidebarPrefMemory
}

function writeSidebarPref(collapsed: boolean) {
  sidebarPrefMemory = collapsed
  try {
    window.localStorage.setItem(SIDEBAR_PREF_KEY, collapsed ? '1' : '0')
  } catch {
    /* التخزين محظور — يبقى التفضيل للجلسة الحالية فقط */
  }
  sidebarPrefListeners.forEach(listener => listener())
}

function subscribeSidebarPref(listener: () => void) {
  sidebarPrefListeners.add(listener)
  window.addEventListener('storage', listener)
  return () => {
    sidebarPrefListeners.delete(listener)
    window.removeEventListener('storage', listener)
  }
}

export interface AppShellProps {
  /** عنوان H1 الوحيد للصفحة. */
  title: string
  /** سطر وصفي تحت العنوان (اختياري). */
  subtitle?: React.ReactNode
  /** مسار التنقّل بعد «الرئيسية» (تُضاف «الرئيسية» تلقائيًا في البداية). */
  breadcrumbs?: { label: string; href?: string }[]
  /** إجراءات بجانب العنوان (مثل «تحميل PDF»). */
  actions?: React.ReactNode
  /** يبدأ الشريط مطويًا على سطح المكتب (افتراضي /meeting لوضع العرض). */
  defaultCollapsed?: boolean
  children: React.ReactNode
}

export function AppShell({
  title,
  subtitle,
  breadcrumbs = [],
  actions,
  defaultCollapsed = false,
  children,
}: Readonly<AppShellProps>) {
  const pathname = usePathname() ?? ''
  useEffect(() => {
    if (pathname) recordInternalPath(pathname)
  }, [pathname])
  const [user, setUser] = useState<SessionUser | null>(null)
  const storedCollapsed = useSyncExternalStore(subscribeSidebarPref, readSidebarPref, () => null)
  const collapsed = storedCollapsed ?? defaultCollapsed
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [loggingOut, setLoggingOut] = useState(false)
  const [tipsHidden, setTipsHidden] = useState(false)
  const hamburgerRef = useRef<HTMLButtonElement>(null)
  const navId = useId()

  useEffect(() => {
    if (!collapsed) return
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') setTipsHidden(true)
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [collapsed])

  useEffect(() => {
    const root = document.documentElement
    root.style.setProperty('--app-sidebar-width', collapsed ? '5rem' : '16rem')
    return () => {
      root.style.removeProperty('--app-sidebar-width')
    }
  }, [collapsed])

  useEffect(() => {
    const original = window.fetch
    let redirecting = false
    window.fetch = async (...args: Parameters<typeof fetch>) => {
      const res = await original(...args)
      if (res.status === 401 && !redirecting) {
        const input = args[0]
        const url = requestUrl(input)
        if (/\/api\//.test(url) && !/\/api\/auth\/(login|logout)\b/.test(url)) {
          redirecting = true
          window.location.replace('/login')
        }
      }
      return res
    }
    return () => {
      window.fetch = original
    }
  }, [])

  // قراءة هوية الجلسة للعرض فقط (الدور والاسم).
  useEffect(() => {
    let cancelled = false
    fetch('/api/auth/me', { cache: 'no-store' })
      .then(res => (res.ok ? res.json() : null))
      .then((data: SessionUser | null) => {
        if (!cancelled && data) setUser(data)
      })
      .catch(() => {
        /* فشل شبكة: تبقى القائمة بلا عناصر خاصة بالدور — آمن افتراضيًا. */
      })
    return () => {
      cancelled = true
    }
  }, [])

  const logout = useCallback(() => {
    if (loggingOut) return
    setLoggingOut(true)
    fetch('/api/auth/logout', { method: 'POST' })
      .then(() => window.location.replace('/login'))
      .catch(() => {
        // فشل الشبكة: الكوكيز قد تبقى، لكن proxy.ts يرفض أي جلسة ملغاة — نُخرج المستخدم بصريًا.
        window.location.replace('/login')
      })
  }, [loggingOut])

  const home = user ? homeHrefFor(user.role) : pathname || '/'
  const items = navItemsFor(user?.role).slice(0, MAX_TOP_LEVEL)

  const closeDrawer = useCallback(() => {
    setDrawerOpen(false)
    // إعادة التركيز لزر الهامبرغر الذي فتح الدرج.
    requestAnimationFrame(() => hamburgerRef.current?.focus())
  }, [])

  return (
    <>
      <SkipLink />
      <div dir="rtl" className="isolate min-h-screen bg-pc-gray-50 lg:flex">
        <BrandPattern fixed fade="none" opacity={0.14} className="meeting-no-pdf" />
        {/* ───────────── الشريط الجانبي — سطح المكتب (≥1024px) ───────────── */}
        {/* الحاوية تحجز عرض الشريط داخل التخطيط، بينما الشريط نفسه fixed حتى لا يتحرك
            أو يتوقف عند نهاية محتوى الصفحة أثناء التمرير. */}
        <div
          className={`meeting-no-pdf hidden shrink-0 transition-[width] duration-200 lg:block ${
            collapsed ? 'w-20' : 'w-64'
          }`}
        >
          <div
            data-app-sidebar="desktop"
            className={`fixed inset-y-0 start-0 z-30 flex h-dvh flex-col bg-pc-brand-900 text-white transition-[width] duration-200 ${
              collapsed ? 'w-20' : 'w-64'
            }`}
            onMouseLeave={() => setTipsHidden(false)}
          >
            <SidebarBody
              navId={navId}
              collapsed={collapsed}
              showTips={!tipsHidden}
              onTipTarget={() => setTipsHidden(false)}
              items={items}
              home={home}
              pathname={pathname}
              user={user}
              loggingOut={loggingOut}
              onLogout={logout}
              headerControl={
                <button
                  type="button"
                  onClick={() => writeSidebarPref(!collapsed)}
                  aria-expanded={!collapsed}
                  aria-controls={navId}
                  aria-label={collapsed ? 'توسيع القائمة الجانبية' : 'طي القائمة الجانبية'}
                  title={collapsed ? 'توسيع القائمة الجانبية' : 'طي القائمة الجانبية'}
                  className="flex h-11 w-11 shrink-0 items-center justify-center rounded-pc-md text-pc-brand-50 transition-colors hover:bg-white/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-white"
                >
                  {collapsed ? (
                    <ChevronsLeft aria-hidden="true" className="h-5 w-5" />
                  ) : (
                    <ChevronsRight aria-hidden="true" className="h-5 w-5" />
                  )}
                </button>
              }
            />
          </div>
        </div>

        <div className="flex min-w-0 flex-1 flex-col">
          {/* ───────────── شريط الجوال/اللوحي (<1024px): هامبرغر + الشعار ───────────── */}
          <div className="meeting-no-pdf sticky top-0 z-40 flex min-h-[3.5rem] items-center gap-pc-s bg-pc-brand-900 px-pc-m text-white lg:hidden">
            <button
              ref={hamburgerRef}
              type="button"
              onClick={() => setDrawerOpen(true)}
              aria-expanded={drawerOpen}
              aria-haspopup="dialog"
              aria-label="فتح قائمة التنقّل"
              className="flex h-11 w-11 items-center justify-center rounded-pc-md hover:bg-white/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-white"
            >
              <Menu aria-hidden="true" className="h-6 w-6" />
            </button>
            <Link
              href={home}
              className="flex min-h-[2.75rem] items-center gap-pc-s rounded-pc-md focus:outline-none focus-visible:ring-2 focus-visible:ring-white"
            >
              <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-white">
                <AppLogo variant="emblem" size={26} decorative />
              </span>
              <span className="text-base font-bold">{SYSTEM_NAME}</span>
            </Link>
          </div>

          {/* tabIndex=-1 حتى يستقبل التركيز فعليًا عند استخدام رابط التخطي. */}
          <main id="main-content" tabIndex={-1} className="flex-1 outline-none">
            <div className="mx-auto w-full max-w-[90rem] px-pc-m py-pc-m lg:px-pc-l">
              <div className="meeting-no-pdf mb-pc-m">
                {/* Breadcrumb للصفحات الداخلية فقط؛ الصفحة الرئيسية للدور لا تحتاج مسارًا. */}
                {breadcrumbs.length > 0 && (
                  <Breadcrumb
                    items={[{ label: 'الرئيسية', href: user ? home : undefined }, ...breadcrumbs]}
                    className="mb-pc-s"
                  />
                )}
                <div className="flex flex-wrap items-start justify-between gap-pc-s">
                  <div className="min-w-0">
                    <h1 dir="auto" className="text-start text-pc-h2 text-pc-gray-900 [unicode-bidi:plaintext]">{title}</h1>
                    {subtitle && <div className="mt-pc-xs text-base text-pc-gray-600">{subtitle}</div>}
                  </div>
                  {actions && <div className="flex flex-wrap items-center gap-pc-s">{actions}</div>}
                </div>
              </div>
              {children}
            </div>
          </main>
        </div>
      </div>

      {drawerOpen && (
        <MobileDrawer
          onClose={closeDrawer}
          navId={`${navId}-drawer`}
          items={items}
          home={home}
          pathname={pathname}
          user={user}
          loggingOut={loggingOut}
          onLogout={logout}
        />
      )}
    </>
  )
}

/* ───────────────────────── محتوى الشريط (مشترك) ───────────────────────── */
function SidebarBody({
  navId,
  collapsed,
  items,
  home,
  pathname,
  user,
  loggingOut,
  onLogout,
  onNavigate,
  headerControl,
  showTips = true,
  onTipTarget,
}: Readonly<{
  navId: string
  collapsed: boolean
  items: ShellNavItem[]
  home: string
  pathname: string
  user: SessionUser | null
  loggingOut: boolean
  onLogout: () => void
  onNavigate?: () => void
  headerControl?: React.ReactNode
  /** false بعد Escape — يُخفي تلميحات الوضع المطوي. */
  showTips?: boolean
  /** يُستدعى عند دخول المؤشر/التركيز إلى عنصر جديد لإعادة إظهار التلميحات. */
  onTipTarget?: () => void
}>) {
  const settingsActive = isActivePath(pathname, '/settings')
  const tips = collapsed && showTips

  return (
    <div className="flex h-full flex-col">
      {/* الشعار واسم النظام — يقود للرئيسية */}
      <div
        className={`flex min-h-[4.5rem] items-center gap-pc-s border-b border-white/10 px-pc-m ${
          collapsed ? 'flex-col justify-center py-pc-s' : 'justify-between'
        }`}
      >
        <Link
          href={home}
          onClick={onNavigate}
          aria-label={collapsed ? 'منصة متابعة المشاريع — الصفحة الرئيسية' : undefined}
          onFocus={onTipTarget}
          onPointerEnter={onTipTarget}
          className="group relative flex min-h-[2.75rem] min-w-0 items-center gap-pc-s rounded-pc-md focus:outline-none focus-visible:ring-2 focus-visible:ring-white"
        >
          <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-white">
            <AppLogo variant="emblem" size={30} decorative />
          </span>
          {!collapsed && (
            <span className="flex min-w-0 flex-col leading-tight">
              <span className="text-sm font-bold">{SYSTEM_NAME}</span>
              <span className="text-sm text-pc-brand-100">{ORG_NAME}</span>
            </span>
          )}
          {tips && <CollapsedTooltip label="الصفحة الرئيسية" />}
        </Link>
        {headerControl}
      </div>

      {/* عناصر المستوى الأول */}
      <nav
        id={navId}
        aria-label="التنقّل الرئيسي"
        className={`min-h-0 flex-1 px-pc-s py-pc-m ${collapsed ? 'overflow-visible' : 'overflow-y-auto overscroll-contain'}`}
      >
        {items.length === 0 ? (
          // هيكل تحميل بدل عرض عناصر قبل معرفة الدور.
          <ul aria-hidden="true" className="flex flex-col gap-pc-xs">
            {[0, 1].map(i => (
              <li key={i} className="h-11 animate-pulse rounded-pc-md bg-white/10" />
            ))}
          </ul>
        ) : (
          <ul className="flex flex-col gap-pc-xs">
            {items.map(item => (
              <li key={item.href}>
                <ShellLink
                  href={item.href}
                  label={item.label}
                  icon={item.icon}
                  active={isActivePath(pathname, item.href)}
                  collapsed={collapsed}
                  onNavigate={onNavigate}
                  showTip={tips}
                  onTipTarget={onTipTarget}
                />
              </li>
            ))}
          </ul>
        )}
      </nav>

      {/* أسفل الشريط: المستخدم + الإعدادات + تسجيل الخروج */}
      <div className="border-t border-white/10 px-pc-s py-pc-m">
        {/* هيكل بنفس ارتفاع بطاقة المستخدم أثناء التحميل — لا قفزة تخطيط (CLS). */}
        {!user && (
          <div aria-hidden="true" className={`mb-pc-s flex items-center gap-pc-s px-pc-s ${collapsed ? 'justify-center' : ''}`}>
            <span className="h-9 w-9 shrink-0 animate-pulse rounded-full bg-white/10" />
            {!collapsed && <span className="h-4 flex-1 animate-pulse rounded-pc-md bg-white/10" />}
          </div>
        )}
        {user && (
          <div
            className={`mb-pc-s flex items-center gap-pc-s px-pc-s ${collapsed ? 'justify-center' : ''}`}
            title={collapsed ? `${user.name} — ${roleLabel[user.role] ?? user.role}` : undefined}
          >
            <span
              aria-hidden="true"
              className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-pc-brand-700 text-sm font-bold text-white"
            >
              {user.name.trim().charAt(0) || '؟'}
            </span>
            <span className={collapsed ? 'sr-only' : 'flex min-w-0 flex-col leading-tight'}>
              <span className="truncate text-sm font-semibold">{user.name}</span>
              <span className="text-sm text-pc-brand-100">{roleLabel[user.role] ?? user.role}</span>
            </span>
          </div>
        )}
        <ul className="flex flex-col gap-pc-xs">
          <li>
            <ShellLink
              href="/settings"
              label="الإعدادات"
              icon={Settings}
              active={settingsActive}
              collapsed={collapsed}
              onNavigate={onNavigate}
              showTip={tips}
              onTipTarget={onTipTarget}
            />
          </li>
          <li>
            <button
              type="button"
              onClick={onLogout}
              onFocus={onTipTarget}
              onPointerEnter={onTipTarget}
              disabled={loggingOut}
              aria-busy={loggingOut || undefined}
              className={`group relative flex min-h-[2.75rem] w-full items-center gap-pc-s rounded-pc-md px-pc-s text-sm font-medium text-pc-brand-50 transition-colors hover:bg-white/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-white disabled:cursor-not-allowed disabled:opacity-40 ${
                collapsed ? 'justify-center' : ''
              }`}
            >
              {loggingOut ? (
                <Loader2 aria-hidden="true" className="h-5 w-5 shrink-0 animate-spin" />
              ) : (
                <LogOut aria-hidden="true" className="h-5 w-5 shrink-0" />
              )}
              <span className={collapsed ? 'sr-only' : ''}>
                {loggingOut ? 'جارٍ تسجيل الخروج…' : 'تسجيل الخروج'}
              </span>
              {tips && <CollapsedTooltip label="تسجيل الخروج" />}
            </button>
          </li>
        </ul>
      </div>
    </div>
  )
}

/** رابط تنقّل واحد: أيقونة + نص، والنشط بخلفية + مؤشر جانبي + aria-current. */
function ShellLink({
  href,
  label,
  icon: Icon,
  active,
  collapsed,
  onNavigate,
  showTip,
  onTipTarget,
}: Readonly<{
  href: string
  label: string
  icon: LucideIcon
  active: boolean
  collapsed: boolean
  onNavigate?: () => void
  showTip?: boolean
  onTipTarget?: () => void
}>) {
  return (
    <Link
      href={href}
      onClick={onNavigate}
      onFocus={onTipTarget}
      onPointerEnter={onTipTarget}
      aria-current={active ? 'page' : undefined}
      className={`group relative flex min-h-[2.75rem] items-center gap-pc-s rounded-pc-md px-pc-s text-sm transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-white ${
        collapsed ? 'justify-center' : ''
      } ${active ? 'bg-white/15 font-bold text-white' : 'font-medium text-pc-brand-50 hover:bg-white/10'}`}
    >
      {/* المؤشر الجانبي للعنصر النشط — على حافة البداية (يمين في RTL). */}
      {active && (
        <span
          aria-hidden="true"
          className="absolute inset-y-2 start-0 w-1 rounded-full bg-pc-accent-200"
        />
      )}
      <Icon aria-hidden="true" className="h-5 w-5 shrink-0" />
      <span className={collapsed ? 'sr-only' : ''}>{label}</span>
      {collapsed && showTip !== false && <CollapsedTooltip label={label} />}
    </Link>
  )
}

/** تلميح ظاهر عند التحويم/التركيز على عنصر مطوي (يسار الشريط في RTL).
 *  start لا end — في RTL تكون inset-inline-start هي right، و
 *  right: calc(100%+8px) تضع التلميح يسار العنصر داخل منطقة المحتوى؛ end كانت تدفعه
 *  خارج حافة الشاشة اليمنى فلا يُرى. */
function CollapsedTooltip({ label }: Readonly<{ label: string }>) {
  return (
    <span
      aria-hidden="true"
      className="pointer-events-none absolute start-[calc(100%+0.5rem)] top-1/2 z-50 hidden -translate-y-1/2 whitespace-nowrap rounded-pc-md bg-pc-gray-900 px-pc-s py-pc-xs text-sm font-medium text-white shadow-pc-float group-hover:block group-focus-visible:block"
    >
      {label}
    </span>
  )
}

/* ───────────────────────── الدرج على الجوال/اللوحي ───────────────────────── */
function MobileDrawer({
  onClose,
  navId,
  items,
  home,
  pathname,
  user,
  loggingOut,
  onLogout,
}: Readonly<{
  onClose: () => void
  navId: string
  items: ShellNavItem[]
  home: string
  pathname: string
  user: SessionUser | null
  loggingOut: boolean
  onLogout: () => void
}>) {
  const panelRef = useRef<HTMLDialogElement>(null)

  // منع تمرير الخلفية + تركيز أول عنصر + إغلاق تلقائي عند الاتساع إلى ≥1024px.
  useEffect(() => {
    const prev = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    panelRef.current?.querySelector<HTMLElement>(FOCUSABLE)?.focus()
    const mq = window.matchMedia('(min-width: 1024px)')
    const onChange = (e: MediaQueryListEvent) => {
      if (e.matches) onClose()
    }
    mq.addEventListener('change', onChange)
    return () => {
      document.body.style.overflow = prev
      mq.removeEventListener('change', onChange)
    }
  }, [onClose])

  function onKeyDown(e: React.KeyboardEvent) {
    if (e.key === 'Escape') {
      e.stopPropagation()
      onClose()
      return
    }
    if (e.key !== 'Tab') return
    const nodes = Array.from(panelRef.current?.querySelectorAll<HTMLElement>(FOCUSABLE) ?? [])
    if (nodes.length === 0) return
    const first = nodes[0]
    const last = nodes.at(-1)!
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault()
      last.focus()
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault()
      first.focus()
    }
  }

  return (
    // position:fixed لذلك يُكرَّر dir="rtl".
    <div dir="rtl" className="meeting-no-pdf fixed inset-0 z-[90] bg-black/50 lg:hidden">
      <div aria-hidden="true" className="absolute inset-0" onMouseDown={onClose} />
      <dialog
        open
        ref={panelRef}
        onKeyDown={onKeyDown}
        aria-modal="true"
        aria-label="قائمة التنقّل"
        className="absolute inset-y-0 start-0 m-0 flex h-full w-72 border-0 p-0 max-w-[85vw] flex-col bg-pc-brand-900 text-white shadow-pc-modal"
      >
        <SidebarBody
          navId={navId}
          collapsed={false}
          items={items}
          home={home}
          pathname={pathname}
          user={user}
          loggingOut={loggingOut}
          onLogout={onLogout}
          onNavigate={onClose}
          headerControl={
            <button
              type="button"
              onClick={onClose}
              aria-label="إغلاق قائمة التنقّل"
              className="flex h-11 w-11 shrink-0 items-center justify-center rounded-pc-md hover:bg-white/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-white"
            >
              <X aria-hidden="true" className="h-5 w-5" />
            </button>
          }
        />
      </dialog>
    </div>
  )
}
