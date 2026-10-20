
interface PaginatedResponse {
  pagination?: { page: number; totalPages: number }
  [key: string]: unknown
}

/**
 * يجلب كل صفحات مسار مُرقَّم ويُرجع المصفوفة المجمّعة تحت `key`.
 * @param url      المسار مع أي query موجود مسبقًا (مثل `/api/tasks?project_id=1`)
 * @param key      مفتاح المصفوفة في الاستجابة (`projects` / `tasks` ...)
 * @param pageSize أكبر حجم صفحة يقبله المسار (maxPageSize في parsePagination)
 */
export async function fetchAllPages<T>(url: string, key: string, pageSize: number): Promise<T[]> {
  const sep = url.includes('?') ? '&' : '?'
  const items: T[] = []
  // حد أمان ضد حلقة لا نهائية لو أعاد الخادم totalPages خاطئًا.
  const MAX_PAGES = 50

  for (let page = 1; page <= MAX_PAGES; page++) {
    const res = await fetch(`${url}${sep}page=${page}&pageSize=${pageSize}`)
    if (!res.ok) throw new Error(`تعذّر جلب ${key} (رمز ${res.status})`)
    const data = (await res.json()) as PaginatedResponse
    const batch = (data[key] as T[] | undefined) ?? []
    items.push(...batch)

    const totalPages = data.pagination?.totalPages ?? 1
    // الخادم لا يدعم الترقيم على هذا المسار، أو وصلنا للصفحة الأخيرة.
    if (page >= totalPages || batch.length === 0) break
  }
  return items
}
