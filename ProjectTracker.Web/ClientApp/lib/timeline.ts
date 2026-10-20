const DAY_MS = 24 * 60 * 60 * 1000

type TimelineRelation = 'unavailable' | 'before' | 'active' | 'after'

export interface ProjectTimelinePosition {
  /** الأسبوع الفعلي: 0 قبل البداية، وقد يتجاوز مدة المخطط بعد نهايته. */
  elapsedWeek: number | null
  /** موضع الخط داخل المخطط؛ مقصوص عند أول/آخر أسبوع ليبقى ظاهرًا. */
  markerWeek: number | null
  relation: TimelineRelation
  label: string
}

interface TimelineProject {
  start_date?: string | null
  total_weeks?: number | null
}

/**
 * التسمية كانت «نطاق المخطط 2 أسبوعًا» و«5 أسبوعًا» في الشاشة وتقارير PDF. نفس قاعدة
 * formatCount(…, 'week') في lib/arabicCount.ts، مكررة هنا لأن الملف يُستورد مباشرة في اختبارات الوحدة
 * (Node بلا مسارات @/) ولا يستورد وحدات تشغيل أخرى.
 */
function weeksLabel(value: number): string {
  if (value === 1) return 'أسبوع واحد'
  if (value === 2) return 'أسبوعان'
  const tail = value % 100
  if (tail >= 3 && tail <= 10) return `${value} أسابيع`
  if (tail >= 11) return `${value} أسبوعًا`
  return `${value} أسبوع`
}

function dateOnlyUtc(value: string): number | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value)
  if (!match) return null
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  const result = Date.UTC(year, month - 1, day)
  const parsed = new Date(result)
  if (parsed.getUTCFullYear() !== year || parsed.getUTCMonth() !== month - 1 || parsed.getUTCDate() !== day) return null
  return result
}

/**
 * موضع موحّد للخط الأحمر في كل مخططات المشروع. الأسبوع
 * يُحسب من تاريخ بداية المشروع المدخل، ويتحرك عند كل حد سبعة أيام. قبل البداية
 * وبعد نهاية المدة يبقى الخط ظاهرًا عند الحافة مع تسمية صريحة بدل أسبوع وهمي.
 */
export function getProjectTimelinePosition(
  project: TimelineProject | null,
  today: Date,
  timelineWeeks = project?.total_weeks ?? 0
): ProjectTimelinePosition {
  const weeks = Math.max(0, Math.trunc(Number(timelineWeeks) || 0))
  const start = project?.start_date ? dateOnlyUtc(project.start_date) : null
  if (start === null) {
    return { elapsedWeek: null, markerWeek: null, relation: 'unavailable', label: 'غير متوفر — تاريخ بداية المشروع غير محدد' }
  }
  if (weeks <= 0) {
    return { elapsedWeek: null, markerWeek: null, relation: 'unavailable', label: 'غير متوفر — مدة المشروع غير محددة' }
  }

  const todayUtc = Date.UTC(today.getFullYear(), today.getMonth(), today.getDate())
  const diffDays = Math.floor((todayUtc - start) / DAY_MS)
  const elapsedWeek = diffDays < 0 ? 0 : Math.floor(diffDays / 7) + 1

  if (elapsedWeek === 0) {
    return {
      elapsedWeek,
      markerWeek: 1,
      relation: 'before',
      label: 'لم يبدأ المشروع بعد — الخط عند بداية المخطط',
    }
  }
  if (elapsedWeek > weeks) {
    return {
      elapsedWeek,
      markerWeek: weeks,
      relation: 'after',
      label: `الأسبوع الزمني ${elapsedWeek} — الخط عند نهاية المدة (${weeks})`,
    }
  }
  return {
    elapsedWeek,
    markerWeek: elapsedWeek,
    relation: 'active',
    label: `الأسبوع الحالي ${elapsedWeek} — نطاق المخطط ${weeksLabel(weeks)}`,
  }
}
