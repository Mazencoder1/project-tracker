import { Plus } from 'lucide-react'
import { Button, Card, Pagination, usePagination } from '@/app/components/ui'
import { phaseColor } from '@/lib/phaseColor'
import type { Milestone } from '../types'

interface PMMilestonesPanelProps {
  milestones: Milestone[]
  activeMilestone: Milestone | null
  onAddMilestone: () => void
  onEditMilestone: (milestone: Milestone) => void
  onDeleteMilestone: (milestone: Milestone) => void
  onSelectMilestone: (milestone: Milestone) => void
}

// Note: قائمة المراحل مستقلة لأنها عنصر كبير ومتكرر في صفحة مدير المشروع.
export function PMMilestonesPanel({
  milestones,
  activeMilestone,
  onAddMilestone,
  onEditMilestone,
  onDeleteMilestone,
  onSelectMilestone,
}: Readonly<PMMilestonesPanelProps>) {
  const indexed = milestones.map((ms, idx) => ({ ms, idx }))
  const milestonesPage = usePagination(indexed, 8)

  return (
    <Card
      as="section"
      padded={false}
      className="overflow-hidden"
      style={{ gridColumn: '3 / 4', gridRow: '1', direction: 'rtl' }}
    >
      <div className="flex items-center justify-between gap-pc-s border-b border-pc-gray-100 px-pc-m py-pc-s">
        <h2 className="text-base font-bold text-pc-gray-800">المراحل الرئيسية</h2>
        <Button variant="secondary" size="sm" onClick={onAddMilestone} icon={<Plus aria-hidden="true" className="h-4 w-4" />}>
          إضافة{' '}
          <span className="sr-only">مهمة رئيسية</span>
        </Button>
      </div>
      {/* على الجوال تمتد القائمة مع الصفحة؛ التمرير الداخلي محصور بالشاشات الواسعة. */}
      <div className="p-pc-s xl:max-h-[32.5rem] xl:overflow-y-auto xl:[scrollbar-gutter:stable]">
        {milestones.length === 0 ? (
          <p className="p-pc-l text-center text-sm text-pc-gray-600">
            لا توجد مهام رئيسية — اضغط إضافة للبدء
          </p>
        ) : (
          <ul className="flex flex-col gap-pc-s">
            {milestonesPage.visible.map(({ ms, idx }) => {
              const active = activeMilestone?.id === ms.id
              const color = phaseColor(ms.color, idx)
              return (
                <li
                  key={ms.id}
                  className={`flex flex-wrap items-center gap-pc-s rounded-pc-md border p-pc-s transition-colors ${
                    active ? 'bg-pc-brand-50' : 'border-pc-gray-100 bg-pc-gray-50 hover:bg-pc-gray-100'
                  }`}
                  style={active ? { borderColor: color } : undefined}
                >
                  <button
                    type="button"
                    onClick={() => onSelectMilestone(ms)}
                    aria-pressed={active}
                    className="flex min-h-[2.75rem] min-w-0 grow basis-[12.5rem] items-center gap-pc-s rounded-pc-md text-start focus:outline-none focus-visible:ring-2 focus-visible:ring-pc-brand-600"
                  >
                    <span aria-hidden="true" className="h-3 w-3 shrink-0 rounded-full" style={{ background: color }} />
                    <span className="min-w-0 flex-1">
                      <span className="block text-sm font-semibold text-pc-gray-900">{ms.name}</span>
                      <span className="mt-1 block text-xs text-pc-gray-600">أسبوع {ms.week_start} — {ms.week_end}</span>
                    </span>
                    <span className="flex items-center gap-pc-xs">
                      <span aria-hidden="true" className="block h-2 w-16 overflow-hidden rounded-full border border-pc-gray-400 bg-white">
                        <span className="block h-full rounded-full" style={{ width: `${ms.progress || 0}%`, background: color }} />
                      </span>
                      <span className="w-8 text-xs text-pc-gray-600">{Math.round(ms.progress || 0)}%</span>
                    </span>
                  </button>
                  <div className="flex gap-pc-xs">
                    <Button variant="secondary" size="sm" onClick={() => onEditMilestone(ms)}>
                      تعديل{' '}<span className="sr-only">{ms.name}</span>
                    </Button>
                    <Button variant="danger-outline" size="sm" onClick={() => onDeleteMilestone(ms)}>
                      حذف{' '}<span className="sr-only">{ms.name}</span>
                    </Button>
                  </div>
                </li>
              )
            })}
          </ul>
        )}
        <Pagination
          page={milestonesPage.page}
          pageCount={milestonesPage.pageCount}
          total={milestonesPage.total}
          onPageChange={milestonesPage.setPage}
          unit="milestone"
          ariaLabel="تصفّح المراحل الرئيسية"
        />
      </div>
    </Card>
  )
}
