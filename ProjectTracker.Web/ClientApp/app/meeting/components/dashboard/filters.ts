import type { ExecutiveStatus } from '@/lib/portfolio/executive'

export type QuickFilter = 'all' | ExecutiveStatus | 'needs_support' | 'near_deadline' | 'delayed_tasks' | 'attention'

export const NO_DEPARTMENT = '__none__'

/** عنوان قائمة المشاريع حسب الفلتر النشط. */
export const QUICK_FILTER_TITLE: Record<QuickFilter, string> = {
  all: 'جميع المشاريع',
  on_track: 'المشاريع ضمن الخطة',
  needs_attention: 'المشاريع التي تحتاج متابعة',
  delayed: 'المشاريع المتأخرة',
  stalled: 'المشاريع المتعثرة',
  completed: 'المشاريع المكتملة',
  unknown: 'مشاريع بلا بيانات كافية',
  needs_support: 'المشاريع التي تحتاج دعمًا',
  near_deadline: 'المشاريع القريبة من موعد الانتهاء',
  delayed_tasks: 'المشاريع التي بها مهام متأخرة',
  attention: 'مشاريع تستدعي الانتباه',
}

