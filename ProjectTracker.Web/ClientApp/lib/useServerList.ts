'use client'

import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError, apiJson, errorMessage } from '@/app/components/executive/api'

interface PageResponse {
  pagination?: { page: number; pageSize: number; total: number; totalPages: number }
  [key: string]: unknown
}

export interface ServerListState<T> {
  /** صفوف الصفحة الحالية فقط. */
  rows: T[]
  /** رقم الصفحة الحالي — يبدأ من 0 (مثل usePagination). */
  page: number
  pageSize: number
  pageCount: number
  /** إجمالي الصفوف المطابقة للمرشحات في الخادم. */
  total: number
  setPage: (page: number) => void
  /** أول تحميل (لا صفوف بعد). */
  loading: boolean
  /** جلب صفحة/مرشح جديد مع بقاء الصفوف السابقة معروضة. */
  refreshing: boolean
  error: string
  /** رمز HTTP للخطأ (403 للصلاحية مثلًا)، أو 0. */
  errorStatus: number
  /** الرد الكامل الأخير — للحقول الإضافية بجانب المصفوفة (stats / allTotal ...). */
  data: PageResponse | null
  /** إعادة جلب الصفحة الحالية (بعد إضافة/تعديل/حذف). */
  reload: () => Promise<void>
}

/**
 * يجلب صفحة واحدة من مسار مُرقَّم في الخادم.
 * @param url      المسار مع المرشحات (بدون page/pageSize)، أو null لتعطيل الجلب
 * @param key      مفتاح المصفوفة في الرد (`projects` / `users` / `entries` ...)
 * @param pageSize حجم الصفحة (لا يتجاوز maxPageSize الخاص بالمسار في الخادم)
 */
export function useServerList<T>(url: string | null, key: string, pageSize: number): ServerListState<T> {
  const [page, setPage] = useState(0)
  const [rows, setRows] = useState<T[]>([])
  const [total, setTotal] = useState(0)
  const [data, setData] = useState<PageResponse | null>(null)
  const [busy, setBusy] = useState(false)
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState('')
  const [errorStatus, setErrorStatus] = useState(0)
  const seq = useRef(0)

  // مرشح جديد ⇒ الصفحة الأولى (تعديل الحالة أثناء العرض — نمط React الموصى به بدل effect).
  const [lastUrl, setLastUrl] = useState(url)
  if (url !== lastUrl) {
    setLastUrl(url)
    setPage(0)
  }

  const load = useCallback(async () => {
    if (!url) return
    const id = ++seq.current
    setBusy(true)
    setError('')
    setErrorStatus(0)
    try {
      const sep = url.includes('?') ? '&' : '?'
      const res = await apiJson<PageResponse>(`${url}${sep}page=${page + 1}&pageSize=${pageSize}`)
      if (id !== seq.current) return
      const batch = (res[key] as T[] | undefined) ?? []
      const count = res.pagination?.total ?? batch.length
      const pages = Math.max(1, res.pagination?.totalPages ?? 1)
      // الصفحة لم تعد موجودة (حُذف آخر صف فيها): انتقل لآخر صفحة بدل عرض قائمة فارغة.
      if (batch.length === 0 && count > 0 && page > pages - 1) {
        setPage(pages - 1)
        return
      }
      setRows(batch)
      setTotal(count)
      setData(res)
      setLoaded(true)
    } catch (e) {
      if (id !== seq.current) return
      setError(errorMessage(e, 'تعذّر تحميل البيانات.'))
      setErrorStatus(e instanceof ApiError ? e.status : 0)
    } finally {
      if (id === seq.current) setBusy(false)
    }
  }, [url, key, page, pageSize])

  useEffect(() => {
    load()
  }, [load])

  const pageCount = Math.max(1, Math.ceil(total / pageSize))
  const changePage = useCallback((next: number) => setPage(Math.max(0, next)), [])

  return {
    rows,
    page: Math.min(page, pageCount - 1),
    pageSize,
    pageCount,
    total,
    setPage: changePage,
    loading: busy && !loaded,
    refreshing: busy && loaded,
    error,
    errorStatus,
    data,
    reload: load,
  }
}

/** يبني query string من قيم المرشحات، ويُسقط الفارغة و'all'. */
export function listQuery(params: Record<string, string | null | undefined>): string {
  const q = new URLSearchParams()
  for (const [name, value] of Object.entries(params)) {
    if (value && value !== 'all') q.set(name, value)
  }
  return q.toString()
}

/** قيمة تتأخر عن الأصل `delay` ms — للبحث حتى لا يُرسل طلب مع كل حرف. */
export function useDebouncedValue<T>(value: T, delay = 300): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay)
    return () => clearTimeout(timer)
  }, [value, delay])
  return debounced
}
