// Note: مسارات رمز شعار النظام (public/brand/emblem.svg) بصيغة يرسمها BrandPattern كخطوط في الزخرفة.
// إن غيّرت الشعار فحدّث هذه المسارات لتبقى الزخرفة مطابقة له.
export const BRAND_EMBLEM_BOX = { x: 6, y: 6, width: 52, height: 52 } as const

export const BRAND_EMBLEM_PATHS: readonly { color: string; d: string }[] = [
  { color: '#003171', d: 'M18 6H46A12 12 0 0 1 58 18V46A12 12 0 0 1 46 58H18A12 12 0 0 1 6 46V18A12 12 0 0 1 18 6Z' },
  { color: '#004195', d: 'M16 38H23V50H16Z' },
  { color: '#004195', d: 'M28.5 30H35.5V50H28.5Z' },
  { color: '#004195', d: 'M41 24H48V50H41Z' },
  { color: '#F2A93B', d: 'M44.5 10.5L50 16L44.5 21.5L39 16Z' },
]
