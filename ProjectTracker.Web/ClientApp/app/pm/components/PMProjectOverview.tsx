import { BidiText, Card, DateRange, Select } from '@/app/components/ui'
import { formatCount } from '@/lib/arabicCount'
import type { Project } from '../types'

interface PMProjectOverviewProps {
  projects: Project[]
  selected: Project | null
  completedMilestones: number
  milestonesCount: number
  completedTasks: number
  tasksCount: number
  totalSupportNotes: number
  overallProgress: number
  projectStatusLabel: Record<string, string>
  onSelectProject: (project: Project) => void
}

const STAT_COLORS = ['text-pc-warning-600', 'text-pc-info-600', 'text-pc-brand-600', 'text-pc-warning-600', 'text-pc-brand-700']

// Note: يفصل بوكس اختيار المشروع وملخصه حتى تبقى page.tsx مركزة على البيانات والعمليات.
export function PMProjectOverview({
  projects,
  selected,
  completedMilestones,
  milestonesCount,
  completedTasks,
  tasksCount,
  totalSupportNotes,
  overallProgress,
  projectStatusLabel,
  onSelectProject,
}: Readonly<PMProjectOverviewProps>) {
  if (projects.length === 0) {
    return (
      <Card className="text-center text-sm text-pc-gray-600" bodyClassName="p-pc-xl">
        لا توجد مشاريع نشطة مرتبطة بحسابك
      </Card>
    )
  }

  if (!selected) return null

  const stats = [
    { label: 'نسبة الإنجاز', value: `${overallProgress}%` },
    { label: 'المراحل', value: `${completedMilestones}/${milestonesCount}` },
    { label: 'المهام', value: `${completedTasks}/${tasksCount}` },
    { label: 'الدعم المطلوب', value: totalSupportNotes },
    { label: 'مدة المشروع', value: formatCount(selected.total_weeks || 0, 'week') },
  ]

  return (
    <div
      className="mb-pc-m flex flex-col gap-pc-m xl:grid xl:items-stretch xl:[direction:rtl] xl:[grid-template-columns:minmax(360px,0.95fr)_minmax(520px,1.35fr)]"
    >
      <Card padded={false} className="order-2 overflow-hidden" style={{ direction: 'rtl' }}>
        <div className="flex items-start gap-pc-s px-pc-l pb-pc-s pt-pc-l">
          <div className="min-w-0 text-start">
            <p className="text-xl font-black leading-snug text-pc-gray-900"><BidiText>{selected.name}</BidiText></p>
            <p className="mt-pc-s text-xs text-pc-gray-600">
              {(selected.end_date || selected.start_date) ? <DateRange start={selected.start_date} end={selected.end_date} /> : 'لا توجد تواريخ محددة'}
            </p>
          </div>
        </div>
        <div className="grid grid-cols-2 gap-pc-s px-pc-l pb-pc-l pt-pc-m sm:grid-cols-5">
          {stats.map((item, idx) => (
            <div key={item.label} className="rounded-pc-md border border-pc-gray-100 bg-pc-gray-50 p-pc-s text-center" style={{ minHeight: 70 }}>
              <div className="mb-1 text-xs text-pc-gray-600">{item.label}</div>
              <div className={`text-lg font-black ${STAT_COLORS[idx % STAT_COLORS.length]}`}>{item.value}</div>
            </div>
          ))}
        </div>
      </Card>

      <Card padded={false} className="order-1 overflow-visible p-pc-l" style={{ direction: 'rtl' }}>
        <div className="mb-pc-m flex items-center justify-between gap-pc-s">
          <h2 className="text-lg font-black text-pc-gray-900">اختيار المشروع</h2>
          <span className="rounded-full border border-pc-brand-100 bg-pc-brand-50 px-pc-s py-1 text-xs font-extrabold text-pc-brand-800">
            {formatCount(projects.length, 'project')}
          </span>
        </div>
        <Select
          id="pm-project-select"
          label="المشروع الحالي"
          value={selected.id}
          placeholder="اختر مشروعًا"
          onChange={(id) => {
            const project = projects.find(p => p.id === id)
            if (project) onSelectProject(project)
          }}
          options={projects.map(project => ({
            value: project.id,
            label: `${project.name} — ${projectStatusLabel[project.status] || project.status}`,
          }))}
        />
      </Card>
    </div>
  )
}
