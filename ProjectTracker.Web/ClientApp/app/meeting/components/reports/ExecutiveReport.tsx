'use client'

import type { ReactNode } from 'react'
import { BidiText } from '@/app/components/ui'
import { formatCount } from '@/lib/arabicCount'
import {
  EXECUTIVE_STATUS_LABEL,
  EXECUTIVE_STATUS_ORDER,
  type ExecutiveDashboardResponse,
  type ExecutiveKpis,
  type ExecutiveProject,
  type ExecutiveStatus,
} from '@/lib/portfolio/executive'
import { ReportControls } from './ReportControls'
import { ReportPage, reportFileDate } from './ReportPage'

const ROWS_PER_PAGE = 8

const COUNT_KEY: Record<ExecutiveStatus, keyof ExecutiveKpis> = {
  on_track: 'onTrack',
  needs_attention: 'needsAttention',
  delayed: 'delayed',
  stalled: 'stalled',
  completed: 'completed',
  unknown: 'unknown',
}

interface PagePart {
  key: string
  sectionTitle: string
  content: ReactNode
}

function chunks<T>(items: T[], size: number): T[][] {
  const result: T[][] = []
  for (let index = 0; index < items.length; index += size) result.push(items.slice(index, index + size))
  return result
}

function formatDate(iso: string | null) {
  if (!iso) return 'غير محدد'
  const [y, m, d] = iso.split('-')
  return y && m && d ? `${d}/${m}/${y}` : 'غير محدد'
}

const LRM = '‎'

function summaryLines(data: ExecutiveDashboardResponse): string[] {
  const k = data.kpis
  const unknownNote = k.unknown > 0 ? `، بلا بيانات كافية ${k.unknown}` : ''
  const lines = [
    `إجمالي المشاريع ${formatCount(k.total, 'project')}: مكتملة ${k.completed}، ضمن الخطة ${k.onTrack}، تحتاج متابعة ${k.needsAttention}، متأخرة ${k.delayed}، متعثرة ${k.stalled}${unknownNote}.`,
    k.averageProgress === null
      ? 'متوسط نسبة الإنجاز غير متوفر لعدم تسجيل نسب إنجاز.'
      : `متوسط نسبة الإنجاز ${LRM}${k.averageProgress}% (محسوب من ${formatCount(k.progressAvailable, 'project')} تتوفر نسبة إنجازها).`,
    k.scheduleCompliance === null
      ? 'نسبة الالتزام بالجدول الزمني غير متوفرة لعدم كفاية البيانات.'
      : `نسبة الالتزام بالجدول الزمني ${LRM}${k.scheduleCompliance}% (المشاريع غير المكتملة: ${k.compliant} من ${k.complianceEligible}).`,
    `يحتاج ${formatCount(k.needsSupport, 'project')} إلى دعم أو قرار، وتنتهي مدة ${formatCount(k.nearDeadline, 'project')} خلال ${data.settings.nearDeadlineDays} يومًا، وإجمالي المهام المتأخرة ${k.delayedTasks}.`,
  ]
  const top = data.projects.filter(p => p.attentionScore !== null).slice(0, 3)
  if (top.length > 0) {
    const topList = top.map(p => p.name + ' (' + p.attentionReasons[0] + ')').join('؛ ')
    lines.push(`أبرز ما يستدعي الانتباه: ${topList}.`)
  }
  return lines
}

function KpiGrid({ data }: Readonly<{ data: ExecutiveDashboardResponse }>) {
  const k = data.kpis
  const cells: [string, string][] = [
    ['إجمالي المشاريع', String(k.total)],
    ['المشاريع المكتملة', String(k.completed)],
    ['ضمن الخطة', String(k.onTrack)],
    ['المشاريع المتأخرة', String(k.delayed)],
    ['المشاريع المتعثرة', String(k.stalled)],
    ['تحتاج دعمًا', String(k.needsSupport)],
    ['متوسط نسبة الإنجاز', k.averageProgress === null ? 'غير متوفر' : `${k.averageProgress}%`],
    ['الالتزام بالجدول الزمني', k.scheduleCompliance === null ? 'غير متوفر' : `${k.scheduleCompliance}%`],
    ['المهام المتأخرة', String(k.delayedTasks)],
    ['قريبة من موعد الانتهاء', String(k.nearDeadline)],
  ]
  return (
    <div className="grid grid-cols-5 gap-pc-s">
      {cells.map(([label, value]) => (
        <div key={label} className="rounded-pc-md border-2 border-pc-gray-400 bg-white px-pc-s py-2 text-center">
          <p className="text-2xl font-black tabular-nums text-pc-brand-900">{value}</p>
          <p className="text-sm font-semibold text-pc-gray-700">{label}</p>
        </div>
      ))}
    </div>
  )
}

function StatusDistribution({ kpis }: Readonly<{ kpis: ExecutiveKpis }>) {
  const barColor: Record<ExecutiveStatus, string> = {
    on_track: 'bg-pc-brand-500',
    needs_attention: 'bg-pc-warning-600',
    delayed: 'bg-pc-error-600',
    stalled: 'bg-pc-error-800',
    completed: 'bg-pc-brand-800',
    unknown: 'bg-pc-gray-500',
  }
  return (
    <div className="rounded-pc-md border-2 border-pc-gray-400">
      {EXECUTIVE_STATUS_ORDER.map((status, index) => {
        const count = kpis[COUNT_KEY[status]] as number
        const percent = kpis.total > 0 ? Math.round((count / kpis.total) * 100) : 0
        return (
          <div key={status} className={`grid items-center gap-pc-s px-pc-s py-1.5 text-sm first:rounded-t-pc-md last:rounded-b-pc-md ${index % 2 === 0 ? 'bg-white' : 'bg-pc-gray-50'}`} style={{ gridTemplateColumns: '160px 70px minmax(0,1fr)' }}>
            <span className="font-bold text-pc-gray-900">{EXECUTIVE_STATUS_LABEL[status]}</span>
            <span className="tabular-nums text-pc-gray-800">{count} · {percent}%</span>
            <span className="block h-4 overflow-hidden rounded-full bg-pc-gray-100">
              <span className={`block h-full ${barColor[status]}`} style={{ width: `${percent}%` }} />
            </span>
          </div>
        )
      })}
    </div>
  )
}

function ProjectsTable({ rows, detailHeader, detail }: Readonly<{ rows: ExecutiveProject[]; detailHeader: string; detail: (p: Readonly<ExecutiveProject>) => string }>) {
  return (
    <table className="w-full table-fixed border-collapse text-sm">
      <thead>
        <tr className="bg-pc-brand-900 text-white">
          <th className="w-[24%] border border-pc-gray-400 px-pc-s py-2 text-start">اسم المشروع</th>
          <th className="w-[15%] border border-pc-gray-400 px-pc-s py-2 text-start">مسؤول المشروع</th>
          <th className="w-[9%] border border-pc-gray-400 px-pc-s py-2 text-start">الإنجاز</th>
          <th className="w-[11%] border border-pc-gray-400 px-pc-s py-2 text-start">الحالة</th>
          <th className="w-[11%] border border-pc-gray-400 px-pc-s py-2 text-start">الموعد النهائي</th>
          <th className="border border-pc-gray-400 px-pc-s py-2 text-start">{detailHeader}</th>
        </tr>
      </thead>
      <tbody>
        {rows.length === 0 ? (
          <tr>
            <td colSpan={6} className="border border-pc-gray-400 px-pc-s py-pc-m text-center font-semibold text-pc-gray-700">لا توجد مشاريع في هذا القسم.</td>
          </tr>
        ) : rows.map((p, index) => (
          <tr key={p.id} className={index % 2 === 0 ? 'bg-white' : 'bg-pc-gray-50'}>
            <td className="border border-pc-gray-400 px-pc-s py-1.5 font-bold"><span className="break-words"><BidiText>{p.name}</BidiText></span></td>
            <td className="border border-pc-gray-400 px-pc-s py-1.5"><span className="break-words">{p.managers.join('، ') || 'غير محدد'}</span></td>
            <td className="border border-pc-gray-400 px-pc-s py-1.5 tabular-nums">{p.actualProgress === null ? 'غير متوفر' : `${p.actualProgress}%`}</td>
            <td className="border border-pc-gray-400 px-pc-s py-1.5">{EXECUTIVE_STATUS_LABEL[p.execStatus]}</td>
            <td className="border border-pc-gray-400 px-pc-s py-1.5 tabular-nums">{formatDate(p.end_date)}</td>
            <td className="border border-pc-gray-400 px-pc-s py-1.5"><span className="break-words"><BidiText>{detail(p)}</BidiText></span></td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

function partSuffix(index: number, total: number): string {
  return total > 1 ? ` — الجزء ${index + 1} من ${total}` : ''
}

function sectionParts(key: string, title: string, rows: ExecutiveProject[], detailHeader: string, detail: (p: ExecutiveProject) => string): PagePart[] {
  const pages = chunks(rows, ROWS_PER_PAGE)
  return (pages.length > 0 ? pages : [[]]).map((pageRows, index, all) => ({
    key: `${key}-${index}`,
    sectionTitle: `${title} (${rows.length})${partSuffix(index, all.length)}`,
    content: <ProjectsTable rows={pageRows} detailHeader={detailHeader} detail={detail} />,
  }))
}

function ExecutiveReportPages({ data, preparedAt }: Readonly<{ data: ExecutiveDashboardResponse; preparedAt: Date }>) {
  const projects = data.projects
  const parts: PagePart[] = [
    {
      key: 'summary',
      sectionTitle: 'الملخص التنفيذي',
      content: (
        <div className="space-y-pc-s">
          <ul className="space-y-1 rounded-pc-md border-2 border-pc-gray-400 bg-pc-gray-50 px-pc-m py-pc-s text-sm leading-6 text-pc-gray-900">
            {summaryLines(data).map(line => (
              <li key={line} className="flex items-start gap-pc-xs">
                <span aria-hidden="true" className="mt-2 h-1.5 w-1.5 shrink-0 rounded-full bg-pc-brand-800" />
                <span><BidiText>{line}</BidiText></span>
              </li>
            ))}
          </ul>
          <KpiGrid data={data} />
          <div>
            <h3 className="mb-1 text-base font-black text-pc-brand-900">توزيع المشاريع حسب الحالة</h3>
            <StatusDistribution kpis={data.kpis} />
          </div>
        </div>
      ),
    },
    ...sectionParts('delayed', 'المشاريع المتأخرة', projects.filter(p => p.execStatus === 'delayed'), 'سبب التأخر', p => p.attentionReasons[0] ?? 'غير متوفر'),
    ...sectionParts('stalled', 'المشاريع المتعثرة', projects.filter(p => p.execStatus === 'stalled'), 'سبب التعثر', p => p.attentionReasons[0] ?? 'غير متوفر'),
    ...sectionParts('support', 'المشاريع التي تحتاج دعمًا', projects.filter(p => p.needsSupport), 'الدعم المطلوب', p =>
      (p.supportRequests[0]?.description ?? 'غير متوفر') + (p.supportRequests.length > 1 ? ` (وطلبات أخرى: ${p.supportRequests.length - 1})` : '')),
    ...sectionParts('near', 'المشاريع القريبة من موعد الانتهاء', projects.filter(p => p.nearDeadline), 'المتبقي على الموعد', p =>
      p.daysToEnd === null ? 'غير متوفر' : formatCount(p.daysToEnd, 'day')),
    ...sectionParts('progress', 'نسب إنجاز جميع المشاريع', [...projects].sort((a, b) => (b.actualProgress ?? -1) - (a.actualProgress ?? -1)), 'المهام المتأخرة', p =>
      String(p.delayedTasks)),
  ]

  return (
    <>
      {parts.map((part, index) => (
        <ReportPage
          key={part.key}
          title="التقرير التنفيذي لمتابعة المشاريع"
          preparedAt={preparedAt}
          page={index + 1}
          totalPages={parts.length}
          sectionTitle={part.sectionTitle}
        >
          {part.content}
        </ReportPage>
      ))}
    </>
  )
}

export function ExecutiveReport({ data }: Readonly<{ data: ExecutiveDashboardResponse }>) {
  return (
    <ReportControls label="تقرير تنفيذي PDF" fileName={preparedAt => `تقرير-تنفيذي-متابعة-المشاريع-${reportFileDate(preparedAt)}`}>
      {preparedAt => <ExecutiveReportPages data={data} preparedAt={preparedAt} />}
    </ReportControls>
  )
}
