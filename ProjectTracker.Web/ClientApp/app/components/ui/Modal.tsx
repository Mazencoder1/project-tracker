'use client'

import { useCallback, useEffect, useId, useRef } from 'react'
import { AlertTriangle, X } from 'lucide-react'
import { Button, IconButton } from './Button'

const FOCUSABLE =
  'a[href],button:not([disabled]),textarea:not([disabled]),input:not([disabled]),select:not([disabled]),[tabindex]:not([tabindex="-1"])'

/**
 * مكدّس النوافذ المفتوحة — أعلى نافذة فقط تستجيب لمفاتيح المستند (راجع
 * مستمع keydown على document أدناه) حتى لا يُغلق Escape نافذتين متراكبتين معًا.
 */
const openModals: symbol[] = []

interface ModalProps {
  title: string
  subtitle?: string
  onClose: () => void
  /** العرض الأقصى بالبكسل (يبقى متجاوبًا: w-full تحت هذا الحد). */
  maxWidth?: number
  children: React.ReactNode
  /** التذييل الافتراضي: زرا الحفظ/الإلغاء. مرّر footer لتذييل مخصّص بالكامل. */
  onSave?: () => void
  saveLabel?: string
  cancelLabel?: string
  saveDisabled?: boolean
  saveLoading?: boolean
  saveVariant?: 'primary' | 'danger'
  footer?: React.ReactNode
}

export function Modal({
  title,
  subtitle,
  onClose,
  maxWidth = 420,
  children,
  onSave,
  saveLabel = 'حفظ',
  cancelLabel = 'إلغاء',
  saveDisabled = false,
  saveLoading = false,
  saveVariant = 'primary',
  footer,
}: Readonly<ModalProps>) {
  const panelRef = useRef<HTMLDialogElement>(null)
  const openerRef = useRef<HTMLElement | null>(null)
  const onCloseRef = useRef(onClose)
  const uid = useId()
  const titleId = `modal-title-${uid}`
  const subId = subtitle ? `modal-sub-${uid}` : undefined

  useEffect(() => {
    onCloseRef.current = onClose
  }, [onClose])

  // حفظ العنصر الذي فتح النافذة، ثم إعادة التركيز إليه عند الإغلاق.
  useEffect(() => {
    openerRef.current = document.activeElement as HTMLElement | null
    const prevOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    // نقل التركيز إلى أول عنصر قابل للتركيز داخل النافذة.
    const first = panelRef.current?.querySelector<HTMLElement>(FOCUSABLE)
    ;(first ?? panelRef.current)?.focus()
    const opener = openerRef.current
    return () => {
      document.body.style.overflow = prevOverflow
      opener?.focus?.()
      requestAnimationFrame(() => {
        if (opener && document.activeElement === document.body && opener.isConnected) opener.focus()
      })
    }
  }, [])

  useEffect(() => {
    const token = Symbol('modal')
    openModals.push(token)
    function onDocumentKeyDown(e: KeyboardEvent) {
      if (openModals.at(-1) !== token || e.defaultPrevented) return
      const panel = panelRef.current
      if (!panel || panel.contains(document.activeElement)) return
      if (e.key === 'Escape') {
        e.preventDefault()
        onCloseRef.current()
      } else if (e.key === 'Tab') {
        e.preventDefault()
        ;(panel.querySelector<HTMLElement>(FOCUSABLE) ?? panel).focus()
      }
    }
    document.addEventListener('keydown', onDocumentKeyDown)
    return () => {
      document.removeEventListener('keydown', onDocumentKeyDown)
      const index = openModals.indexOf(token)
      if (index >= 0) openModals.splice(index, 1)
    }
  }, [])

  // Escape للإغلاق + حبس التركيز داخل النافذة (Focus Trap).
  const onKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.stopPropagation()
        onClose()
        return
      }
      if (e.key !== 'Tab') return
      const nodes = Array.from(
        panelRef.current?.querySelectorAll<HTMLElement>(FOCUSABLE) ?? []
      ).filter(n => n.offsetParent !== null)
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
    },
    [onClose]
  )

  return (
    // الحاوية position:fixed لذلك تكرّر dir="rtl".
    <div dir="rtl" className="fixed inset-0 z-[100] flex items-center justify-center bg-black/50 p-4">
      {/* إغلاق بالنقر خارج النافذة (طبقة زخرفية؛ الإغلاق بلوحة المفاتيح عبر Escape وزر الإغلاق). */}
      <div
        aria-hidden="true"
        className="absolute inset-0"
        onMouseDown={e => {
          e.preventDefault()
          onClose()
        }}
      />
      <dialog
        open
        ref={panelRef}
        onKeyDown={onKeyDown}
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={subId}
        tabIndex={-1}
        className="relative m-0 flex max-h-[85vh] w-full flex-col overflow-hidden rounded-pc-md border-0 bg-white p-0 text-inherit shadow-pc-modal outline-none"
        style={{ maxWidth: `${maxWidth / 16}rem` }}
      >
        <div className="flex items-start justify-between gap-pc-s border-b border-pc-gray-200 px-pc-m pt-pc-m pb-pc-s">
          <div className="min-w-0">
            <h2 id={titleId} className="text-base font-semibold text-pc-gray-900">
              {title}
            </h2>
            {subtitle && (
              <p id={subId} className="mt-pc-xs text-sm text-pc-gray-600">
                {subtitle}
              </p>
            )}
          </div>
          <IconButton label="إغلاق النافذة" onClick={onClose} className="-me-2 -mt-2 shrink-0">
            <X aria-hidden="true" className="h-5 w-5" />
          </IconButton>
        </div>

        <div className="overflow-y-auto px-pc-m py-pc-m">{children}</div>

        <div className="border-t border-pc-gray-200 px-pc-m pt-pc-s pb-pc-m">
          {footer ?? (
            <div className="flex flex-col gap-pc-s sm:flex-row">
              <Button
                variant={saveVariant}
                onClick={onSave}
                disabled={saveDisabled}
                loading={saveLoading}
                className="flex-1"
              >
                {saveLabel}
              </Button>
              <Button variant="ghost" onClick={onClose} className="flex-1">
                {cancelLabel}
              </Button>
            </div>
          )}
        </div>
      </dialog>
    </div>
  )
}

/**
 * نافذة تأكيد الإجراءات المدمّرة — البديل الموحّد لـ confirm() الأصلية.
 * الإرشادات التصميم: «الإجراءات المدمّرة تتطلب نصًا صريحًا بعدم إمكانية التراجع وزرًا
 * يحمل صياغة الإجراء نفسه لا "موافق"».
 */
export function ConfirmModal({
  title,
  message,
  confirmLabel,
  cancelLabel = 'إلغاء',
  destructive = true,
  irreversibleNote = 'لا يمكن التراجع عن هذا الإجراء بعد تنفيذه.',
  loading = false,
  onConfirm,
  onClose,
}: Readonly<{
  title: string
  message: string
  /** صياغة الإجراء نفسه: «حذف المشروع» / «إيقاف الموظف» — ممنوع «موافق». */
  confirmLabel: string
  cancelLabel?: string
  destructive?: boolean
  /** يُمرَّر '' لإخفاء ملاحظة عدم إمكانية التراجع في الإجراءات غير المدمّرة. */
  irreversibleNote?: string
  loading?: boolean
  onConfirm: () => void
  onClose: () => void
}>) {
  return (
    <Modal
      title={title}
      onClose={onClose}
      maxWidth={460}
      onSave={onConfirm}
      saveLabel={confirmLabel}
      cancelLabel={cancelLabel}
      saveLoading={loading}
      saveVariant={destructive ? 'danger' : 'primary'}
    >
      <div className="flex items-start gap-pc-s">
        {destructive && (
          <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-pc-error-100 text-pc-error-600">
            <AlertTriangle aria-hidden="true" className="h-5 w-5" />
          </span>
        )}
        <div className="min-w-0">
          <p className="text-base text-pc-gray-900">{message}</p>
          {destructive && irreversibleNote && (
            <p className="mt-pc-s text-sm font-medium text-pc-error-800">{irreversibleNote}</p>
          )}
        </div>
      </div>
    </Modal>
  )
}
