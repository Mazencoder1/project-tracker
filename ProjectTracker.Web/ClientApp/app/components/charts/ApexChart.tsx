'use client'

import dynamic from 'next/dynamic'
import type { ApexOptions } from 'apexcharts'
import { useRemScale } from '@/lib/useRemScale'

const ReactApexChart = dynamic(() => import('react-apexcharts'), {
  ssr: false,
  loading: () => <div aria-hidden="true" className="h-full min-h-[10rem] w-full animate-pulse rounded-pc-md bg-pc-gray-100" />,
})

/** اسم التوكن ← القيمة الاحتياطية المطابقة لـ app/globals.css. */
const TOKENS = {
  brand: ['--color-pc-brand-600', '#143A6C'],
  brandDark: ['--color-pc-brand-800', '#0D2647'],
  brandLight: ['--color-pc-brand-500', '#3E5E87'],
  brandSoft: ['--color-pc-brand-100', '#CFD7E2'],
  danger: ['--color-pc-error-600', '#9B2226'],
  dangerDark: ['--color-pc-error-800', '#70181B'],
  warning: ['--color-pc-warning-600', '#8A4B08'],
  gold: ['--color-pc-accent-500', '#6540A6'],
  gray: ['--color-pc-gray-500', '#767676'],
  grayLight: ['--color-pc-gray-100', '#F0F0F0'],
  text: ['--color-pc-gray-800', '#2C2C2C'],
  muted: ['--color-pc-gray-600', '#555555'],
} as const

export type ChartColor = keyof typeof TOKENS
export type ChartPalette = Record<ChartColor, string>

/** ألوان الهوية الحالية (من CSS وقت التشغيل، أو القيم الاحتياطية على الخادم). */
export function chartPalette(): ChartPalette {
  const styles = typeof window !== 'undefined' ? getComputedStyle(document.documentElement) : null
  const palette = {} as ChartPalette
  for (const [name, [variable, fallback]] of Object.entries(TOKENS) as [ChartColor, readonly [string, string]][]) {
    palette[name] = styles?.getPropertyValue(variable).trim() || fallback
  }
  return palette
}

function interfaceFont(): string {
  if (typeof window === 'undefined') return "'Segoe UI', Tahoma, Arial, sans-serif"
  return getComputedStyle(document.body).fontFamily || "'Segoe UI', Tahoma, Arial, sans-serif"
}

/** خيارات مشتركة لكل رسوم اللوحة. */
export function baseChartOptions(): ApexOptions {
  const palette = chartPalette()
  return {
    chart: {
      fontFamily: interfaceFont(),
      foreColor: palette.muted,
      toolbar: { show: false },
      zoom: { enabled: false },
      animations: { enabled: true, speed: 350, animateGradually: { enabled: false }, dynamicAnimation: { enabled: true, speed: 250 } },
      parentHeightOffset: 0,
      redrawOnParentResize: true,
    },
    dataLabels: { enabled: false },
    legend: { show: false },
    tooltip: { theme: 'light', style: { fontFamily: interfaceFont(), fontSize: '13px' } },
    states: { hover: { filter: { type: 'darken' } }, active: { filter: { type: 'none' } } },
    grid: { borderColor: palette.grayLight, strokeDashArray: 3 },
    noData: { text: 'لا توجد بيانات كافية للعرض' },
  }
}

/** دمج عميق بسيط لكائنات الخيارات (المصفوفات تُستبدل). */
function merge<T>(base: T, extra: Partial<T> | undefined): T {
  if (!extra) return base
  const out: Record<string, unknown> = { ...(base as Record<string, unknown>) }
  for (const [key, value] of Object.entries(extra as Record<string, unknown>)) {
    const current = out[key]
    out[key] =
      value && typeof value === 'object' && !Array.isArray(value) && current && typeof current === 'object' && !Array.isArray(current)
        ? merge(current, value as Record<string, unknown>)
        : value
  }
  return out as T
}

function scaleFontSizes<T>(value: T, scale: number): T {
  if (scale === 1 || !value || typeof value !== 'object') return value
  if (Array.isArray(value)) return value.map(item => scaleFontSizes(item, scale)) as T
  const out: Record<string, unknown> = {}
  for (const [key, item] of Object.entries(value as Record<string, unknown>)) {
    const px = key === 'fontSize' && typeof item === 'string' ? /^(\d+(?:\.\d+)?)px$/.exec(item) : null
    if (px) out[key] = `${Math.round(Number(px[1]) * scale)}px`
    else out[key] = typeof item === 'function' ? item : scaleFontSizes(item, scale)
  }
  return out as T
}

export function ApexChart({
  type,
  series,
  options,
  height,
  ariaLabel,
}: Readonly<{
  type: 'donut' | 'radialBar' | 'bar' | 'area' | 'line'
  series: ApexOptions['series']
  options?: ApexOptions
  height: number
  /** وصف نصي للرسم لقارئ الشاشة (البيانات نفسها معروضة نصًا بجانب كل رسم). */
  ariaLabel: string
}>) {
  const scale = useRemScale()
  const scaledHeight = Math.round(height * scale)
  return (
    <div role="img" aria-label={ariaLabel} dir="ltr" className="w-full min-w-0" style={{ minHeight: scaledHeight }}>
      <ReactApexChart
        key={scale}
        type={type}
        series={series}
        options={scaleFontSizes(merge(baseChartOptions(), options), scale)}
        height={scaledHeight}
        width="100%"
      />
    </div>
  )
}
