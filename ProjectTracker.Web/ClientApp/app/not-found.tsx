import { FileQuestion } from 'lucide-react'
import { ButtonLink } from '@/app/components/ui/ButtonLink'
import { SkipLink } from '@/app/components/ui/Navigation'
import { BrandPattern } from '@/app/components/ui/BrandPattern'

export const metadata = {
  title: 'الصفحة غير موجودة — منصة متابعة المشاريع',
}

export default function NotFound() {
  return (
    <>
      <SkipLink />
      <main
        id="main-content"
        tabIndex={-1}
        dir="rtl"
        className="isolate flex min-h-screen flex-col items-center justify-center gap-pc-m bg-pc-gray-50 px-pc-m py-pc-xl text-center outline-none"
      >
        <BrandPattern fixed fade="center" opacity={0.2} />
        <span
          aria-hidden="true"
          className="flex h-16 w-16 items-center justify-center rounded-full bg-pc-brand-50 text-pc-brand-700"
        >
          <FileQuestion className="h-8 w-8" />
        </span>
        <p className="text-pc-h3 font-bold text-pc-brand-900">خطأ 404</p>
        <h1 className="text-pc-h2 text-pc-gray-900">الصفحة المطلوبة غير موجودة</h1>
        <p className="max-w-md text-base text-pc-gray-600">
          قد يكون الرابط قديمًا أو مكتوبًا بشكل غير صحيح، أو أن الصفحة نُقلت إلى مكان آخر.
        </p>
        <div className="flex flex-col gap-pc-s sm:flex-row">
          <ButtonLink href="/" variant="primary">
            العودة إلى الصفحة الرئيسية
          </ButtonLink>
          <ButtonLink href="/meeting" variant="secondary">
            الذهاب إلى لوحة الاجتماع
          </ButtonLink>
        </div>
      </main>
    </>
  )
}
