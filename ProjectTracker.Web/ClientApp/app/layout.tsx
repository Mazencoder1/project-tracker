import type { Metadata } from 'next'
import { IBM_Plex_Sans_Arabic } from 'next/font/google'
import './globals.css'
import { ToastProvider } from './components/ui'

// Note: خط "IBM Plex Sans Arabic" يُحمَّل عبر next/font ليُخزَّن ذاتيًا (self-hosted) بدل CDN خارجي،
// ويُعرَّف كمتغيّر CSS يستخدمه --font-arabic في app/globals.css.
const ibmPlexArabic = IBM_Plex_Sans_Arabic({
  subsets: ['arabic'],
  weight: ['400', '500', '600', '700'],
  variable: '--font-ibm-plex-arabic',
  display: 'swap',
})

export const metadata: Metadata = {
  title: 'منصة متابعة المشاريع',
  description: 'منصة موحدة لمتابعة تنفيذ المشاريع ومراحلها ومهامها',
}

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode
}>) {
  return (
    <html lang="ar" dir="rtl">
      <body className={`${ibmPlexArabic.variable} bg-pc-gray-50 text-pc-gray-900 antialiased`}>
        <ToastProvider>{children}</ToastProvider>
      </body>
    </html>
  )
}