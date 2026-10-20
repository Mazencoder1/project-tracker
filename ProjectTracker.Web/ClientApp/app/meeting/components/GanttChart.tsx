'use client'

import { useEffect, useRef, useState } from 'react'
import { phaseColor, tintColor } from '@/lib/phaseColor'
import type { Milestone, Task } from '../types'
import { parseCalendarDate } from '@/app/components/ui/Content'
import { useRemScale } from '@/lib/useRemScale'

export type GanttZoom = 'week' | 'month' | 'quarter'

/**
 * مستوى التكبير الافتراضي حسب مدة المشروع. مشروع من 3 أسابيع على مستوى
 * «شهر» كان يشغل جزءًا صغيرًا من عرض المخطط؛ المشاريع الأقصر من 8 أسابيع تبدأ على «أسبوع».
 */
export function defaultGanttZoom(totalWeeks: number | null | undefined): GanttZoom {
  return (totalWeeks || 0) < 8 ? 'week' : 'month'
}

// Note: مستويات تكبير/تصغير الجدول الزمني ( من طلب إعادة تصميم
// الداشبورد: "أسبوع / شهر / ربع سنة، Default = شهر") — تغيّر فقط عرض الخلية
// بالبكسل وكثافة تسميات الرأس، ولا تغيّر إطلاقًا حساب مواقع/عروض أشرطة المراحل
// (ws/we/span تبقى كما هي بالضبط) — أقل خطر ممكن على منطق كثيف الحسابات وموجود مسبقًا.
// قبل إضافة التكبير كان CELL_W ثابتًا = 40px (راجع git
// HEAD). الخريطة السابقة { week: 40, month: 20, quarter: 10 } جعلت العرض الافتراضي
// (شهر) 20px، أي غيّرت الرسم الافتراضي فعليًا رغم قيد «لا تغيير لحسابات المواضع».
// أُعيد الافتراضي (شهر) إلى 40px بالضبط مع الحفاظ على نسبة المستويات (×2 / ÷2).
const ZOOM_CELL_W: Record<GanttZoom, number> = { week: 80, month: 40, quarter: 20 }
const ZOOM_GROUP_WEEKS: Record<GanttZoom, number> = { week: 1, month: 4, quarter: 13 }

// تاريخ تقويمي حقيقي لأسبوع معيّن، محسوب من start_date الفعلي للمشروع — يُستخدم
// فقط لتسمية أعمدة الرأس في مستوى شهر/ربع، وليس له أي أثر على منطق الأشرطة.
function getWeekCalendarDate(weekNum: number, weekOffset: number, projectStartDate?: string): Date | null {
  if (!projectStartDate) return null
  const start = parseCalendarDate(projectStartDate) ?? new Date(projectStartDate)
  if (Number.isNaN(start.getTime())) return null
  return new Date(start.getTime() + (weekNum + weekOffset - 1) * 7 * 24 * 60 * 60 * 1000)
}

interface HeaderGroup {
  /** عدد الأسابيع التي تغطيها المجموعة من أسبوع بدايتها. */
  span: number
  label: string
  /** التسمية الكاملة (تلميح) حين تُختصر التسمية لضيق المجموعة. */
  title: string
}

/**
 * مجموعات رأس «شهر/ربع سنة» صارت تتبع حدود التقويم الفعلية. كانت مجموعات
 * ثابتة (4 / 13 أسبوعًا من بداية المشروع) تنحرف عن الأشهر (تكرر «أغسطس» في مشروع من 53 أسبوعًا)،
 * و«ر1» بلا سنة تكررت في مشروع يعبر سنتين. الآن تبدأ المجموعة عند أول أسبوع يقع تاريخ بدايته في
 * شهر/ربع جديد، ويُذكر العام مع أول مجموعة ومع كل يناير/ربع أول. بلا تاريخ بداية نعود لمجموعات
 * ثابتة بأرقام الأسابيع. لا أثر على مواضع الأشرطة (CELL_W/ws/we كما هي).
 */
function calendarGroupLabel(date: Date, zoom: 'month' | 'quarter', widthPx: number, isFirstGroup: boolean): Omit<HeaderGroup, 'span'> {
  const year = date.getFullYear()
  if (zoom === 'month') {
    const month = date.toLocaleDateString('ar-SA-u-ca-gregory-nu-latn', { month: 'long' })
    const title = `${month} ${year}`
    const withYear = (isFirstGroup || date.getMonth() === 0) && widthPx >= 96
    return { title, label: withYear ? title : month }
  }
  const q = Math.floor(date.getMonth() / 3) + 1
  const title = `الربع ${q} — ${year}`
  if (widthPx >= 120) return { title, label: title }
  return { title, label: widthPx >= 56 ? `الربع ${q}` : `ر${q}` }
}

function buildHeaderGroups(weeks: number, weekOffset: number, zoom: GanttZoom, cellW: number, projectStartDate?: string): Map<number, HeaderGroup> {
  const groups = new Map<number, HeaderGroup>()
  if (zoom === 'week') return groups

  const dateOf = (weekNum: number) => getWeekCalendarDate(weekNum, weekOffset, projectStartDate)
  const keyOf = (date: Date) => date.getFullYear() * 12 + (zoom === 'month' ? date.getMonth() : Math.floor(date.getMonth() / 3))

  // بلا تاريخ بداية: مجموعات ثابتة بأرقام الأسابيع (السلوك السابق).
  if (!dateOf(1)) {
    for (let start = 1; start <= weeks; start += ZOOM_GROUP_WEEKS[zoom]) {
      const label = String(start + weekOffset)
      groups.set(start, { span: Math.min(ZOOM_GROUP_WEEKS[zoom], weeks - start + 1), label, title: label })
    }
    return groups
  }

  let startWeek = 1
  for (let w = 1; w <= weeks; w++) {
    const next = w < weeks ? dateOf(w + 1) : null
    if (next && keyOf(next) === keyOf(dateOf(w)!)) continue
    const span = w - startWeek + 1
    groups.set(startWeek, { span, ...calendarGroupLabel(dateOf(startWeek)!, zoom, span * cellW, startWeek === 1) })
    startWeek = w + 1
  }
  return groups
}

// عرض تقريبي لنص النسبة (خط 13–14px عريض) لتقرير هل يتسع داخل الشريط أم يُكتب بجانبه.
function percentTextWidth(text: string, scale: number): number {
  return (text.length * 9 + 8) * scale
}

interface GanttBarProps {
  name: string
  color: string
  progress: number
  isComplete: boolean
  barWidthPx: number
  /** يوجد عمود فارغ بعد نهاية الشريط يمكن كتابة النسبة فيه. */
  canLabelOutside: boolean
  /** معامل تكبير الواجهة على الشاشات الكبيرة (lib/useRemScale). */
  scale: number
}

function rowBackground(isCurrent: boolean, idx: number): string {
  if (isCurrent) return '#EAEFF5'
  return idx % 2 === 0 ? '#fff' : '#F7F7F7'
}

function rowState(isComplete: boolean, isOverdue: boolean, inProgress: boolean): { label: string; className: string } {
  if (isComplete) return { label: 'مكتملة', className: 'bg-pc-success-100 text-pc-success-800' }
  if (isOverdue) return { label: 'متأخرة', className: 'bg-pc-error-100 text-pc-error-800' }
  if (inProgress) return { label: 'جارية', className: 'bg-pc-brand-100 text-pc-brand-800' }
  return { label: 'لم تبدأ', className: 'bg-pc-gray-100 text-pc-gray-700' }
}

const PERCENT_TEXT_STYLE = { fontSize: '0.8125rem', fontWeight: 700, lineHeight: '1.25rem', whiteSpace: 'nowrap' } as const
const DARK_TEXT = '#1A1A1A'

/**
 * المسار أبيض بحدّ #8A8A8A (3.45:1) لأنه المؤشر
 * الوحيد على النطاق الزمني عند إنجاز 0%؛ الارتفاع 24 وline-height صريح يمنعان قصّ «100%» من الأسفل.
 * (1) المرحلة المكتملة تُملأ بدرجة فاتحة من لونها وتُكتب «100%» بالأسود (طلب المستخدم؛
 * الأسود فوق ألوان المراحل الداكنة نفسها غير مقروء). (2) موضع النسبة يُختار حسب المساحة الفعلية لا حسب
 * النسبة وحدها: داخل التعبئة ← وسط المسار ← الجزء الفارغ ← بجانب الشريط؛ في «ربع سنة» كانت «100%» تفيض
 * خارج شريط من أسبوعين، وأشرطة الأسبوع الواحد تظهر بلا نسبة.
 */
function GanttBar({ name, color, progress, isComplete, barWidthPx, canLabelOutside, scale }: Readonly<GanttBarProps>) {
  const text = `${Math.round(progress)}%`
  const textW = percentTextWidth(text, scale)
  const filledPx = Math.max(0, Math.round((progress / 100) * barWidthPx))
  const emptyPx = barWidthPx - filledPx

  let placement: 'fill' | 'track' | 'empty' | 'outside' | 'none' = 'none'
  if (!isComplete && progress > 20 && filledPx >= textW) placement = 'fill'
  else if ((isComplete || progress <= 20) && barWidthPx >= textW) placement = 'track'
  else if (!isComplete && emptyPx >= textW) placement = 'empty'
  else if (canLabelOutside) placement = 'outside'

  return (
    <>
      <div title={`${name}: ${text}`} style={{
        width: barWidthPx,
        height: Math.round(24 * scale),
        background: '#FFFFFF',
        border: `1px solid ${isComplete ? color : '#8A8A8A'}`,
        boxSizing: 'border-box',
        borderRadius: 8,
        overflow: 'hidden',
        position: 'relative'
      }}>
        <div style={{
          width: filledPx,
          height: '100%',
          background: isComplete ? tintColor(color, 0.22) : color,
          borderRadius: 8,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          transition: 'width .3s'
        }}>
          {placement === 'fill' && <span style={{ ...PERCENT_TEXT_STYLE, color: '#fff', fontSize: '0.875rem' }}>{text}</span>}
        </div>
        {(placement === 'track' || placement === 'empty') && (
          <span style={{
            ...PERCENT_TEXT_STYLE,
            position: 'absolute',
            top: 0,
            bottom: 0,
            left: 0,
            width: placement === 'track' ? '100%' : emptyPx,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            color: DARK_TEXT,
          }}>
            {text}
          </span>
        )}
      </div>
      {placement === 'outside' && (
        <span aria-hidden="true" style={{
          ...PERCENT_TEXT_STYLE,
          position: 'absolute',
          top: '50%',
          right: 'calc(100% + 4px)',
          transform: 'translateY(-50%)',
          zIndex: 11,
          color: DARK_TEXT,
          background: 'rgba(255,255,255,0.9)',
          borderRadius: 4,
          paddingInline: 2,
        }}>
          {text}
        </span>
      )}
    </>
  )
}

// Note: هذه الخصائص تسمح لنفس المخطط بعرض المشروع الكامل أو مهام مرحلة محددة بدون تكرار الكود.
interface GanttChartProps {
  msList: Milestone[]
  tasksList: Task[]
  weeks: number
  todayW: number
  mini?: boolean
  weekOffset?: number
  miniColor?: string
  autoCenterToday?: boolean
  zoom?: GanttZoom
  projectStartDate?: string
  /** أسبوع فعلي غير مقصوص، لاشتقاق حالة التأخر حتى بعد نهاية المشروع. */
  statusWeek?: number
  /** العنصر الممثل للمرحلة الحالية في العرض العلوي. */
  currentItemId?: string
}

// Note: فصل مخطط Gantt عن الصفحة يقلل حجم page.tsx ويحافظ على نفس طريقة عرض المراحل والمهام.
export function GanttChart({
  msList,
  tasksList,
  weeks,
  todayW,
  mini,
  weekOffset = 0,
  miniColor,
  autoCenterToday,
  zoom = 'month',
  projectStartDate,
  statusWeek = todayW,
  currentItemId,
}: Readonly<GanttChartProps>) {
  const scrollRef = useRef<HTMLDivElement | null>(null)
  const scale = useRemScale()
  const [containerW, setContainerW] = useState<number | null>(null)
  useEffect(() => {
    const el = scrollRef.current
    if (!el || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(([entry]) => setContainerW(entry.contentRect.width))
    observer.observe(el)
    return () => observer.disconnect()
  }, [])
  const LABEL_W = Math.round(containerW === null ? 280 * scale : Math.min(280 * scale, Math.max(150 * scale, containerW * 0.45)))
  const baseCellW = Math.round((mini ? 34 : ZOOM_CELL_W[zoom]) * scale)
  const CELL_W = containerW !== null && weeks > 0
    ? Math.max(baseCellW, Math.floor((containerW - LABEL_W) / weeks))
    : baseCellW
  const totalW = LABEL_W + weeks * CELL_W
  const showTodayLine = todayW > 0 && todayW <= weeks
  const headerGroups = mini ? new Map<number, HeaderGroup>() : buildHeaderGroups(weeks, weekOffset, zoom, CELL_W, projectStartDate)
  const grouped = !mini && zoom !== 'week' && headerGroups.size > 0
  const columns: { start: number; span: number; group?: HeaderGroup }[] = grouped
    ? Array.from(headerGroups, ([start, group]) => ({ start, span: group.span, group }))
    : Array.from({ length: weeks }, (_, i) => ({ start: i + 1, span: 1 }))
  const columnOf = (week: number) => columns.findIndex((c) => week >= c.start && week < c.start + c.span)
  const columnHasToday = (c: { start: number; span: number }) => showTodayLine && todayW >= c.start && todayW < c.start + c.span
  // Note: تثبيت عمود أسماء المراحل على اليمين يحقق طلب المدير مع بقاء التركيز على خط الأسبوع الحالي.
  const stickyLabelStyle = {
    position: 'sticky' as const,
    right: 0,
    zIndex: 20,
    boxShadow: '-6px 0 10px rgba(11, 33, 61,0.06)',
  }

  // Note: عند تغيير المشروع نوسّط مخطط المشروع تلقائيًا على خط الأسبوع الحالي، ثم يبقى المستخدم حرًا في التحريك.
  useEffect(() => {
    if (!autoCenterToday || !showTodayLine || !scrollRef.current) return

    const container = scrollRef.current
    const todayLineFromRight = LABEL_W + (todayW - 1) * CELL_W + CELL_W / 2
    const todayLineFromLeft = totalW - todayLineFromRight
    const maxScroll = Math.max(0, container.scrollWidth - container.clientWidth)
    const targetScroll = Math.min(
      maxScroll,
      Math.max(0, todayLineFromLeft - container.clientWidth / 2)
    )

    container.scrollTo({ left: targetScroll, behavior: 'auto' })
  }, [autoCenterToday, CELL_W, LABEL_W, showTodayLine, todayW, totalW, weeks])

  return (
    <div ref={scrollRef} className="meeting-gantt-scroll" style={{ overflowX: 'auto', direction: 'ltr' }}>
      <div data-gantt-kind={mini ? 'tasks' : 'milestones'} className="meeting-gantt-body" style={{ position: 'relative', minWidth: totalW, direction: 'rtl' }}>
        {showTodayLine && (
          <div data-current-time-line="true" aria-hidden="true" style={{
            position: 'absolute',
            top: 0,
            bottom: 0,
            zIndex: 10,
            right: LABEL_W + (todayW - 1) * CELL_W + CELL_W / 2,
            width: 2,
            background: '#9B2226',
            pointerEvents: 'none'
          }} />
        )}

        <div style={{
          display: 'flex',
          position: 'relative',
          zIndex: 15,
          background: '#0B213D',
          color: '#fff',
          fontSize: '0.875rem',
          fontWeight: 600
        }}>
          <div style={{
            ...stickyLabelStyle,
            width: LABEL_W,
            minWidth: LABEL_W,
            padding: '0.5rem 0.75rem',
            textAlign: 'right',
            borderLeft: '1px solid rgba(255,255,255,0.15)',
            background: '#0B213D',
          }}>
            {mini ? 'المهمة' : 'المرحلة الرئيسية'}
          </div>

          {columns.map((col) => {
            const colW = col.span * CELL_W

            return (
              <div key={col.start} style={{
                width: colW,
                minWidth: colW,
                textAlign: 'center',
                padding: '0.5rem 0',
                fontSize: '0.875rem',
                lineHeight: 1.5,
                background: !grouped && columnHasToday(col) ? '#9B2226' : 'transparent',
                borderLeft: `1px solid ${grouped ? 'rgba(255,255,255,0.45)' : 'rgba(255,255,255,0.1)'}`,
                overflow: 'hidden',
                whiteSpace: 'nowrap',
                position: 'relative',
              }}>
                {/* الأسبوع الحالي داخل عمود الشهر: شريحة حمراء بعرض أسبوع تحت التسمية، فوق خط اليوم. */}
                {grouped && columnHasToday(col) && (
                  <span aria-hidden="true" style={{
                    position: 'absolute',
                    top: 0,
                    bottom: 0,
                    right: (todayW - col.start) * CELL_W,
                    width: CELL_W,
                    background: '#9B2226',
                  }} />
                )}
                {col.group ? (
                  <span
                    className="meeting-gantt-group-label"
                    title={col.group.title}
                    style={{
                      display: 'block',
                      position: 'relative',
                      paddingInline: '0.375rem',
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                      zIndex: 1,
                    }}
                  >
                    {col.group.label}
                  </span>
                ) : (
                  String(col.start + weekOffset)
                )}
              </div>
            )
          })}
        </div>

        {(mini ? tasksList : msList).map((item: (Milestone | Task) & { color?: string; status?: string }, idx: number) => {
          const color = mini
            ? phaseColor(miniColor, 0)
            : phaseColor(item.color, idx)
          const progress = item.progress || 0
          const ws = item.week_start
          const we = item.week_end
          const isComplete = progress >= 100 || item.status === 'completed'
          const isOverdue = !isComplete && statusWeek > we
          const scheduledNow = !isComplete && statusWeek >= ws && statusWeek <= we
          const isCurrent = item.id === currentItemId || scheduledNow
          const inProgress = scheduledNow || item.status === 'in_progress' || (!isComplete && progress > 0)
          const { label: stateLabel, className: stateClass } = rowState(isComplete, isOverdue, inProgress)

          return (
            <div key={item.id} style={{
              display: 'flex',
              alignItems: mini ? 'flex-start' : 'center',
              background: rowBackground(isCurrent, idx),
              borderBottom: '1px solid #F0F0F0',
              minHeight: '3.5rem',
              boxShadow: isCurrent ? 'inset -4px 0 #143A6C' : undefined,
            }}>
              <div style={{
                ...stickyLabelStyle,
                width: LABEL_W,
                minWidth: LABEL_W,
                padding: mini ? '0.5625rem 0.75rem' : '0.375rem 0.75rem',
                fontSize: '0.875rem',
                fontWeight: 600,
                color: '#1A1A1A',
                textAlign: 'right',
                borderLeft: '1px solid #C9C9C9',
                display: 'flex',
                alignItems: mini ? 'flex-start' : 'center',
                gap: '0.5rem',
                rowGap: '0.25rem',
                flexWrap: 'wrap',
                alignSelf: 'stretch',
                justifyContent: 'flex-end',
                paddingTop: mini ? '0.5625rem' : 0,
                background: rowBackground(isCurrent, idx),
              }}>
                <span className="meeting-gantt-label-text" style={{
                  width: '0.5625rem',
                  height: '0.5625rem',
                  borderRadius: '50%',
                  background: color,
                  flexShrink: 0,
                  marginTop: mini ? '0.3125rem' : 0
                }} />

                <span style={{ minWidth: 0, flex: '1 1 5.625rem' }}>
                  <span style={{
                    display: 'block',
                    whiteSpace: 'normal',
                    wordBreak: 'break-word',
                    lineHeight: 1.5,
                  }}>
                    {item.name}
                  </span>
                  <span style={{ display: 'block', color: '#555555', fontSize: '0.875rem', fontWeight: 500, lineHeight: 1.4 }}>
                    أسبوع {ws + weekOffset} — {we + weekOffset}
                  </span>
                </span>
                <span className={`shrink-0 whitespace-nowrap rounded-full px-2 py-1 text-xs font-bold ${stateClass}`}>
                  {stateLabel}
                </span>
              </div>

              {columns.map((col, ci) => {
                const firstCol = columnOf(Math.max(ws, 1))
                const lastCol = columnOf(Math.min(we, weeks))
                const inBar = firstCol !== -1 && lastCol !== -1 && ci >= firstCol && ci <= lastCol
                const isFirst = ci === firstCol
                const barWeeks = inBar ? columns.slice(firstCol, lastCol + 1).reduce((sum, c) => sum + c.span, 0) : 0
                const isToday = !grouped && columnHasToday(col)

                if (inBar && isFirst) {
                  return (
                    <div key={col.start} style={{
                      width: CELL_W * barWeeks,
                      minWidth: CELL_W * barWeeks,
                      padding: '5px 2px',
                      display: 'flex',
                      alignItems: 'center',
                      alignSelf: 'stretch',
                      position: 'relative',
                      background: isToday ? 'rgba(155,34,38,0.04)' : 'transparent',
                      borderLeft: '1px solid #F0F0F0',
                    }}>
                      <GanttBar
                        name={item.name}
                        color={color}
                        progress={progress}
                        isComplete={isComplete}
                        barWidthPx={barWeeks * CELL_W - 4}
                        canLabelOutside={lastCol < columns.length - 1}
                        scale={scale}
                      />
                    </div>
                  )
                }

                if (inBar) return null

                return (
                  <div key={col.start} style={{
                    width: CELL_W * col.span,
                    minWidth: CELL_W * col.span,
                    alignSelf: 'stretch',
                    background: isToday ? 'rgba(155,34,38,0.04)' : '#fff',
                    borderLeft: `1px solid ${grouped ? '#C9C9C9' : '#F0F0F0'}`
                  }} />
                )
              })}
            </div>
          )
        })}

        {(mini ? tasksList : msList).length === 0 && (
          <div style={{ position: 'relative', zIndex: 12, background: '#fff', textAlign: 'center', padding: '1.5rem 1rem' }}>
            <p style={{ margin: 0, color: '#1A1A1A', fontSize: '0.9375rem', fontWeight: 700 }}>
              {mini ? 'لا توجد مهام لهذه المرحلة' : 'لا توجد مراحل لهذا المشروع بعد'}
            </p>
            <p style={{ margin: '0.375rem 0 0', color: '#555555', fontSize: '0.875rem' }}>
              {mini ? 'تُضاف المهام من صفحة مدير المشروع.' : 'تُضاف المراحل من صفحة مدير المشروع وتظهر هنا فور حفظها.'}
            </p>
          </div>
        )}
      </div>
    </div>
  )
}
