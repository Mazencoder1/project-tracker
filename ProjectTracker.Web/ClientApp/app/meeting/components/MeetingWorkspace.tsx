'use client'

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { ArrowRight } from 'lucide-react'
import { Alert, Button, Card, CardSkeleton, EmptyState, SegmentedControl, useToast } from '@/app/components/ui'
import { apiJson, errorMessage } from '@/app/components/executive/api'
import { fetchAllPages } from '@/lib/fetchAllPages'
import { phaseColor } from '@/lib/phaseColor'
import { PROJECT_STATUS_LABEL } from '@/lib/statusStyles'
import { getProjectTimelinePosition } from '@/lib/timeline'
import { supportRequestsFrom, type ExecutiveDashboardResponse, type ExecutiveProject } from '@/lib/portfolio/executive'
import type { ProjectSummaryResponse } from '@/lib/portfolio/types'
import { GanttChart, defaultGanttZoom, type GanttZoom } from './GanttChart'
import { PhaseSummaryCards } from './PhaseSummaryCards'
import { ProjectDashboard } from './ProjectDashboard'
import { ProjectReport } from './reports/ProjectReport'
import { ExecutiveDashboard } from './dashboard/ExecutiveDashboard'
import { useCurrentDate } from '../useCurrentDate'
import type { Assignment, Milestone, Note, Project, Task } from '../types'
import {
  getPlannedProgress,
  getProgressDeviation,
  getDeviationTone,
  getProjectDashboardData,
} from '../utils'

interface ProjectData {
  milestones: Milestone[]
  tasks: Task[]
  notes: Note[]
  assignments: Assignment[]
  /** ملخص المشروع التنفيذي (/api/portfolio/[id]): المخاطر والإجراءات وآخر تحديث أسبوعي، أو null إن تعذّر. */
  summary: ProjectSummaryResponse | null
}

const EMPTY_DATA: ProjectData = { milestones: [], tasks: [], notes: [], assignments: [], summary: null }

function findCurrentMilestone(milestones: Milestone[], elapsedWeek: number, started: boolean) {
  if (milestones.length === 0) return undefined
  if (started) {
    const scheduledNow = milestones.find(
      milestone => elapsedWeek >= milestone.week_start && elapsedWeek <= milestone.week_end
    )
    if (scheduledNow) return scheduledNow
  }
  return milestones.find(milestone => (milestone.progress || 0) < 100) || milestones.at(-1)
}

/** مشروع اللوحة التنفيذية بصيغة Project التي تستخدمها شاشة المتابعة ومكوّناتها. */
async function loadProjectData(projectId: string): Promise<ProjectData> {
  const [milestones, tasks, notes, assignments, summary] = await Promise.all([
    fetchAllPages<Milestone>(`/api/milestones?project_id=${projectId}`, 'milestones', 200),
    fetchAllPages<Task>(`/api/tasks?project_id=${projectId}`, 'tasks', 300),
    fetchAllPages<Note>(`/api/notes?project_id=${projectId}`, 'notes', 300),
    fetchAllPages<Assignment>(`/api/assignments?project_id=${projectId}`, 'assignments', 300),
    // الملخص التنفيذي اختياري: فشله لا يمنع عرض المراحل والمهام.
    apiJson<ProjectSummaryResponse>(`/api/portfolio/${projectId}`).catch(() => null),
  ])
  return { milestones, tasks, notes, assignments, summary }
}

/** Keeps the current selection when it is still listed; otherwise the project named in the URL (if any). */
function pickSelected(visible: Project[], current: Project | null, fromUrl: string | undefined): Project | null {
  if (current && visible.some(project => project.id === current.id)) return visible.find(project => project.id === current.id) ?? current
  if (!fromUrl) return null
  return visible.find(project => project.id.toLowerCase() === fromUrl) ?? null
}

function urlForProject(projectId: string | null) {
  const params = new URLSearchParams(window.location.search)
  if (projectId) params.set('project', projectId)
  else params.delete('project')
  const query = params.toString()
  const search = query ? `?${query}` : ''
  return `${window.location.pathname}${search}`
}

function toProject(p: ExecutiveProject): Project {
  return {
    id: p.id,
    name: p.name,
    status: p.status,
    total_weeks: p.total_weeks ?? 0,
    start_date: p.start_date ?? '',
    end_date: p.end_date ?? '',
    description: p.description ?? undefined,
    project_code: p.project_code,
    department: p.department,
  }
}

/** Loads the milestones, tasks, notes and assignments of the selected project (stale responses are ignored). */
function useProjectData(selected: Project | null, toast: ReturnType<typeof useToast>) {
  const latestProjectRef = useRef<string | null>(null)
  const [data, setData] = useState<ProjectData>(EMPTY_DATA)
  const [loadingData, setLoadingData] = useState(false)
  const [ganttZoom, setGanttZoom] = useState<GanttZoom>('month')

  useEffect(() => {
    if (!selected) {
      latestProjectRef.current = null
      setData(EMPTY_DATA)
      return
    }

    const projectId = selected.id
    latestProjectRef.current = projectId
    setData(EMPTY_DATA)
    setLoadingData(true)
    setGanttZoom(defaultGanttZoom(selected.total_weeks))

    loadProjectData(projectId)
      .then(loaded => {
        if (latestProjectRef.current !== projectId) return
        setData(loaded)
      })
      .catch(() => {
        if (latestProjectRef.current !== projectId) return
        setData(EMPTY_DATA)
        toast.error('تعذّر تحميل بيانات المشروع. اختر المشروع مرة أخرى أو أعد تحميل الصفحة.')
      })
      .finally(() => {
        if (latestProjectRef.current === projectId) setLoadingData(false)
      })
  }, [selected, toast])

  return { data, loadingData, ganttZoom, setGanttZoom }
}

/** Everything the project page derives from the loaded data: progress, timeline position, current phase and its mini-timeline. */
function deriveProjectView(selected: Project, data: ProjectData, today: ReturnType<typeof useCurrentDate>, dashboard: ExecutiveDashboardResponse) {
  const { milestones, tasks, notes, assignments, summary } = data
  const executive = dashboard.projects.find(project => project.id === selected.id)
  const { overallProgress, completedMilestones } = getProjectDashboardData({
    project: selected,
    milestones,
    today,
  })
  const projectWeeks = Math.max(
    selected.total_weeks || 0,
    ...milestones.map(milestone => milestone.week_end || 0)
  )
  const timeline = getProjectTimelinePosition(selected, today, projectWeeks)
  const elapsedWeek = timeline.elapsedWeek ?? 0
  const started = timeline.relation !== 'before' && timeline.relation !== 'unavailable'
  const currentMilestone = findCurrentMilestone(milestones, elapsedWeek, started)
  const currentTasks = currentMilestone
    ? tasks.filter(task => task.milestone_id === currentMilestone.id)
    : []
  const detailNotes = currentMilestone
    ? notes.filter(note => note.milestone_id === currentMilestone.id && note.type === 'detail')
    : []
  const supportNotes = currentMilestone
    ? notes.filter(note => note.milestone_id === currentMilestone.id && note.type === 'support')
    : []
  // الدعم المطلوب: نفس قاعدة اللوحة — التحديث الأسبوعي + الإجراءات المفتوحة + ملاحظات دعم المراحل غير المكتملة.
  const openMilestones = new Map(milestones.filter(m => (m.progress || 0) < 100).map(m => [m.id.toLowerCase(), m.name]))
  const supportRequests = supportRequestsFrom({
    latestUpdate: summary?.latestUpdate ?? null,
    actions: summary?.actions ?? [],
    supportNotes: notes
      .filter(note => note.type === 'support' && openMilestones.has(String(note.milestone_id).toLowerCase()))
      .map(note => ({ id: note.id, content: note.content, milestoneName: openMilestones.get(String(note.milestone_id).toLowerCase()) ?? null })),
  })
  const assignedPmNames = Array.from(new Set(
    assignments.filter(assignment => assignment.role === 'pm').map(assignment => assignment.full_name).filter(Boolean)
  ))
  const plannedProgress = getPlannedProgress(selected, today)
  let deviationTone: ReturnType<typeof getDeviationTone>
  if (timeline.relation === 'after' && selected.status !== 'completed' && overallProgress < 100) deviationTone = 'error'
  else if (selected.status === 'completed' || timeline.relation === 'before' || timeline.relation === 'unavailable') deviationTone = 'success'
  else deviationTone = getDeviationTone(getProgressDeviation(overallProgress, plannedProgress))
  const miniWeeks = currentMilestone
    ? Math.max(1, currentMilestone.week_end - currentMilestone.week_start + 1)
    : 1
  const miniWeekOffset = currentMilestone ? currentMilestone.week_start - 1 : 0
  const miniTodayWeek = currentMilestone && timeline.elapsedWeek !== null
    ? Math.min(miniWeeks, Math.max(1, timeline.elapsedWeek - currentMilestone.week_start + 1))
    : 0
  const miniStatusWeek = currentMilestone && timeline.elapsedWeek !== null
    ? timeline.elapsedWeek - currentMilestone.week_start + 1
    : 0
  const relativeTasks = currentMilestone
    ? currentTasks.map(task => ({
        ...task,
        week_start: task.week_start - currentMilestone.week_start + 1,
        week_end: task.week_end - currentMilestone.week_start + 1,
      }))
    : []
  return {
    milestones, executive, overallProgress, completedMilestones, projectWeeks, timeline, elapsedWeek, currentMilestone, currentTasks, detailNotes, supportNotes, supportRequests, assignedPmNames, deviationTone, miniWeeks, miniWeekOffset, miniTodayWeek, miniStatusWeek, relativeTasks,
  }
}

export function MeetingWorkspace() {
  const toast = useToast()
  const [dashboard, setDashboard] = useState<ExecutiveDashboardResponse | null>(null)
  const [dashboardError, setDashboardError] = useState('')
  const [refreshing, setRefreshing] = useState(false)
  const [selected, setSelected] = useState<Project | null>(null)
  const [loadingDashboard, setLoadingDashboard] = useState(true)
  const { data, loadingData, ganttZoom, setGanttZoom } = useProjectData(selected, toast)
  const today = useCurrentDate()

  const projects = useMemo(() => (dashboard?.projects ?? []).map(toProject), [dashboard])

  const loadDashboard = useCallback(async (initial: boolean) => {
    if (!initial) setRefreshing(true)
    try {
      const response = await apiJson<ExecutiveDashboardResponse>('/api/dashboard/executive')
      setDashboard(response)
      setDashboardError('')
      const visible = response.projects.map(toProject)
      const fromUrl = new URLSearchParams(window.location.search).get('project')?.toLowerCase()
      setSelected(current => pickSelected(visible, current, fromUrl))
      if (!initial) toast.success('تم تحديث بيانات لوحة المؤشرات.')
    } catch (e) {
      setDashboardError(errorMessage(e, 'تعذّر تحميل لوحة المؤشرات.'))
      if (!initial) toast.error('تعذّر تحديث بيانات لوحة المؤشرات. أعد المحاولة.')
    } finally {
      setLoadingDashboard(false)
      setRefreshing(false)
    }
  }, [toast])

  useEffect(() => {
    loadDashboard(true)
  }, [loadDashboard])

  useEffect(() => {
    function syncFromUrl() {
      const id = new URLSearchParams(window.location.search).get('project')?.toLowerCase()
      setSelected(id ? projects.find(project => project.id.toLowerCase() === id) ?? null : null)
    }
    window.addEventListener('popstate', syncFromUrl)
    return () => window.removeEventListener('popstate', syncFromUrl)
  }, [projects])

  function openProjectById(projectId: string) {
    const project = projects.find(item => item.id === projectId)
    if (!project) return
    window.history.pushState({ meetingProjectPushed: true }, '', urlForProject(project.id))
    setSelected(project)
    window.scrollTo({ top: 0 })
  }

  function switchProject(project: Project) {
    window.history.replaceState(window.history.state, '', urlForProject(project.id))
    setSelected(project)
  }

  function backToDashboard() {
    if (window.history.state?.meetingProjectPushed) {
      window.history.back()
      return
    }
    window.history.replaceState(window.history.state, '', urlForProject(null))
    setSelected(null)
  }

  if (loadingDashboard) return <CardSkeleton count={4} />

  if (!dashboard) {
    return (
      <Card>
        <Alert tone="danger" title="تعذّر تحميل لوحة المؤشرات">{dashboardError}</Alert>
        <div className="mt-pc-m">
          <Button onClick={() => { setLoadingDashboard(true); loadDashboard(true) }}>إعادة المحاولة</Button>
        </div>
      </Card>
    )
  }

  if (projects.length === 0) {
    return (
      <Card>
        <EmptyState
          title="لا توجد مشاريع مسجلة حتى الآن"
          description="ستظهر المشاريع ومؤشراتها هنا فور إضافتها من صفحة الإدارة."
        />
      </Card>
    )
  }

  if (!selected) {
    return (
      <ExecutiveDashboard
        data={dashboard}
        onOpenProject={openProjectById}
        onRefresh={() => loadDashboard(false)}
        refreshing={refreshing}
      />
    )
  }

  const view = deriveProjectView(selected, data, today, dashboard)
  const {
    milestones,
    executive,
    overallProgress,
    completedMilestones,
    projectWeeks,
    timeline,
    elapsedWeek,
    currentMilestone,
    currentTasks,
    detailNotes,
    supportNotes,
    supportRequests,
    assignedPmNames,
    deviationTone,
    miniWeeks,
    miniWeekOffset,
    miniTodayWeek,
    miniStatusWeek,
    relativeTasks,
  } = view
  const zoomOptions: { key: GanttZoom; label: string }[] = [
    { key: 'week', label: 'أسبوع' },
    { key: 'month', label: 'شهر' },
    { key: 'quarter', label: 'ربع سنة' },
  ]

  return (
    <div className="meeting-page bg-transparent" dir="rtl">
      <div className="meeting-report-content space-y-pc-m">
        <div className="meeting-no-pdf flex flex-wrap items-center justify-between gap-pc-s">
          <Button variant="secondary" onClick={backToDashboard} icon={<ArrowRight aria-hidden="true" className="h-4 w-4" />}>
            العودة إلى المشاريع
          </Button>
          <p className="text-sm font-semibold text-pc-gray-700">ملخص المشروع والخط الزمني</p>
        </div>

        {loadingData ? (
          <CardSkeleton count={3} />
        ) : (
          <>
            <ProjectReport
              key={selected.id}
              project={selected}
              managerNames={assignedPmNames}
              statusLabel={PROJECT_STATUS_LABEL[selected.status] || selected.status || 'غير متوفر'}
              progress={overallProgress}
              timelineLabel={timeline.label}
              progressAvailable={milestones.length > 0}
              currentWeek={timeline.elapsedWeek}
              markerWeek={timeline.markerWeek}
              milestones={milestones}
              currentMilestone={currentMilestone}
              currentTasks={currentTasks}
              supportRequests={supportRequests}
            />

            <ProjectDashboard
              selected={selected}
              projects={projects}
              onSelectProject={switchProject}
              statusLabel={PROJECT_STATUS_LABEL}
              assignedPmNames={assignedPmNames}
              completedMilestones={completedMilestones}
              milestonesCount={milestones.length}
              actualProgress={overallProgress}
              deviationTone={deviationTone}
              timelineLabel={timeline.label}
              executiveStatus={executive?.execStatus}
              currentMilestoneName={currentMilestone?.name ?? null}
            />

            <Card as="section" padded={false} className="overflow-hidden">
              <div className="flex flex-wrap items-center justify-between gap-pc-s border-b border-pc-gray-100 px-pc-m py-pc-s">
                <div>
                  <h2 className="text-base font-bold text-pc-gray-900">المخطط الزمني للمشروع</h2>
                  <p className="mt-1 text-sm text-pc-gray-600">المراحل الرئيسية خلال مدة المشروع بالأسابيع</p>
                </div>
                <div className="flex flex-wrap items-center gap-pc-s">
                  <SegmentedControl
                    label="مستوى تكبير المخطط"
                    options={zoomOptions}
                    value={ganttZoom}
                    onChange={setGanttZoom}
                    className="meeting-gantt-zoom"
                  />
                  {timeline.markerWeek !== null && (
                    <span className="flex items-center gap-pc-xs text-sm font-semibold text-pc-error-600">
                      <span aria-hidden="true" className="h-0.5 w-6 bg-pc-error-600" />
                      الخط الأحمر — {timeline.label}
                    </span>
                  )}
                </div>
              </div>
              <div className="p-pc-s">
                <GanttChart
                  msList={milestones}
                  tasksList={[]}
                  weeks={projectWeeks}
                  todayW={timeline.markerWeek ?? 0}
                  statusWeek={elapsedWeek}
                  currentItemId={currentMilestone?.id}
                  autoCenterToday
                  zoom={ganttZoom}
                  projectStartDate={selected.start_date}
                />
              </div>
            </Card>

            <div className="grid items-start gap-pc-m lg:grid-cols-[minmax(0,1.65fr)_minmax(18.75rem,0.85fr)]">
              <Card as="section" padded={false} className="min-w-0 overflow-hidden">
                <div className="border-b border-pc-gray-100 px-pc-m py-pc-s">
                  <div className="flex flex-wrap items-center justify-between gap-pc-s">
                    <div className="min-w-0">
                      <h2 className="text-base font-bold text-pc-gray-900">مهام المرحلة الحالية</h2>
                      <p className="mt-1 break-words text-sm font-semibold text-pc-brand-800">
                        {currentMilestone?.name || 'لا توجد مرحلة حالية'}
                      </p>
                    </div>
                    {currentMilestone && (
                      <div className="flex flex-wrap items-center gap-pc-s text-sm text-pc-gray-600">
                        <span>أسبوع {currentMilestone.week_start} — {currentMilestone.week_end}</span>
                        <span className="rounded-full bg-pc-brand-50 px-pc-s py-1 font-bold text-pc-brand-800">
                          {Math.round(currentMilestone.progress || 0)}%
                        </span>
                      </div>
                    )}
                  </div>
                </div>
                <div className="p-pc-s">
                  {currentMilestone ? (
                    <GanttChart
                      msList={[]}
                      tasksList={relativeTasks}
                      weeks={miniWeeks}
                      todayW={miniTodayWeek}
                      statusWeek={miniStatusWeek}
                      weekOffset={miniWeekOffset}
                      miniColor={phaseColor(currentMilestone.color, milestones.indexOf(currentMilestone))}
                      mini
                    />
                  ) : (
                    <EmptyState
                      title="لا توجد مراحل لهذا المشروع بعد"
                      description="تُضاف المراحل من صفحة مدير المشروع وتظهر هنا فور حفظها."
                    />
                  )}
                </div>
              </Card>

              <PhaseSummaryCards
                milestones={milestones}
                currentMilestone={currentMilestone}
                detailNotes={detailNotes}
                supportNotes={supportNotes}
                overallProgress={overallProgress}
                showSupport
              />
            </div>
          </>
        )}
      </div>
    </div>
  )
}
