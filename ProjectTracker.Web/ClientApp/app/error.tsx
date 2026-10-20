'use client'

import { useEffect } from 'react'
import { AlertOctagon } from 'lucide-react'
import { BrandPattern } from '@/app/components/ui/BrandPattern'
import { Button } from '@/app/components/ui/Button'
import { ButtonLink } from '@/app/components/ui/ButtonLink'

export default function GlobalError({
  error,
  reset,
}: Readonly<{
  error: Error & { digest?: string }
  reset: () => void
}>) {
  useEffect(() => {
    console.error('[ProjectTracker] خطأ غير متوقع في الواجهة:', error)
  }, [error])

  return (
    <main
      id="main-content"
      dir="rtl"
      className="isolate flex min-h-screen flex-col items-center justify-center gap-pc-m bg-pc-gray-50 px-pc-m py-pc-xl text-center"
    >
      <BrandPattern fixed fade="center" opacity={0.2} />
      <span
        aria-hidden="true"
        className="flex h-16 w-16 items-center justify-center rounded-full bg-pc-error-100 text-pc-error-600"
      >
        <AlertOctagon className="h-8 w-8" />
      </span>
      <h1 className="text-pc-h2 text-pc-gray-900">تعذّر عرض هذه الصفحة</h1>
      <p role="alert" className="max-w-md text-base text-pc-gray-600">
        حدث خطأ غير متوقع أثناء تحميل البيانات. جرّب إعادة المحاولة، وإذا تكرّر الخطأ تواصل مع
        مسؤول النظام مع ذكر الرقم المرجعي أدناه.
      </p>
      {error.digest && (
        <p className="text-sm text-pc-gray-600">
          الرقم المرجعي للخطأ: <strong>{error.digest}</strong>
        </p>
      )}
      <div className="flex flex-col gap-pc-s sm:flex-row">
        <Button onClick={reset}>إعادة المحاولة</Button>
        <ButtonLink href="/" variant="secondary">
          العودة إلى الصفحة الرئيسية
        </ButtonLink>
      </div>
    </main>
  )
}
