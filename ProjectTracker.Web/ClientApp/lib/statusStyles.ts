// أنماط ألوان الحالة الموحّدة (Tailwind pc-*) — تحل محل خرائط statusBg/statusTx
// المكرّرة سابقًا بقيم hex شبه متطابقة في app/admin/page.tsx وapp/meeting/page.tsx
//. المطابقة نفسها اللون القديم ←
// الجديد موثّقة مسبقًا في إرشادات التصميم: نشط→warning (أصفر)، مكتمل→success (أخضر)،
// متأخر/خطأ→error (أحمر)، ملغى/محذوف→gray. لا تُصدَّر تسميات (labels) هنا عمدًا —
// كل صفحة تحتفظ بنصوصها الخاصة (بعضها يختلف فعليًا، مثال: admin يعرض "نشط"
// بينما pm يعرض "جاري" لنفس القيمة) حتى لا يتغيّر أي محتوى ظاهر للمستخدم.

export const PROJECT_STATUS_LABEL: Record<string, string> = {
  active: 'نشط',
  completed: 'مكتمل',
  delayed: 'متأخر',
  cancelled: 'ملغى',
}

/** خلفية + لون نص شارة (badge) جاهزين كـ className واحد لحالة المشروع. */
export function projectStatusBadgeClass(status: string): string {
  switch (status) {
    case 'active': return 'bg-pc-info-100 text-pc-info-800'
    case 'completed': return 'bg-pc-success-100 text-pc-success-800'
    case 'delayed': return 'bg-pc-error-100 text-pc-error-800'
    case 'cancelled': return 'bg-pc-gray-100 text-pc-gray-600'
    default: return 'bg-pc-gray-100 text-pc-gray-600'
  }
}

/** خلفية + لون نص شارة (badge) جاهزين كـ className واحد لحالة الموظف. */
export function userStatusBadgeClass(status: string): string {
  switch (status) {
    case 'active': return 'bg-pc-success-100 text-pc-success-800'
    case 'disabled': return 'bg-pc-error-100 text-pc-error-800'
    case 'deleted': return 'bg-pc-gray-100 text-pc-gray-600'
    default: return 'bg-pc-gray-100 text-pc-gray-600'
  }
}
