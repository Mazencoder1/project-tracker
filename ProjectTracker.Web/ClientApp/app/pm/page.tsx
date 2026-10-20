'use client'
import { fetchAllPages } from '@/lib/fetchAllPages'
import { useState, useEffect, useRef } from 'react'
import { Check } from 'lucide-react'
import {
  Alert,
  AppShell,
  CardSkeleton,
  ConfirmModal,
  FormRequiredHint,
  Input,
  Modal,
  TextArea,
  useToast,
} from '@/app/components/ui'
import { PHASE_COLOR_OPTIONS, phaseColor, readableTextOn } from '@/lib/phaseColor'
import { PMMilestonesPanel } from './components/PMMilestonesPanel'
import { PMPhaseDetailsPanel } from './components/PMPhaseDetailsPanel'
import { PMProjectOverview } from './components/PMProjectOverview'
import { PMTasksPanel } from './components/PMTasksPanel'
import { PMWeeklyUpdatesPanel } from './components/PMWeeklyUpdatesPanel'
import type { Milestone, Note, PMInfoTab, Project, Task } from './types'

// Note: حقول النوافذ الثلاث انتقلت إلى
// مكوّني Input/TextArea من app/components/ui (تسمية ظاهرة + نجمة إلزامي + نص
// مساعد + رسالة خطأ ملاصقة مربوطة بـ aria-describedby)، ورسالة الخطأ العامة
// صارت Alert. لم يبقَ من كائن cls سوى كلاس زر الترويسة المشترك.

/** وصف نافذة تأكيد موحّدة تحل محل confirm() المتصفّح. */
interface ConfirmState {
  title: string
  message: string
  confirmLabel: string
  onConfirm: () => void | Promise<void>
}

/** رسالة خطأ نموذج المهمة، أو null إن كانت المدخلات صالحة ضمن نطاق المهمة الرئيسية. */
function taskFormProblem(name: string, ws: number, we: number, milestone: Milestone | null | undefined): string | null {
  if (!name.trim()) return 'اسم المهمة مطلوب'
  if (!Number.isInteger(ws) || !Number.isInteger(we)) return 'أدخل أسابيع صحيحة.'
  if (we < ws) return 'أسبوع النهاية يجب أن يكون أكبر من أو يساوي أسبوع البداية'
  if (milestone && (ws < milestone.week_start || we > milestone.week_end)) {
    return `يجب أن تكون المهمة ضمن نطاق المهمة الرئيسية (أسبوع ${milestone.week_start} — ${milestone.week_end})`
  }
  return null
}

export default function PMPage() {
  const toast = useToast()
  const [projects, setProjects] = useState<Project[]>([])
  const [todayLabel, setTodayLabel] = useState('')
  useEffect(() => {
    setTodayLabel(new Date().toLocaleDateString('ar-SA-u-ca-gregory-nu-latn', {
      weekday: 'long',
      year: 'numeric',
      month: 'long',
      day: 'numeric'
    }))
  }, [])
  const [selected, setSelected] = useState<Project | null>(null)
  const [milestones, setMilestones] = useState<Milestone[]>([])
  const [tasks, setTasks] = useState<Task[]>([])
  const [notes, setNotes] = useState<Note[]>([])
  const [activeMilestone, setActiveMilestone] = useState<Milestone | null>(null)
  const [loading, setLoading] = useState(true)
  const latestPidRef = useRef<string | null>(null)

  const [modal, setModal] = useState<'none' | 'addMS' | 'editMS' | 'addTask' | 'editTask' | 'addNote'>('none')
  const [editingMS, setEditingMS] = useState<Milestone | null>(null)
  const [editingTask, setEditingTask] = useState<Task | null>(null)
  const [noteType, setNoteType] = useState<'detail' | 'support'>('detail')
  const [activeInfoTab, setActiveInfoTab] = useState<PMInfoTab>('support')

  const [msForm, setMsForm] = useState({ name: '', week_start: '1', week_end: '4', color: '#143A6C' })
  const [taskForm, setTaskForm] = useState({ name: '', week_start: '1', week_end: '2', progress: 0 })
  const [noteContent, setNoteContent] = useState('')
  const [msg, setMsg] = useState('')
  const [formError, setFormError] = useState('')
  const [confirmState, setConfirmState] = useState<ConfirmState | null>(null)
  const [confirmBusy, setConfirmBusy] = useState(false)
  const [msSaving, setMsSaving] = useState(false)
  const [taskSaving, setTaskSaving] = useState(false)
  const [noteSaving, setNoteSaving] = useState(false)
  const [noteError, setNoteError] = useState('')
  const saveLockRef = useRef(false)

  // الجلب الأولي مقصود مرة واحدة؛ تعاد الاستدعاءات صراحة بعد عمليات الحفظ.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { fetchProjects() }, [])
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { if (selected) fetchData(selected.id) }, [selected])

  async function fetchProjects() {
    try {
      const allProjects = await fetchAllPages<Project>('/api/projects', 'projects', 100)
      // Note: إخفاء المشاريع الملغاة عن مدير المشروع مثل صفحة الاجتماع حتى لا يشتغل عليها بالخطأ.
      // Note: المشاريع الملغاة تبقى محفوظة في الإدارة، لكنها لا تظهر لمدير المشروع في صفحة المتابعة.
      const visibleProjects: Project[] = allProjects.filter((project: Project) => project.status !== 'cancelled')
      setProjects(visibleProjects)
      if (visibleProjects.length > 0) setSelected(visibleProjects[0])
      else setSelected(null)
    } catch {
      setProjects([])
      setSelected(null)
      toast.error('تعذّر تحميل المشاريع. تحقّق من الاتصال ثم أعد تحميل الصفحة.')
    } finally {
      setLoading(false)
    }
  }

  async function fetchData(pid: string) {
    const switching = latestPidRef.current !== pid
    latestPidRef.current = pid
    if (switching) {
      setMilestones([])
      setTasks([])
      setNotes([])
      setActiveMilestone(null)
    }
    try {
      const [msData, tk, nt] = await Promise.all([
        fetchAllPages<Milestone>(`/api/milestones?project_id=${pid}`, 'milestones', 200),
        fetchAllPages<Task>(`/api/tasks?project_id=${pid}`, 'tasks', 300),
        fetchAllPages<Note>(`/api/notes?project_id=${pid}`, 'notes', 300),
      ])
      if (latestPidRef.current !== pid) return
      setMilestones(msData)
      setTasks(tk)
      setNotes(nt)
      setActiveMilestone(prev => {
        if (!prev) return msData[0] || null
        return msData.find(m => m.id === prev.id) || msData[0] || null
      })
    } catch {
      if (latestPidRef.current !== pid) return
      toast.error('تعذّر تحديث بيانات المشروع. البيانات المعروضة قد لا تكون الأحدث — حدّث الصفحة.')
    }
  }

  function showMsg(text: string) {
    setMsg(text)
    toast.success(text)
    setTimeout(() => setMsg(''), 3000)
  }

  /** ينفّذ إجراء نافذة التأكيد ثم يغلقها. */
  async function runConfirm() {
    if (!confirmState) return
    setConfirmBusy(true)
    try {
      await confirmState.onConfirm()
    } finally {
      setConfirmBusy(false)
      setConfirmState(null)
    }
  }

  function openAddMS() {
    setFormError('')
    const totalWeeks = Math.max(1, selected?.total_weeks || 1)
    const nextWeek = Math.min(totalWeeks, Math.max(0, ...milestones.map(item => item.week_end)) + 1)
    setMsForm({ name: '', week_start: String(nextWeek), week_end: String(Math.min(totalWeeks, nextWeek + 3)), color: '#143A6C' })
    setModal('addMS')
  }

  function openEditMS(ms: Milestone) {
    setFormError('')
    setEditingMS(ms)
    setMsForm({ name: ms.name, week_start: String(ms.week_start), week_end: String(ms.week_end), color: phaseColor(ms.color) })
    setModal('editMS')
  }

  async function saveMS() {
    if (msSaving || saveLockRef.current) return
    if (!msForm.name.trim()) { setFormError('اسم المهمة الرئيسية مطلوب'); return }
    const ws = Number(msForm.week_start)
    const we = Number(msForm.week_end)
    const totalWeeks = selected?.total_weeks || 0
    if (!Number.isInteger(ws) || !Number.isInteger(we)) { setFormError('أدخل أسابيع صحيحة.'); return }
    if (ws < 1 || we > totalWeeks) { setFormError(`يجب أن تكون المهمة الرئيسية ضمن مدة المشروع (1 — ${totalWeeks}).`); return }
    if (we < ws) { setFormError('أسبوع النهاية يجب أن يكون أكبر من أو يساوي أسبوع البداية'); return }
    setFormError('')
    const payload = { ...msForm, color: phaseColor(msForm.color), week_start: ws, week_end: we }
    const adding = modal === 'addMS'
    saveLockRef.current = true
    setMsSaving(true)
    try {
      let res: Response | null = null
      if (adding) {
        res = await fetch('/api/milestones', {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ ...payload, project_id: selected?.id })
        })
      } else if (modal === 'editMS' && editingMS) {
        res = await fetch(`/api/milestones/${editingMS.id}`, {
          method: 'PATCH', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        })
      }
      if (!res) return
      if (!res.ok) {
        const data = await res.json().catch(() => null)
        setFormError(data?.error || `تعذّر ${adding ? 'إضافة' : 'تعديل'} المهمة الرئيسية.`)
        return
      }
      showMsg(adding ? 'تمت إضافة المهمة الرئيسية' : 'تم تعديل المهمة الرئيسية')
      setModal('none')
      fetchData(selected!.id)
    } catch {
      toast.error('تعذّر الاتصال بالخادم فلم تُحفظ المهمة الرئيسية. تحقّق من الاتصال ثم اضغط «حفظ» مرة أخرى — بياناتك ما زالت في النموذج.')
    } finally {
      saveLockRef.current = false
      setMsSaving(false)
    }
  }

  function deleteMS(ms: Milestone) {
    setConfirmState({
      title: 'حذف المهمة الرئيسية',
      message: `ستُحذف «${ms.name}» وجميع المهام المرتبطة بها من هذا المشروع.`,
      confirmLabel: 'حذف المهمة الرئيسية',
      onConfirm: async () => {
        const res = await fetch(`/api/milestones/${ms.id}`, { method: 'DELETE' }).catch(() => null)
        if (!res?.ok) {
          const data = await res?.json().catch(() => null)
          toast.error(`تعذّر حذف المهمة الرئيسية. ${data?.error || 'تحقّق من الاتصال ثم أعد المحاولة.'}`)
          return
        }
        if (activeMilestone?.id === ms.id) setActiveMilestone(null)
        showMsg('تم حذف المهمة الرئيسية')
        fetchData(selected!.id)
      },
    })
  }

  function openAddTask() {
    if (!activeMilestone) return
    setFormError('')
    setTaskForm({
      name: '',
      week_start: String(activeMilestone.week_start),
      week_end: String(activeMilestone.week_end),
      progress: 0
    })
    setModal('addTask')
  }

  function openEditTask(task: Task) {
    setFormError('')
    setEditingTask(task)
    setTaskForm({ name: task.name, week_start: String(task.week_start), week_end: String(task.week_end), progress: task.progress })
    setModal('editTask')
  }

  async function saveTask() {
    if (taskSaving || saveLockRef.current) return
    const ws = Number(taskForm.week_start)
    const we = Number(taskForm.week_end)
    const problem = taskFormProblem(taskForm.name, ws, we, activeMilestone)
    if (problem) { setFormError(problem); return }
    setFormError('')
    const payload = { ...taskForm, week_start: ws, week_end: we }
    const adding = modal === 'addTask'
    saveLockRef.current = true
    setTaskSaving(true)
    try {
      let res: Response | null = null
      if (adding) {
        res = await fetch('/api/tasks', {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ ...payload, milestone_id: activeMilestone?.id })
        })
      } else if (modal === 'editTask' && editingTask) {
        res = await fetch(`/api/tasks/${editingTask.id}`, {
          method: 'PATCH', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        })
      }
      if (!res) return
      if (!res.ok) {
        const data = await res.json().catch(() => null)
        setFormError(data?.error || `تعذّر ${adding ? 'إضافة' : 'تعديل'} المهمة.`)
        return
      }
      showMsg(adding ? 'تمت إضافة المهمة' : 'تم تعديل المهمة')
      setModal('none')
      fetchData(selected!.id)
    } catch {
      toast.error('تعذّر الاتصال بالخادم فلم تُحفظ المهمة. تحقّق من الاتصال ثم اضغط «حفظ» مرة أخرى — بياناتك ما زالت في النموذج.')
    } finally {
      saveLockRef.current = false
      setTaskSaving(false)
    }
  }

  function deleteTask(task: Task) {
    setConfirmState({
      title: 'حذف المهمة',
      message: `ستُحذف «${task.name}» من المهمة الرئيسية الحالية.`,
      confirmLabel: 'حذف المهمة',
      onConfirm: async () => {
        const res = await fetch(`/api/tasks/${task.id}`, { method: 'DELETE' }).catch(() => null)
        if (!res?.ok) {
          const data = await res?.json().catch(() => null)
          toast.error(`تعذّر حذف المهمة. ${data?.error || 'تحقّق من الاتصال ثم أعد المحاولة.'}`)
          return
        }
        showMsg('تم حذف المهمة')
        fetchData(selected!.id)
      },
    })
  }

  async function updateProgress(taskId: string, progress: number) {
    try {
      const res = await fetch(`/api/tasks/${taskId}`, {
        method: 'PATCH', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ progress })
      })
      if (!res.ok) {
        toast.error('تعذّر حفظ نسبة الإنجاز الجديدة، وتُعرض الآن آخر نسبة محفوظة. أعد تحريك المؤشر للمحاولة مجددًا.')
      }
    } catch {
      toast.error('تعذّر الاتصال بالخادم فلم تُحفظ نسبة الإنجاز. تحقّق من الاتصال ثم أعد تحريك المؤشر.')
    }
    await fetchData(selected!.id)
  }

  async function addNote() {
    if (noteSaving || saveLockRef.current) return
    if (!noteContent.trim()) { setNoteError('نص الملاحظة مطلوب — اكتب النص ثم اضغط «حفظ».'); return }
    setNoteError('')
    saveLockRef.current = true
    setNoteSaving(true)
    try {
      const res = await fetch('/api/notes', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ milestone_id: activeMilestone?.id, type: noteType, content: noteContent })
      })
      if (!res.ok) {
        toast.error('تعذّر إضافة الملاحظة لأن الخادم رفض الطلب. تأكّد من تحديد مهمة رئيسية ثم اضغط «حفظ» مرة أخرى — النص ما زال في النموذج.')
        return
      }
      setNoteContent(''); setModal('none')
      showMsg('تمت الإضافة')
      fetchData(selected!.id)
    } catch {
      toast.error('تعذّر الاتصال بالخادم فلم تُحفظ الملاحظة. تحقّق من الاتصال ثم اضغط «حفظ» مرة أخرى — النص ما زال في النموذج.')
    } finally {
      saveLockRef.current = false
      setNoteSaving(false)
    }
  }

  function deleteNote(id: string) {
    setConfirmState({
      title: 'حذف الملاحظة',
      message: 'ستُحذف هذه الملاحظة من المهمة الرئيسية الحالية ولن تظهر في تقرير الاجتماع.',
      confirmLabel: 'حذف الملاحظة',
      onConfirm: async () => {
        const res = await fetch(`/api/notes/${id}`, { method: 'DELETE' }).catch(() => null)
        if (!res?.ok) {
          toast.error('تعذّر حذف الملاحظة. تحقّق من الاتصال ثم أعد المحاولة.')
          return
        }
        showMsg('تم حذف الملاحظة')
        fetchData(selected!.id)
      },
    })
  }

  // فتح modal الإضافة مع التحقق من وجود مايلستون محدد
  function openAddNote(type: 'detail' | 'support') {
    if (!activeMilestone) {
      toast.warning('حدّد مهمة رئيسية أولًا من قائمة المهام الرئيسية ثم أعد المحاولة')
      return
    }
    setNoteType(type)
    setNoteContent('')
    setNoteError('')
    setModal('addNote')
  }

  const msTasks = tasks.filter(t => t.milestone_id === activeMilestone?.id)
  const detailNotes = notes.filter(n => n.milestone_id === activeMilestone?.id && n.type === 'detail')
  const supportNotes = notes.filter(n => n.milestone_id === activeMilestone?.id && n.type === 'support')
  // Note: ملخص مدير المشروع يحسب أرقام التشغيل من نفس بيانات الصفحة بدون إضافة API جديد.
  const completedMilestones = milestones.filter(ms => (ms.progress || 0) >= 100).length
  const completedTasks = tasks.filter(task => (task.progress || 0) >= 100).length
  const totalSupportNotes = notes.filter(note => note.type === 'support').length
  const overallProgress = milestones.length > 0
    ? Math.round(milestones.reduce((sum, ms) => sum + (ms.progress || 0), 0) / milestones.length)
    : 0
  const projectStatusLabel: Record<string, string> = {
    active: 'نشط',
    completed: 'مكتمل',
    delayed: 'متأخر',
    cancelled: 'ملغى',
  }

  const pageTitle = 'متابعة تنفيذ المشروع' + (selected ? `: ${selected.name}` : '')

  return (
    <AppShell
      title={pageTitle}
      subtitle={todayLabel || ' '}
    >
    <div>
        {msg && (
          <Alert tone="success" className="mb-pc-m" onDismiss={() => setMsg('')}>
            {msg}
          </Alert>
        )}

        {loading ? (
          <output aria-label="جارٍ تحميل بيانات المشروع" className="block rounded-pc-md border border-pc-gray-200 bg-white p-pc-m shadow-pc-card">
            <CardSkeleton count={3} />
          </output>
        ) : (
          <>
            <PMProjectOverview
              projects={projects}
              selected={selected}
              completedMilestones={completedMilestones}
              milestonesCount={milestones.length}
              completedTasks={completedTasks}
              tasksCount={tasks.length}
              totalSupportNotes={totalSupportNotes}
              overallProgress={overallProgress}
              projectStatusLabel={projectStatusLabel}
              onSelectProject={(project) => {
                setSelected(project)
                setActiveMilestone(null)
              }}
            />

            {selected && <PMWeeklyUpdatesPanel projectId={selected.id} />}

            {selected && (
              <div className="flex flex-col gap-pc-m xl:grid xl:items-start xl:gap-pc-m xl:[direction:ltr] xl:[grid-template-columns:280px_minmax(0,1fr)_300px] 2xl:[grid-template-columns:320px_minmax(0,1fr)_340px]">
                {/* Note: ترتيب لوحة مدير المشروع بثلاث مناطق ظاهرة: الدعم يسار، المهام في الوسط، والمراحل يمين (من lg فأعلى فقط — راجع gridColumn الصريح داخل كل مكوّن). */}
                <PMMilestonesPanel
                  milestones={milestones}
                  activeMilestone={activeMilestone}
                  onAddMilestone={openAddMS}
                  onEditMilestone={openEditMS}
                  onDeleteMilestone={deleteMS}
                  onSelectMilestone={(milestone) => {
                    setActiveMilestone(milestone)
                    setActiveInfoTab('support')
                  }}
                />

                <PMTasksPanel
                  activeMilestone={activeMilestone}
                  tasks={msTasks}
                  onAddTask={openAddTask}
                  onEditTask={openEditTask}
                  onDeleteTask={deleteTask}
                  onUpdateProgress={updateProgress}
                />

                <PMPhaseDetailsPanel
                  milestones={milestones}
                  activeMilestone={activeMilestone}
                  taskCount={msTasks.length}
                  detailNotes={detailNotes}
                  supportNotes={supportNotes}
                  activeInfoTab={activeInfoTab}
                  onSelectMilestone={(milestoneId) => {
                    setActiveMilestone(milestones.find(ms => ms.id === milestoneId) || null)
                    setActiveInfoTab('support')
                  }}
                  onChangeTab={setActiveInfoTab}
                  onAddNote={openAddNote}
                  onDeleteNote={deleteNote}
                />
              </div>
            )}
          </>
        )}

      {/* MODALS */}

      {(modal === 'addMS' || modal === 'editMS') && (
        <Modal
          title={modal === 'addMS' ? 'إضافة مهمة رئيسية' : 'تعديل المهمة الرئيسية'}
          onClose={() => { setModal('none'); setFormError('') }}
          onSave={saveMS}
          saveLoading={msSaving}
        >
          <div className="flex flex-col gap-pc-m">
            <FormRequiredHint />
            {formError && <Alert tone="danger">{formError}</Alert>}
            <Input
              id="ms-name"
              label="اسم المهمة الرئيسية"
              required
              value={msForm.name}
              onChange={e => setMsForm({ ...msForm, name: e.target.value })}
              placeholder="مثال: مرحلة التطوير"
            />
            <div className="grid grid-cols-1 gap-pc-s sm:grid-cols-2">
              <Input
                id="ms-week-start"
                label="أسبوع البداية"
                type="number"
                inputMode="numeric"
                value={msForm.week_start}
                min={1}
                max={selected?.total_weeks || 1}
                onChange={e => setMsForm({ ...msForm, week_start: e.target.value })}
              />
              <Input
                id="ms-week-end"
                label="أسبوع النهاية"
                type="number"
                inputMode="numeric"
                value={msForm.week_end}
                min={1}
                max={selected?.total_weeks || 1}
                onChange={e => setMsForm({ ...msForm, week_end: e.target.value })}
              />
            </div>
            {/* Note: لون المهمة الرئيسية قيمة ديناميكية يختارها المستخدم — استثناء
                موثَّق في إرشادات التصميم (يبقى input[type=color] كما هو، مع تسمية ظاهرة
                ونص مساعد يوضّح أن اللون تمييز بصري مساعد لا ناقل معلومة وحده). */}
            <fieldset className="flex flex-col gap-pc-xs">
              <legend className="mb-pc-xs text-sm font-medium text-pc-gray-800">
                لون المهمة الرئيسية في المخطط
              </legend>
              <div className="flex flex-wrap gap-pc-s">
                {PHASE_COLOR_OPTIONS.map(opt => {
                  const checked = phaseColor(msForm.color) === opt.value
                  return (
                    <label key={opt.value} className="relative inline-flex cursor-pointer">
                      <input
                        type="radio"
                        name="ms-color"
                        value={opt.value}
                        checked={checked}
                        onChange={() => setMsForm({ ...msForm, color: opt.value })}
                        aria-label={opt.label}
                        className="peer sr-only"
                      />
                      <span
                        aria-hidden="true"
                        className="flex h-11 w-11 items-center justify-center rounded-full border-2 border-white ring-2 ring-pc-gray-400 peer-checked:ring-pc-gray-900 peer-focus-visible:outline peer-focus-visible:outline-2 peer-focus-visible:outline-offset-4 peer-focus-visible:outline-pc-brand-600"
                        style={{ background: opt.value, color: readableTextOn(opt.value) }}
                      >
                        {checked && <Check className="h-5 w-5" />}
                      </span>
                    </label>
                  )
                })}
              </div>
            </fieldset>
          </div>
        </Modal>
      )}

      {(modal === 'addTask' || modal === 'editTask') && (
        <Modal
          title={modal === 'addTask' ? 'إضافة مهمة' : 'تعديل المهمة'}
          onClose={() => { setModal('none'); setFormError('') }}
          onSave={saveTask}
          saveLoading={taskSaving}
        >
          <div className="flex flex-col gap-pc-m">
            <FormRequiredHint />
            {formError && <Alert tone="danger">{formError}</Alert>}
            {activeMilestone && (
              <Alert tone="info">
                نطاق المهمة الرئيسية: أسبوع {activeMilestone.week_start} — {activeMilestone.week_end}
              </Alert>
            )}
            <Input
              id="task-name"
              label="اسم المهمة"
              required
              value={taskForm.name}
              onChange={e => setTaskForm({ ...taskForm, name: e.target.value })}
              placeholder="مثال: مراجعة المتطلبات"
            />
            <div className="grid grid-cols-1 gap-pc-s sm:grid-cols-2">
              <Input
                id="task-week-start"
                label="أسبوع البداية"
                type="number"
                inputMode="numeric"
                value={taskForm.week_start}
                min={activeMilestone?.week_start || 1}
                max={activeMilestone?.week_end || 99}
                onChange={e => setTaskForm({ ...taskForm, week_start: e.target.value })}
              />
              <Input
                id="task-week-end"
                label="أسبوع النهاية"
                type="number"
                inputMode="numeric"
                value={taskForm.week_end}
                min={activeMilestone?.week_start || 1}
                max={activeMilestone?.week_end || 99}
                onChange={e => setTaskForm({ ...taskForm, week_end: e.target.value })}
              />
            </div>
            <div className="flex flex-col gap-pc-xs">
              <label htmlFor="task-progress" className="text-sm font-medium text-pc-gray-800">
                نسبة الإنجاز:{' '}
                <strong className="text-pc-brand-700">{taskForm.progress}%</strong>
              </label>
              <input
                id="task-progress"
                type="range"
                min="0"
                max="100"
                step="5"
                value={taskForm.progress}
                aria-valuetext={`${taskForm.progress} بالمئة`}
                onChange={e => setTaskForm({ ...taskForm, progress: +e.target.value })}
                className="h-11 w-full accent-pc-brand-600"
              />
            </div>
          </div>
        </Modal>
      )}

      {modal === 'addNote' && (
        <Modal
          title={noteType === 'detail' ? 'إضافة تفصيل للمرحلة' : 'إضافة دعم مطلوب'}
          onClose={() => { setModal('none'); setNoteError('') }}
          onSave={addNote}
          saveLoading={noteSaving}
        >
          <div className="flex flex-col gap-pc-m">
            {activeMilestone && (
              <Alert tone="info">المهمة الرئيسية: {activeMilestone.name}</Alert>
            )}
            <FormRequiredHint />
            <TextArea
              id="note-content"
              label={noteType === 'detail' ? 'نص تفصيل المرحلة' : 'نص الدعم المطلوب'}
              required
              rows={5}
              value={noteContent}
              onChange={e => { setNoteContent(e.target.value); if (noteError) setNoteError('') }}
              error={noteError || undefined}
              placeholder="أدخل النص هنا…"
              help="اكتب النص بلغة واضحة ومختصرة ليظهر في تقرير الاجتماع."
            />
          </div>
        </Modal>
      )}

      {confirmState && (
        <ConfirmModal
          title={confirmState.title}
          message={confirmState.message}
          confirmLabel={confirmState.confirmLabel}
          loading={confirmBusy}
          onConfirm={runConfirm}
          onClose={() => setConfirmState(null)}
        />
      )}
    </div>
    </AppShell>
  )
}
