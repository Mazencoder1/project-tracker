'use client'

import { useEffect, useState } from 'react'

/** يعيد احتساب التاريخ عند أول منتصف ليل تالٍ حتى يتحرك الخط دون إعادة تحميل الصفحة. */
export function useCurrentDate() {
  const [currentDate, setCurrentDate] = useState(() => new Date())

  useEffect(() => {
    let timer: ReturnType<typeof setTimeout>
    const scheduleNextDay = () => {
      const now = new Date()
      const nextDay = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1)
      timer = setTimeout(() => {
        setCurrentDate(new Date())
        scheduleNextDay()
      }, Math.max(1_000, nextDay.getTime() - now.getTime() + 1_000))
    }
    scheduleNextDay()
    return () => clearTimeout(timer)
  }, [])

  return currentDate
}

