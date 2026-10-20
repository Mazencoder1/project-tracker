'use client'

import { Badge, Card } from '@/app/components/ui'
import type { EmailTemplate } from '../types'

export function TemplateList({
  templates,
  selectedKey,
  onSelect,
}: Readonly<{
  templates: EmailTemplate[]
  selectedKey: string
  onSelect: (key: string) => void
}>) {
  return (
    <Card as="section" title="القوالب" titleAs="h2" subtitle="اختر قالبًا لتعديله." padded={false}>
      <ul className="divide-y divide-pc-gray-100">
        {templates.map(template => {
          const selected = template.templateKey === selectedKey
          return (
            <li key={template.templateKey}>
              <button
                type="button"
                onClick={() => onSelect(template.templateKey)}
                aria-current={selected ? 'true' : undefined}
                className={`flex w-full flex-col items-start gap-1 px-pc-m py-pc-s text-start focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-pc-brand-600 ${
                  selected ? 'bg-pc-brand-50' : 'hover:bg-pc-gray-50'
                }`}
              >
                <span className="flex w-full items-center justify-between gap-pc-xs">
                  <span className="font-semibold text-pc-gray-900">{template.nameAr}</span>
                  <Badge tone={template.isEnabled ? 'success' : 'neutral'}>{template.isEnabled ? 'مفعّل' : 'معطّل'}</Badge>
                </span>
                <code dir="ltr" className="text-sm text-pc-gray-600">{template.templateKey}</code>
                <span className="text-sm text-pc-gray-600">{template.whenSent}</span>
              </button>
            </li>
          )
        })}
      </ul>
    </Card>
  )
}
