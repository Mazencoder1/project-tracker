'use client'

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { FolderOpen, RefreshCw, Search, X } from 'lucide-react'
import { BidiText, Button, Card, DateText, Input, Pagination, usePagination } from '@/app/components/ui'
import { formatCount } from '@/lib/arabicCount'
import { EXECUTIVE_STATUS_LABEL, type ExecutiveDashboardResponse, type ExecutiveProject, type ExecutiveStatus } from '@/lib/portfolio/executive'
import { ExecutiveReport } from '../reports/ExecutiveReport'
import { ExecStatusBadge, ProgressCell } from './ExecStatusBadge'
import { NO_DEPARTMENT, QUICK_FILTER_TITLE } from './filters'
import { PortfolioSnapshotCharts, type StatusFilter } from './PortfolioSnapshotCharts'

function deadlineLabel(project: ExecutiveProject) {
  if (project.execStatus === 'completed') return 'مكتمل'
  if (project.daysToEnd === null) return 'الموعد غير محدد'
  if (project.daysToEnd < 0) return `متجاوز بـ ${formatCount(Math.abs(project.daysToEnd), 'day')}`
  if (project.daysToEnd === 0) return 'ينتهي اليوم'
  return `متبقٍ ${formatCount(project.daysToEnd, 'day')}`
}

function deadlineTone(project: ExecutiveProject) {
  if (project.daysToEnd !== null && project.daysToEnd < 0) return 'text-pc-error-600'
  if (project.nearDeadline) return 'text-pc-warning-800'
  return 'text-pc-gray-600'
}

/** شارة مرشح نشط مع زر إزالته. */
function FilterChip({ label, onClear }: Readonly<{ label: string; onClear: () => void }>) {
  return (
    <span className="inline-flex items-center gap-pc-xs rounded-full border border-pc-brand-600 bg-pc-brand-25 py-1 pe-1 ps-pc-s text-sm font-bold text-pc-brand-900">
      <BidiText>{label}</BidiText>
      <button
        type="button"
        onClick={onClear}
        aria-label={`إزالة المرشح: ${label}`}
        className="flex h-6 w-6 items-center justify-center rounded-full text-pc-brand-900 hover:bg-pc-brand-100 focus:outline-none focus-visible:ring-2 focus-visible:ring-pc-brand-700"
      >
        <X aria-hidden="true" className="h-3.5 w-3.5" />
      </button>
    </span>
  )
}

export function ExecutiveDashboard({
  data,
  onOpenProject,
  onRefresh,
  refreshing,
}: Readonly<{
  data: ExecutiveDashboardResponse
  onOpenProject: (projectId: string) => void
  onRefresh: () => void
  refreshing: boolean
}>) {
  const [query, setQuery] = useState('')
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all')
  const [departmentFilter, setDepartmentFilter] = useState('')
  const projectsListRef = useRef<HTMLDivElement>(null)
  const firstRenderRef = useRef(true)
  const generatedAt = new Date(data.generatedAt).toLocaleString('ar-SA-u-ca-gregory-nu-latn', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).replace(/[‎‏؜]/g, '')
  const departmentLabel = departmentFilter === NO_DEPARTMENT ? 'إدارة غير محددة' : departmentFilter
  const hasFilters = statusFilter !== 'all' || departmentFilter !== ''
  const visibleProjects = useMemo(() => {
    const normalized = query.trim().toLocaleLowerCase('ar')
    return data.projects.filter(project => {
      if (statusFilter !== 'all' && project.execStatus !== statusFilter) return false
      if (departmentFilter !== '') {
        const department = project.department?.trim() ?? ''
        if (departmentFilter === NO_DEPARTMENT ? department !== '' : department !== departmentFilter) return false
      }
      if (!normalized) return true
      return [
        project.name,
        project.project_code ?? '',
        project.department ?? '',
        ...project.managers,
      ].some(value => value.toLocaleLowerCase('ar').includes(normalized))
    })
  }, [data.projects, query, statusFilter, departmentFilter])

  const toggleStatus = useCallback((status: ExecutiveStatus) => {
    setStatusFilter(current => current === status ? 'all' : status)
  }, [])
  const toggleDepartment = useCallback((departmentKey: string) => {
    setDepartmentFilter(current => current === departmentKey ? '' : departmentKey)
  }, [])
  useEffect(() => {
    if (firstRenderRef.current) {
      firstRenderRef.current = false
      return
    }
    if (statusFilter === 'all' && departmentFilter === '') return
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
    projectsListRef.current?.scrollIntoView({ behavior: reduceMotion ? 'auto' : 'smooth', block: 'start' })
  }, [statusFilter, departmentFilter])

  const clearFilters = () => {
    setStatusFilter('all')
    setDepartmentFilter('')
  }

  const projectsPage = usePagination(visibleProjects, 10)

  return (
    <div className="space-y-pc-m" dir="rtl">
      <div className="flex flex-wrap items-center justify-between gap-pc-s">
        <div>
          <h2 className="text-lg font-extrabold text-pc-gray-900">ملخص المشاريع</h2>
          <p className="mt-1 text-sm text-pc-gray-600">
            آخر تحديث: <span className="font-semibold tabular-nums text-pc-gray-800">{generatedAt}</span>
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-pc-s">
          <Button
            variant="secondary"
            onClick={onRefresh}
            loading={refreshing}
            loadingLabel="جارٍ التحديث…"
            icon={<RefreshCw aria-hidden="true" className="h-4 w-4" />}
          >
            تحديث
          </Button>
          <ExecutiveReport data={data} />
        </div>
      </div>

      <PortfolioSnapshotCharts
        data={data}
        statusFilter={statusFilter}
        onStatusFilter={toggleStatus}
        departmentFilter={departmentFilter}
        onDepartmentFilter={toggleDepartment}
      />

      <div ref={projectsListRef} className="scroll-mt-pc-l">
      <Card as="section" padded={false} className="overflow-hidden">
        <div className="flex flex-wrap items-end justify-between gap-pc-s border-b border-pc-gray-100 px-pc-m py-pc-s">
          <div className="min-w-0">
            <h2 className="text-base font-bold text-pc-gray-900">
              {statusFilter === 'all' ? 'المشاريع' : QUICK_FILTER_TITLE[statusFilter]}
            </h2>
            <p className="mt-1 text-sm text-pc-gray-600">
              {hasFilters
                ? `${formatCount(visibleProjects.length, 'project')} من ${formatCount(data.projects.length, 'project')} — اضغط على أي صف لفتح المشروع`
                : 'اضغط على أي صف لعرض ملخص المشروع وخطه الزمني'}
            </p>
          </div>
          {data.projects.length > 7 && (
            <div className="w-full sm:w-80">
              <Input
                id="executive-project-search"
                label="البحث عن مشروع"
                type="search"
                value={query}
                onChange={event => setQuery(event.target.value)}
                endAdornment={<Search aria-hidden="true" className="h-4 w-4 text-pc-gray-600" />}
              />
            </div>
          )}
        </div>

        {hasFilters && (
          <div className="flex flex-wrap items-center gap-pc-s border-b border-pc-gray-100 bg-pc-gray-25 px-pc-m py-pc-s">
            <span className="text-sm font-semibold text-pc-gray-700">المرشحات النشطة:</span>
            {statusFilter !== 'all' && (
              <FilterChip label={`الحالة: ${EXECUTIVE_STATUS_LABEL[statusFilter]}`} onClear={() => setStatusFilter('all')} />
            )}
            {departmentFilter !== '' && (
              <FilterChip label={`الإدارة: ${departmentLabel}`} onClear={() => setDepartmentFilter('')} />
            )}
            <Button variant="ghost" size="sm" onClick={clearFilters}>إزالة المرشحات</Button>
          </div>
        )}

        {visibleProjects.length > 0 ? (
          <ul className="divide-y divide-pc-gray-100">
            {projectsPage.visible.map(project => (
              <li
                key={project.id}
                className="relative grid cursor-pointer gap-pc-s px-pc-m py-pc-m transition-colors hover:bg-pc-brand-600/10 focus-within:bg-pc-brand-600/10 lg:grid-cols-[minmax(0,1.35fr)_minmax(11.25rem,0.7fr)_minmax(11.25rem,0.65fr)_auto] lg:items-center"
              >
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-pc-s">
                    <p className="break-words font-bold text-pc-gray-900"><BidiText>{project.name}</BidiText></p>
                    <ExecStatusBadge status={project.execStatus} />
                  </div>
                  <p className="mt-1 text-sm text-pc-gray-600">
                    {project.managers.length > 0 ? project.managers.join('، ') : 'مسؤول المشروع غير محدد'}
                    {project.department ? ` · ${project.department}` : ''}
                  </p>
                </div>

                <div className="text-sm">
                  <p className="text-pc-gray-600">المرحلة الحالية</p>
                  <p className="mt-1 break-words font-semibold text-pc-gray-900">
                    <BidiText>{project.currentMilestoneName ?? 'لا توجد مرحلة مسجلة'}</BidiText>
                  </p>
                </div>

                <div className="min-w-0">
                  <ProgressCell value={project.actualProgress} label={project.name} />
                  <p className={`mt-2 text-sm font-semibold ${deadlineTone(project)}`}>
                    {deadlineLabel(project)}
                    {project.end_date && <span className="font-normal"> · <DateText value={project.end_date} /></span>}
                  </p>
                </div>

                <Button
                  variant="secondary"
                  onClick={() => onOpenProject(project.id)}
                  className="static after:absolute after:inset-0"
                  aria-label={`فتح المشروع: ${project.name}`}
                  icon={<FolderOpen aria-hidden="true" className="h-4 w-4" />}
                >
                  فتح المشروع
                </Button>
              </li>
            ))}
          </ul>
        ) : (
          <div className="flex flex-wrap items-center gap-pc-s p-pc-m">
            <p className="text-sm text-pc-gray-600">
              {hasFilters ? 'لا توجد مشاريع مطابقة للمرشحات الحالية.' : 'لا توجد مشاريع تطابق البحث.'}
            </p>
            {hasFilters && <Button variant="secondary" size="sm" onClick={clearFilters}>عرض كل المشاريع</Button>}
          </div>
        )}
        {projectsPage.paginated && (
          <div className="border-t border-pc-gray-100 px-pc-m py-pc-s">
            <Pagination
              page={projectsPage.page}
              pageCount={projectsPage.pageCount}
              total={projectsPage.total}
              onPageChange={projectsPage.setPage}
              unit="project"
              ariaLabel="تصفّح قائمة المشاريع التنفيذية"
              className="mt-0"
            />
          </div>
        )}
      </Card>
      </div>
    </div>
  )
}
