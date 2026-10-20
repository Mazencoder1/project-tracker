'use client'

import { Button, Modal } from '@/app/components/ui'
import type { EmailPreview } from '../types'

export function PreviewModal({ preview, onClose }: Readonly<{ preview: EmailPreview; onClose: () => void }>) {
  const srcDoc = `<!doctype html><html dir="rtl" lang="ar"><head><meta charset="utf-8"></head><body style="margin:0;padding:16px;background:#f5f5f5">${preview.bodyHtml}</body></html>`
  return (
    <Modal
      title="معاينة الرسالة"
      subtitle="بيانات تجريبية فقط — لا يتأثر أي مشروع."
      onClose={onClose}
      maxWidth={760}
      footer={<Button variant="secondary" onClick={onClose}>إغلاق</Button>}
    >
      <div className="flex flex-col gap-pc-s">
        <p className="rounded-pc-md bg-pc-gray-50 px-pc-s py-pc-xs text-sm text-pc-gray-800">
          <span className="font-semibold">العنوان: </span>
          {preview.subject}
        </p>
        <iframe
          title="معاينة محتوى الرسالة"
          sandbox=""
          srcDoc={srcDoc}
          className="h-[60vh] w-full rounded-pc-md border border-pc-gray-300 bg-white"
        />
      </div>
    </Modal>
  )
}
