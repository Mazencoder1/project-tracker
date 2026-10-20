'use client'

import { useEffect, useState } from 'react'

function readScale(): number {
  if (typeof window === 'undefined') return 1
  const size = Number.parseFloat(getComputedStyle(document.documentElement).fontSize)
  return Number.isFinite(size) && size > 0 ? size / 16 : 1
}

export function useRemScale(): number {
  const [scale, setScale] = useState(1)
  useEffect(() => {
    const update = () => setScale(prev => {
      const next = Math.round(readScale() * 100) / 100
      return next === prev ? prev : next
    })
    update()
    window.addEventListener('resize', update)
    return () => window.removeEventListener('resize', update)
  }, [])
  return scale
}
