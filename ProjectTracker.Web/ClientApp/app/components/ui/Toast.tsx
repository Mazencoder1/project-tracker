'use client'

import { createContext, useCallback, useContext, useMemo, useState } from 'react'
import { AlertTriangle, CheckCircle2, Info, OctagonAlert, X } from 'lucide-react'

export type ToastTone = 'success' | 'error' | 'warning' | 'info'

interface ToastItem {
  id: number
  tone: ToastTone
  message: string
}

interface ToastApi {
  /** رسالة نجاح — تختفي تلقائيًا بعد 5 ثوانٍ. */
  success: (message: string) => void
  /** رسالة خطأ — تبقى حتى يغلقها المستخدم (قاعدة إرشادات التصميم). */
  error: (message: string) => void
  warning: (message: string) => void
  info: (message: string) => void
}

const ToastContext = createContext<ToastApi | null>(null)

const SUCCESS_MS = 5000
let nextToastId = 0

const tones: Record<ToastTone, { cls: string; Icon: typeof Info; sr: string }> = {
  success: {
    cls: 'border-pc-success-600 bg-pc-success-50 text-pc-success-800',
    Icon: CheckCircle2,
    sr: 'نجاح:',
  },
  error: {
    cls: 'border-pc-error-600 bg-pc-error-50 text-pc-error-800',
    Icon: OctagonAlert,
    sr: 'خطأ:',
  },
  warning: {
    cls: 'border-pc-warning-600 bg-pc-warning-50 text-pc-warning-800',
    Icon: AlertTriangle,
    sr: 'تنبيه:',
  },
  info: {
    cls: 'border-pc-info-600 bg-pc-info-50 text-pc-info-800',
    Icon: Info,
    sr: 'معلومة:',
  },
}

export function ToastProvider({ children }: Readonly<{ children: React.ReactNode }>) {
  const [items, setItems] = useState<ToastItem[]>([])

  const remove = useCallback((id: number) => {
    setItems(list => list.filter(t => t.id !== id))
  }, [])

  const push = useCallback(
    (tone: ToastTone, message: string) => {
      const id = ++nextToastId
      setItems(list => (list.some(t => t.tone === tone && t.message === message) ? list : [...list, { id, tone, message }]))
      // الخطأ لا يختفي تلقائيًا — قاعدة صريحة في الإرشادات التصميم
      if (tone !== 'error') {
        setTimeout(() => remove(id), SUCCESS_MS)
      }
    },
    [remove]
  )

  const api = useMemo<ToastApi>(
    () => ({
      success: m => push('success', m),
      error: m => push('error', m),
      warning: m => push('warning', m),
      info: m => push('info', m),
    }),
    [push]
  )

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div
        dir="rtl"
        className="pointer-events-none fixed bottom-4 start-4 z-[200] flex w-[calc(100vw-2rem)] max-w-sm flex-col gap-pc-s lg:start-[calc(var(--app-sidebar-width,0px)+1rem)]"
      >
        <div aria-live="polite" aria-atomic="false" className="contents">
          {items
            .filter(t => t.tone !== 'error')
            .map(t => (
              <ToastRow key={t.id} item={t} onClose={() => remove(t.id)} />
            ))}
        </div>
        <div aria-live="assertive" aria-atomic="false" className="contents">
          {items
            .filter(t => t.tone === 'error')
            .map(t => (
              <ToastRow key={t.id} item={t} onClose={() => remove(t.id)} />
            ))}
        </div>
      </div>
    </ToastContext.Provider>
  )
}

function ToastRow({ item, onClose }: Readonly<{ item: ToastItem; onClose: () => void }>) {
  const { cls, Icon, sr } = tones[item.tone]
  return (
    <div
      className={`pointer-events-auto flex items-start gap-pc-s rounded-pc-md border p-pc-m shadow-pc-float ${cls}`}
    >
      <Icon aria-hidden="true" className="h-5 w-5 shrink-0" />
      <p className="min-w-0 flex-1 text-sm font-medium">
        <span className="sr-only">{sr}</span>
        {item.message}
      </p>
      <button
        type="button"
        onClick={onClose}
        aria-label="إغلاق الرسالة"
        className="-m-pc-xs flex h-11 w-11 shrink-0 items-center justify-center rounded-pc-md hover:bg-black/5 focus:outline-none focus-visible:ring-2 focus-visible:ring-current"
      >
        <X aria-hidden="true" className="h-4 w-4" />
      </button>
    </div>
  )
}

/**
 * الوصول إلى Toast من أي مكوّن داخل ToastProvider.
 * إن لم يوجد Provider (مكوّن خارج الشجرة) يرجع واجهة صامتة بدل الانهيار،
 * ويُسجَّل تحذير في console للمطوّر فقط.
 */
export function useToast(): ToastApi {
  const ctx = useContext(ToastContext)
  return useMemo<ToastApi>(() => {
    if (ctx) return ctx
    const noop = (m: string) => {
      if (process.env.NODE_ENV !== 'production') {
        console.warn('[Toast] لا يوجد <ToastProvider> في الشجرة. الرسالة:', m)
      }
    }
    return { success: noop, error: noop, warning: noop, info: noop }
  }, [ctx])
}
