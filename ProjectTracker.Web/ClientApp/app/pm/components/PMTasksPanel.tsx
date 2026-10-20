import { useRef, useState } from 'react'
import { Plus } from 'lucide-react'
import { Button, Card, Pagination, usePagination } from '@/app/components/ui'
import { phaseColor } from '@/lib/phaseColor'
import type { Milestone, Task } from '../types'

interface PMTasksPanelProps {
  activeMilestone: Milestone | null
  tasks: Task[]
  onAddTask: () => void
  onEditTask: (task: Task) => void
  onDeleteTask: (task: Task) => void
  onUpdateProgress: (taskId: string, progress: number) => Promise<void> | void
}

function taskStatusText(progress: number): string {
  if (progress === 100) return 'مكتملة'
  if (progress > 0) return 'جارية'
  return 'لم تبدأ'
}

// حالة إنجاز المهمة كشارة (badge) — نفس مطابقة الألوان الدلالية في إرشادات التصميم
function progressBadgeClass(progress: number): string {
  if (progress === 100) return 'bg-pc-success-100 text-pc-success-800'
  if (progress > 0) return 'bg-pc-warning-100 text-pc-warning-800'
  return 'bg-pc-gray-100 text-pc-gray-600'
}

// Note: لوحة مهام المرحلة مفصولة لأنها تعتمد على props فقط وتختصر جزءًا كبيرًا من الصفحة.
export function PMTasksPanel({
  activeMilestone,
  tasks,
  onAddTask,
  onEditTask,
  onDeleteTask,
  onUpdateProgress,
}: Readonly<PMTasksPanelProps>) {
  const [drafts, setDrafts] = useState<Record<string, number>>({})
  const savingRef = useRef(new Set<string>())

  const tasksPage = usePagination(tasks, 8)

  async function commitProgress(taskId: string, saved: number) {
    const value = drafts[taskId]
    if (value === undefined || savingRef.current.has(taskId)) return
    if (value === saved) {
      setDrafts(current => {
        const next = { ...current }
        delete next[taskId]
        return next
      })
      return
    }
    savingRef.current.add(taskId)
    try {
      await onUpdateProgress(taskId, value)
    } finally {
      savingRef.current.delete(taskId)
      setDrafts(current => {
        const next = { ...current }
        delete next[taskId]
        return next
      })
    }
  }

  if (!activeMilestone) return null
  const color = phaseColor(activeMilestone.color)

  return (
    <Card
      as="section"
      padded={false}
      className="overflow-hidden"
      style={{ gridColumn: '2 / 3', gridRow: '1 / span 2', direction: 'rtl' }}
    >
      <div className="flex flex-wrap items-center justify-between gap-pc-s border-b border-pc-gray-100 px-pc-m py-pc-s">
        <div className="flex min-w-0 flex-wrap items-center gap-pc-s">
          <span aria-hidden="true" className="h-3 w-3 rounded-full" style={{ background: color }} />
          <h2 className="text-sm font-semibold text-pc-gray-800">مهام: {activeMilestone.name}</h2>
          <span className="text-xs text-pc-gray-600">(أسبوع {activeMilestone.week_start} — {activeMilestone.week_end})</span>
        </div>
        <Button variant="secondary" size="sm" onClick={onAddTask} icon={<Plus aria-hidden="true" className="h-4 w-4" />}>إضافة مهمة</Button>
      </div>
      {/* على الجوال تعتمد اللوحة تمرير الصفحة الطبيعي لتفادي مناطق التمرير المتداخلة. */}
      <div className="p-pc-s xl:max-h-[38.75rem] xl:overflow-y-auto xl:[scrollbar-gutter:stable]">
        {tasks.length === 0 ? (
          <p className="p-pc-l text-center text-sm text-pc-gray-600">
            لا توجد مهام لهذه المرحلة
          </p>
        ) : (
          <ul className="flex flex-col gap-pc-s">
            {tasksPage.visible.map(task => (
              <li key={task.id} className="rounded-pc-md border border-pc-gray-100 bg-pc-gray-50 p-pc-s">
                <div className="mb-pc-s flex flex-wrap items-center justify-between gap-pc-s">
                  <div className="min-w-0">
                    <p className="text-sm font-medium text-pc-gray-900">{task.name}</p>
                    <p className="mt-1 text-xs text-pc-gray-600">أسبوع {task.week_start} — {task.week_end}</p>
                  </div>
                  <div className="flex flex-wrap items-center gap-pc-xs">
                    <span className={`rounded-full px-pc-s py-1 text-pc-caption font-medium ${progressBadgeClass(task.progress)}`}>
                      {taskStatusText(task.progress)}
                    </span>
                    <Button variant="secondary" size="sm" onClick={() => onEditTask(task)}>
                      تعديل{' '}<span className="sr-only">{task.name}</span>
                    </Button>
                    <Button variant="danger-outline" size="sm" onClick={() => onDeleteTask(task)}>
                      حذف{' '}<span className="sr-only">{task.name}</span>
                    </Button>
                  </div>
                </div>
                <div className="flex items-center gap-pc-s">
                  <input
                    type="range" min="0" max="100" value={drafts[task.id] ?? task.progress}
                    onChange={e => {
                      const value = +e.target.value
                      setDrafts(current => ({ ...current, [task.id]: value }))
                    }}
                    onPointerUp={() => commitProgress(task.id, task.progress)}
                    onKeyUp={() => commitProgress(task.id, task.progress)}
                    onBlur={() => commitProgress(task.id, task.progress)}
                    aria-label={`نسبة إنجاز ${task.name}`}
                    aria-valuetext={`${drafts[task.id] ?? task.progress} بالمئة`}
                    className="h-11 flex-1"
                    style={{ accentColor: color }}
                  />
                  <span className="w-10 text-xs font-semibold tabular-nums text-pc-gray-800">{drafts[task.id] ?? task.progress}%</span>
                </div>
              </li>
            ))}
          </ul>
        )}
        <Pagination
          page={tasksPage.page}
          pageCount={tasksPage.pageCount}
          total={tasksPage.total}
          onPageChange={tasksPage.setPage}
          unit="task"
          ariaLabel="تصفّح مهام المرحلة"
        />
      </div>
    </Card>
  )
}
