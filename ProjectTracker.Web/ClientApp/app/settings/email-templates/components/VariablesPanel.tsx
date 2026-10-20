'use client'

import { useState } from 'react'
import { Check, Copy } from 'lucide-react'
import { Card } from '@/app/components/ui'
import type { EmailVariable } from '../types'

/** نسخ إلى الحافظة؛ navigator.clipboard غير متاح على http داخل الشبكة الداخلية فنستعمل الطريقة القديمة. */
async function copyText(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(text)
      return true
    }
  } catch {
    // ننتقل للطريقة البديلة
  }
  const area = document.createElement('textarea')
  area.value = text
  area.setAttribute('readonly', '')
  area.style.position = 'fixed'
  area.style.opacity = '0'
  document.body.appendChild(area)
  area.select()
  const ok = document.execCommand('copy') // NOSONAR — بديل للمتصفحات/السياقات بلا navigator.clipboard (http داخلي)
  area.remove()
  return ok
}

export function VariablesPanel({ variables }: Readonly<{ variables: EmailVariable[] }>) {
  const [copied, setCopied] = useState('')

  async function copy(name: string) {
    if (await copyText(`{{${name}}}`)) {
      setCopied(name)
      window.setTimeout(() => setCopied(current => (current === name ? '' : current)), 1500)
    }
  }

  return (
    <Card
      as="section"
      title="المتغيرات المتاحة"
      titleAs="h2"
      subtitle="انسخ المتغير والصقه في العنوان أو المحتوى. يُستبدل بقيمته الفعلية عند الإرسال، ويُرفض أي متغير غير مذكور هنا."
      padded={false}
    >
      <ul className="divide-y divide-pc-gray-100">
        {variables.map(variable => (
          <li key={variable.name} className="flex items-center justify-between gap-pc-s px-pc-m py-pc-s">
            <div className="min-w-0">
              <code dir="ltr" className="block truncate text-sm font-semibold text-pc-gray-900">{`{{${variable.name}}}`}</code>
              <span className="text-sm text-pc-gray-600">
                {variable.label}
                {variable.isHtml && ' — للمحتوى فقط'}
              </span>
            </div>
            <button
              type="button"
              onClick={() => copy(variable.name)}
              aria-label={`نسخ المتغير ${variable.name}`}
              className="inline-flex h-10 shrink-0 items-center gap-1 rounded-pc-md border border-pc-gray-300 px-2 text-sm font-medium text-pc-gray-800 hover:bg-pc-gray-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-pc-brand-600"
            >
              {copied === variable.name ? (
                <>
                  <Check aria-hidden="true" className="h-4 w-4 text-pc-success-600" /> تم النسخ
                </>
              ) : (
                <>
                  <Copy aria-hidden="true" className="h-4 w-4" /> نسخ
                </>
              )}
            </button>
          </li>
        ))}
      </ul>
    </Card>
  )
}
