'use client'

import { useMemo } from 'react'
import { Card } from '@/app/components/ui'
import { ApexChart, chartPalette } from '@/app/components/charts/ApexChart'
import { formatCount } from '@/lib/arabicCount'
import {
  EXECUTIVE_STATUS_LABEL,
  EXECUTIVE_STATUS_ORDER,
  type ExecutiveDashboardResponse,
  type ExecutiveStatus,
} from '@/lib/portfolio/executive'
import { EXEC_STATUS_STYLE } from './ExecStatusBadge'
import { NO_DEPARTMENT } from './filters'

/** 'all' = بلا فلتر حالة. */
export type StatusFilter = ExecutiveStatus | 'all'

const COUNT_KEY: Record<ExecutiveStatus, keyof ExecutiveDashboardResponse['kpis']> = {
  on_track: 'onTrack',
  needs_attention: 'needsAttention',
  delayed: 'delayed',
  stalled: 'stalled',
  completed: 'completed',
  unknown: 'unknown',
}

function shorten(value: string, max = 18) {
  return value.length > max ? `${value.slice(0, max - 1)}…` : value
}

const CIRCLE_RADIUS = 42
const CIRCLE_CIRCUMFERENCE = 2 * Math.PI * CIRCLE_RADIUS

/** نُقلت من ExecutiveDashboard لتقف بجوار دونات التوزيع في الصف العلوي نفسه. */
function AverageProgressCircle({ value }: Readonly<{ value: number | null }>) {
  const progress = value === null ? 0 : Math.min(100, Math.max(0, value))

  return (
    <div className="relative h-32 w-32 shrink-0">
      <progress
        className="sr-only"
        aria-label="متوسط إنجاز المشاريع"
        aria-valuetext={value === null ? 'غير متوفر' : `${value}%`}
        value={value === null ? undefined : progress}
        max={100}
      />
      <svg aria-hidden="true" viewBox="0 0 100 100" className="h-full w-full -rotate-90">
        <circle cx="50" cy="50" r={CIRCLE_RADIUS} fill="none" stroke="currentColor" strokeWidth="8" className="text-pc-gray-100" />
        <circle
          cx="50"
          cy="50"
          r={CIRCLE_RADIUS}
          fill="none"
          stroke="currentColor"
          strokeWidth="8"
          strokeLinecap="round"
          strokeDasharray={CIRCLE_CIRCUMFERENCE}
          strokeDashoffset={CIRCLE_CIRCUMFERENCE * (1 - progress / 100)}
          className="text-pc-brand-600"
        />
      </svg>
      <span className="absolute inset-0 flex items-center justify-center text-3xl font-black tabular-nums text-pc-gray-900">
        {value === null ? '—' : `${value}%`}
      </span>
    </div>
  )
}

/**
 * بند مفتاح قابل للضغط — النقطة بلون الجزء نفسه في الرسم، والنص يذكر التسمية والعدد.
 * الضغط يفلتر القائمة أسفل الصفحة على مشاريع البند، والضغط على البند المحدد يلغي الفلتر (الأب يتولى التبديل).
 */
function LegendButton({
  color,
  label,
  valueText,
  pressed,
  ariaLabel,
  onClick,
}: Readonly<{
  color: string
  label: string
  valueText: string
  pressed: boolean
  ariaLabel: string
  onClick: () => void
}>) {
  return (
    <button
      type="button"
      aria-pressed={pressed}
      aria-label={ariaLabel}
      onClick={onClick}
      className={`flex min-h-[2.5rem] w-full items-center justify-between gap-pc-s rounded-pc-md border px-pc-s text-sm focus:outline-none focus-visible:ring-2 focus-visible:ring-pc-brand-700 ${
        pressed ? 'bg-pc-brand-25 font-bold' : 'border-transparent hover:bg-pc-gray-50'
      }`}
      style={pressed ? { borderColor: color } : undefined}
    >
      <span className="flex min-w-0 items-center gap-pc-s">
        <span aria-hidden="true" className="h-3 w-3 shrink-0 rounded-full" style={{ background: color }} />
        <span className="truncate text-pc-gray-900">{label}</span>
      </span>
      <span className="shrink-0 tabular-nums text-pc-gray-700">{valueText}</span>
    </button>
  )
}

/**
 * مؤشرا المحفظة المطلوبان فقط؛ كل جزء/عمود يفلتر قائمة المشاريع أسفل الصفحة فتبقى مشاريعه وحدها.
 * المكوّن يبلّغ عن العنصر المضغوط فقط، والأب يقرر التفعيل أو الإلغاء بتحديث دالي —
 * بذلك لا يحتاج معالج ApexCharts (المسجَّل مرة عند إنشاء الرسم) أن يقرأ حالة قد تكون قديمة.
 */
export function PortfolioSnapshotCharts({
  data,
  statusFilter,
  onStatusFilter,
  departmentFilter,
  onDepartmentFilter,
}: Readonly<{
  data: ExecutiveDashboardResponse
  statusFilter: StatusFilter
  onStatusFilter: (status: ExecutiveStatus) => void
  /** '' = كل الإدارات، NO_DEPARTMENT = «إدارة غير محددة». */
  departmentFilter: string
  onDepartmentFilter: (departmentKey: string) => void
}>) {
  const statusChart = useMemo(() => {
    const palette = chartPalette()
    const statuses = EXECUTIVE_STATUS_ORDER.filter(status => Number(data.kpis[COUNT_KEY[status]]) > 0)
    const values = statuses.map(status => Number(data.kpis[COUNT_KEY[status]]))
    const colors = statuses.map(status => palette[EXEC_STATUS_STYLE[status].chart])
    // الجزء غير المحدد يبهت عند تفعيل فلتر حالة، فيبقى الجزء المختار وحده بلونه.
    const sliceColors = statuses.map((status, index) =>
      statusFilter === 'all' || statusFilter === status ? colors[index] : palette.grayLight)
    return {
      statuses,
      values,
      colors,
      options: {
        labels: statuses.map(status => EXECUTIVE_STATUS_LABEL[status]),
        colors: sliceColors,
        stroke: { width: 2, colors: ['#FFFFFF'] },
        legend: { show: false },
        chart: {
          events: {
            dataPointSelection: (_event: unknown, _chart: unknown, config?: { dataPointIndex?: number }) => {
              const status = statuses[config?.dataPointIndex ?? -1]
              if (status) onStatusFilter(status)
            },
          },
        },
        dataLabels: {
          enabled: true,
          // سطران كالمرجع الذي أرسله المستخدم: اسم الحالة أعلى، والعدد تحته.
          // إرجاع مصفوفة من formatter هو ما يرسمه ApexCharts سطرًا لكل عنصر.
          formatter: (_value: string | number | number[], opts?: { seriesIndex?: number }) => {
            const index = opts?.seriesIndex ?? -1
            const status = statuses[index]
            if (!status) return ''
            return [EXECUTIVE_STATUS_LABEL[status], formatCount(values[index], 'project')]
          },
          // ⚠ ApexCharts يعكس الخاصيتين عند تفعيل background: `style.colors` تصير لون **الصندوق**
          // و`background.foreColor` يصير لون **النص**. أول تنفيذ وضعهما بالمنطق البديهي فخرجت
          // الفقاعة سوداء بنص أبيض. الترتيب أدناه هو الصحيح: صندوق أبيض ونص داكن.
          style: { fontSize: '12px', fontWeight: 700, colors: ['#FFFFFF'] },
          background: {
            enabled: true,
            foreColor: palette.text,
            padding: 6,
            borderRadius: 8,
            borderWidth: 1,
            borderColor: palette.gray,
            opacity: 1,
            dropShadow: { enabled: true, top: 2, left: 0, blur: 5, color: '#000000', opacity: 0.22 },
          },
          dropShadow: { enabled: false },
        },
        plotOptions: {
          pie: {
            expandOnClick: false,
            // بُعد الفقاعة عن مركز الحلقة. زِد الرقم لتبتعد أكثر، وأنقصه إن لامست حافة البطاقة.
            dataLabels: { offset: 46 },
            // تصغير الحلقة داخل الإطار ليبقى هامش كافٍ للفقاعات الخارجية بلا قصّ.
            customScale: 0.7,
            donut: {
              size: '58%',
              labels: {
                show: true,
                name: { fontSize: '16px' },
                value: { fontSize: '22px' },
                total: {
                  show: true,
                  showAlways: true,
                  fontSize: '16px',
                  label: 'إجمالي المشاريع',
                  color: palette.muted,
                  formatter: () => String(data.kpis.total),
                },
              },
            },
          },
        },
        tooltip: { y: { formatter: (value: number) => formatCount(value, 'project') } },
      },
    }
  }, [data.kpis, statusFilter, onStatusFilter])

  const statusSummary = statusChart.statuses
    .map((status, index) => EXECUTIVE_STATUS_LABEL[status] + ' ' + statusChart.values[index])
    .join('، ')

  const departmentChart = useMemo(() => {
    const palette = chartPalette()
    const counts = new Map<string, { label: string; count: number }>()
    for (const project of data.projects) {
      const trimmed = project.department?.trim() ?? ''
      const key = trimmed || NO_DEPARTMENT
      const entry = counts.get(key)
      if (entry) entry.count += 1
      else counts.set(key, { label: trimmed || 'إدارة غير محددة', count: 1 })
    }
    const departments = [...counts.entries()]
      .map(([key, value]) => ({ key, label: value.label, count: value.count }))
      .sort((a, b) => b.count - a.count || a.label.localeCompare(b.label, 'ar'))
      .slice(0, 8)
    const maxCount = Math.max(1, ...departments.map(item => item.count))
    const dimmed = departments.map(item => departmentFilter !== '' && departmentFilter !== item.key)
    const columnColors = departments.map((_item, index) => dimmed[index] ? palette.brandSoft : palette.brand)
    return {
      departments,
      columnColors,
      options: {
        colors: columnColors,
        chart: {
          toolbar: { show: false },
          events: {
            dataPointSelection: (_event: unknown, _chart: unknown, config?: { dataPointIndex?: number }) => {
              const department = departments[config?.dataPointIndex ?? -1]
              if (department) onDepartmentFilter(department.key)
            },
          },
        },
        // distributed: لون مستقل لكل عمود حتى يبهت غير المحدد عند تفعيل فلتر إدارة.
        plotOptions: { bar: { horizontal: false, distributed: true, borderRadius: 4, columnWidth: departments.length <= 3 ? '32%' : '56%' } },
        dataLabels: {
          enabled: true,
          style: { fontWeight: 700, fontSize: '13px', colors: departments.map((_item, index) => dimmed[index] ? palette.text : '#FFFFFF') },
        },
        legend: { show: false },
        xaxis: {
          categories: departments.map(item => shorten(item.label)),
          labels: { style: { colors: palette.text, fontSize: '12px' }, trim: true, hideOverlappingLabels: false, rotateAlways: false },
          axisTicks: { show: false },
        },
        yaxis: {
          min: 0,
          tickAmount: Math.min(5, maxCount),
          labels: { formatter: (value: number) => String(Math.round(Number(value))), style: { colors: palette.muted } },
        },
        grid: { xaxis: { lines: { show: false } }, yaxis: { lines: { show: true } } },
        tooltip: { y: { formatter: (value: number) => formatCount(value, 'project') } },
      },
    }
  }, [data.projects, departmentFilter, onDepartmentFilter])

  const departmentSummary = departmentChart.departments.map(item => item.label + ' ' + item.count).join('، ')

  return (
    <section aria-label="مؤشرات توزيع المشاريع" className="space-y-pc-s">
      <div className="grid gap-pc-s lg:grid-cols-2">
        <Card as="article" padded={false} className="overflow-hidden">
          <div className="border-b border-pc-gray-100 px-pc-m py-pc-s">
            <h2 className="text-base font-bold text-pc-gray-900">توزيع المشاريع حسب الحالة</h2>
            <p className="mt-1 text-sm text-pc-gray-600">اضغط على أي حالة لعرض مشاريعها وحدها في القائمة أسفل الصفحة</p>
          </div>
          {data.kpis.total === 0 ? (
            <p className="p-pc-m text-sm text-pc-gray-600">لا توجد مشاريع لعرض توزيعها.</p>
          ) : (
            <>
              <div className="p-pc-s">
                <ApexChart
                  type="donut"
                  series={statusChart.values}
                  options={statusChart.options}
                  height={260}
                  ariaLabel={`توزيع المشاريع حسب الحالة: ${statusSummary}`}
                />
              </div>
              <ul className="grid gap-pc-xs border-t border-pc-gray-100 p-pc-s sm:grid-cols-2">
                {statusChart.statuses.map((status, index) => {
                  const count = statusChart.values[index]
                  const percent = Math.round((count / data.kpis.total) * 100)
                  const pressed = statusFilter === status
                  return (
                    <li key={status}>
                      <LegendButton
                        color={statusChart.colors[index]}
                        label={EXECUTIVE_STATUS_LABEL[status]}
                        valueText={`${count} · ${percent}%`}
                        pressed={pressed}
                        ariaLabel={`تصفية حسب الحالة: ${EXECUTIVE_STATUS_LABEL[status]} (${formatCount(count, 'project')})`}
                        onClick={() => onStatusFilter(status)}
                      />
                    </li>
                  )
                })}
              </ul>
            </>
          )}
        </Card>

        <Card as="article" padded={false} className="overflow-hidden">
          <div className="border-b border-pc-gray-100 px-pc-m py-pc-s">
            <h2 className="text-base font-bold text-pc-gray-900">متوسط إنجاز المشاريع</h2>
            <p className="mt-1 text-sm text-pc-gray-600">
              {data.kpis.averageProgress === null ? 'لا توجد نسب إنجاز مسجلة' : `محسوب من ${formatCount(data.kpis.progressAvailable, 'project')}`}
            </p>
          </div>
          <div className="flex min-h-[12.5rem] items-center justify-center p-pc-m">
            <AverageProgressCircle value={data.kpis.averageProgress} />
          </div>
        </Card>
      </div>

      <Card as="article" padded={false} className="overflow-hidden">
        <div className="border-b border-pc-gray-100 px-pc-m py-pc-s">
          <h2 className="text-base font-bold text-pc-gray-900">حجم المشاريع بين الإدارات</h2>
          <p className="mt-1 text-sm text-pc-gray-600">اضغط على أي إدارة لعرض مشاريعها وحدها في القائمة — أعلى 8 إدارات</p>
        </div>
        {departmentChart.departments.length === 0 ? (
          <p className="p-pc-m text-sm text-pc-gray-600">لا توجد إدارات مسجلة للمشاريع.</p>
        ) : (
          <>
            <div className="p-pc-s">
              <ApexChart
                type="bar"
                series={[{ name: 'عدد المشاريع', data: departmentChart.departments.map(item => item.count) }]}
                options={departmentChart.options}
                height={220}
                ariaLabel={`حجم المشاريع بين الإدارات: ${departmentSummary}`}
              />
            </div>
            <ul className="grid gap-pc-xs border-t border-pc-gray-100 p-pc-s sm:grid-cols-2 lg:grid-cols-4">
              {departmentChart.departments.map((department, index) => {
                const pressed = departmentFilter === department.key
                return (
                  <li key={department.key}>
                    <LegendButton
                      color={departmentChart.columnColors[index]}
                      label={department.label}
                      valueText={formatCount(department.count, 'project')}
                      pressed={pressed}
                      ariaLabel={`تصفية حسب الإدارة: ${department.label} (${formatCount(department.count, 'project')})`}
                      onClick={() => onDepartmentFilter(department.key)}
                    />
                  </li>
                )
              })}
            </ul>
          </>
        )}
      </Card>
    </section>
  )
}
