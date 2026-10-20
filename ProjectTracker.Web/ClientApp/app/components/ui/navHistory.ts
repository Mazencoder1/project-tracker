const CURRENT_KEY = 'tracker:nav:current'
const PREVIOUS_KEY = 'tracker:nav:previous'

export function recordInternalPath(pathname: string): void {
  try {
    const current = sessionStorage.getItem(CURRENT_KEY)
    if (current === pathname) return
    if (current) sessionStorage.setItem(PREVIOUS_KEY, current)
    sessionStorage.setItem(CURRENT_KEY, pathname)
  } catch {
    /* تخزين الجلسة غير متاح (وضع خاص/محظور) — أزرار الرجوع تستخدم المسار البديل */
  }
}

export function hasInternalHistory(): boolean {
  try {
    return Boolean(sessionStorage.getItem(PREVIOUS_KEY))
  } catch {
    return false
  }
}
