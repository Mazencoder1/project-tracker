import { MS_COLORS } from '@/app/meeting/constants'

/** اللوحة مع أسماء عربية (لـ aria-label في مجموعة أزرار الراديو). */
export const PHASE_COLOR_OPTIONS: { value: string; label: string }[] = [
  { value: MS_COLORS[0], label: 'كحلي داكن' },
  { value: MS_COLORS[1], label: 'أزرق أساسي' },
  { value: MS_COLORS[2], label: 'برتقالي داكن' },
  { value: MS_COLORS[3], label: 'أحمر داكن' },
  { value: MS_COLORS[4], label: 'أخضر غامق' },
]

function parseHex(color: string): [number, number, number] | null {
  const m = /^#?([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(color.trim())
  if (!m) return null
  let hex = m[1]
  if (hex.length === 3) hex = hex.split('').map(c => c + c).join('')
  return [Number.parseInt(hex.slice(0, 2), 16), Number.parseInt(hex.slice(2, 4), 16), Number.parseInt(hex.slice(4, 6), 16)]
}

/** اللون الذي يُرسم فعليًا لمرحلة — دائمًا من لوحة المراحل. */
export function phaseColor(color: string | null | undefined, index = 0): string {
  const fallback = MS_COLORS[((index % MS_COLORS.length) + MS_COLORS.length) % MS_COLORS.length]
  if (!color) return fallback
  const exact = MS_COLORS.find(c => c.toLowerCase() === color.trim().toLowerCase())
  if (exact) return exact
  const rgb = parseHex(color)
  if (!rgb) return fallback
  let best = fallback
  let bestDist = Infinity
  for (const candidate of MS_COLORS) {
    const c = parseHex(candidate)!
    const dist = (c[0] - rgb[0]) ** 2 + (c[1] - rgb[1]) ** 2 + (c[2] - rgb[2]) ** 2
    if (dist < bestDist) {
      bestDist = dist
      best = candidate
    }
  }
  return best
}

function relativeLuminance([r, g, b]: [number, number, number]): number {
  const lin = (v: number) => {
    const s = v / 255
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b)
}

/** نسبة التباين WCAG بين لونين hex. */
function contrastRatio(a: string, b: string): number {
  const ra = parseHex(a)
  const rb = parseHex(b)
  if (!ra || !rb) return 1
  const la = relativeLuminance(ra)
  const lb = relativeLuminance(rb)
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05)
}

/**
 * درجة فاتحة من لون (مزج مع الأبيض بنسبة weight من اللون الأصلي) كقيمة hex صريحة —
 * لا color-mix() لأن html2canvas في تصدير PDF لا يفهمه.
 */
export function tintColor(color: string, weight: number): string {
  const rgb = parseHex(color)
  if (!rgb) return '#FFFFFF'
  const w = Math.min(1, Math.max(0, weight))
  return '#' + rgb.map(v => Math.round(v * w + 255 * (1 - w)).toString(16).padStart(2, '0')).join('')
}

/** لون نص مقروء فوق خلفية: الأبيض إن حقق ≥ 4.5:1، وإلا النص الأساسي الداكن. */
export function readableTextOn(background: string): '#FFFFFF' | '#1A1A1A' {
  return contrastRatio(background, '#FFFFFF') >= 4.5 ? '#FFFFFF' : '#1A1A1A'
}
