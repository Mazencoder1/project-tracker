import { useId } from 'react'
import { BRAND_EMBLEM_BOX, BRAND_EMBLEM_PATHS } from './brandEmblemPaths'

const TILE = 150
const EMBLEM_WIDTH = 50
const SCALE = EMBLEM_WIDTH / BRAND_EMBLEM_BOX.width
const CENTER_X = BRAND_EMBLEM_BOX.x + BRAND_EMBLEM_BOX.width / 2
const CENTER_Y = BRAND_EMBLEM_BOX.y + BRAND_EMBLEM_BOX.height / 2
/** لون المعيّن في الرمز. */
const ACCENT = '#F2A93B'

function Emblem({ x, y, rotate = 0 }: Readonly<{ x: number; y: number; rotate?: number }>) {
  return (
    <g transform={`translate(${x} ${y}) rotate(${rotate}) scale(${SCALE}) translate(${-CENTER_X} ${-CENTER_Y})`}>
      {BRAND_EMBLEM_PATHS.map(path =>
        path.color === ACCENT ? (
          <path key={path.d} d={path.d} fill={ACCENT} fillOpacity={0.6} />
        ) : (
          <path
            key={path.d}
            d={path.d}
            fill="none"
            stroke={path.color}
            strokeWidth={1.1}
            strokeLinejoin="round"
            vectorEffect="non-scaling-stroke"
          />
        )
      )}
    </g>
  )
}

export function BrandPattern({
  className = '',
  opacity = 0.22,
  fade = 'center',
  fixed = false,
}: Readonly<{
  className?: string
  opacity?: number
  /** center: تتلاشى خلف المنتصف. none: تغطية متساوية. */
  fade?: 'center' | 'none'
  /** طبقة ثابتة على كامل الشاشة خلف المحتوى (الأب يحتاج `isolate`). */
  fixed?: boolean
}>) {
  const uid = useId().replace(/[^a-zA-Z0-9_-]/g, '')
  const half = TILE / 2
  const position = fixed ? 'fixed inset-0 -z-10' : 'absolute inset-0'
  return (
    <svg aria-hidden="true" className={`pointer-events-none h-full w-full ${position} ${className}`}>
      <defs>
        <pattern id={`${uid}-tile`} width={TILE} height={TILE} patternUnits="userSpaceOnUse">
          <path
            d={`M${half} 0 L${TILE} ${half} L${half} ${TILE} L0 ${half} Z`}
            fill="none"
            stroke="#004195"
            strokeWidth={0.8}
          />
          {[
            [half, 0],
            [TILE, half],
            [half, TILE],
            [0, half],
          ].map(([cx, cy]) => (
            <circle key={`${cx}-${cy}`} cx={cx} cy={cy} r={2.4} fill={ACCENT} />
          ))}
          <Emblem x={half} y={half} />
          {[
            [0, 0],
            [TILE, 0],
            [0, TILE],
            [TILE, TILE],
          ].map(([ex, ey]) => (
            <Emblem key={`${ex}-${ey}`} x={ex} y={ey} />
          ))}
        </pattern>
        {fade === 'center' && (
          <>
            <radialGradient id={`${uid}-fade`} cx="50%" cy="42%" r="72%">
              <stop offset="0" stopColor="#000" />
              <stop offset="0.32" stopColor="#000" />
              <stop offset="1" stopColor="#FFF" />
            </radialGradient>
            <mask id={`${uid}-mask`}>
              <rect width="100%" height="100%" fill={`url(#${uid}-fade)`} />
            </mask>
          </>
        )}
      </defs>
      <rect
        width="100%"
        height="100%"
        fill={`url(#${uid}-tile)`}
        mask={fade === 'center' ? `url(#${uid}-mask)` : undefined}
        opacity={opacity}
      />
    </svg>
  )
}
