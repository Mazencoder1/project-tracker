import { BidiText, Card } from '@/app/components/ui'
import { phaseColor } from '@/lib/phaseColor'
import type { Milestone, Note } from '../types'

interface PhaseSummaryCardsProps {
  milestones: Milestone[]
  currentMilestone: Milestone | undefined
  detailNotes: Note[]
  supportNotes: Note[]
  overallProgress: number
  /** false في شاشة الاجتماع لأن «الدعم المطلوب من الإدارة العليا» صار قسمًا مستقلًا بارزًا. */
  showSupport?: boolean
}

function NotesList({ notes, empty }: Readonly<{ notes: Note[]; empty: string }>) {
  if (notes.length === 0) return <p className="text-sm text-pc-gray-600">{empty}</p>

  return (
    <ul className="space-y-pc-s text-sm leading-7 text-pc-gray-700">
      {notes.map(note => (
        <li key={note.id} className="flex items-start gap-pc-s">
          <span aria-hidden="true" className="mt-2 h-1.5 w-1.5 shrink-0 rounded-full bg-current" />
          <span>{note.content}</span>
        </li>
      ))}
    </ul>
  )
}

export function PhaseSummaryCards({
  milestones,
  currentMilestone,
  detailNotes,
  supportNotes,
  overallProgress,
  showSupport = true,
}: Readonly<PhaseSummaryCardsProps>) {
  return (
    <div className="min-w-0 space-y-pc-m">
      <Card as="section" padded={false} className="overflow-hidden">
        <h2 className="bg-pc-brand-800 px-pc-m py-pc-s text-base font-bold text-white">تفاصيل المرحلة</h2>
        <div className="p-pc-m">
          {currentMilestone ? (
            <>
              <div className="flex flex-wrap items-start justify-between gap-pc-s">
                <p className="break-words text-sm font-bold text-pc-gray-900">{currentMilestone.name}</p>
                <span className="whitespace-nowrap text-sm font-semibold text-pc-brand-800">
                  {Math.round(currentMilestone.progress || 0)}%
                </span>
              </div>
              <p className="mt-pc-xs text-sm text-pc-gray-600">
                أسبوع {currentMilestone.week_start} — {currentMilestone.week_end}
              </p>
              <div className="mt-pc-m border-t border-pc-gray-100 pt-pc-s">
                <NotesList notes={detailNotes} empty="لا توجد تفاصيل لهذه المرحلة" />
              </div>
            </>
          ) : (
            <p className="text-sm text-pc-gray-600">لا توجد تفاصيل لهذه المرحلة</p>
          )}
        </div>
      </Card>

      {showSupport && (
        <Card as="section" padded={false} className="overflow-hidden">
          <h2 className="bg-pc-warning-600 px-pc-m py-pc-s text-base font-bold text-white">الدعم المطلوب</h2>
          <div className="p-pc-m text-pc-warning-800">
            <NotesList notes={supportNotes} empty="لا يوجد دعم مطلوب" />
          </div>
        </Card>
      )}

      <Card as="section" padded={false} className="overflow-hidden">
        <div className="flex items-center justify-between gap-pc-s bg-pc-brand-800 px-pc-m py-pc-s text-white">
          <h2 className="text-base font-bold">إنجاز المهام الرئيسية</h2>
          <span className="whitespace-nowrap text-sm font-bold">الإجمالي <BidiText direction="ltr">{overallProgress}%</BidiText></span>
        </div>
        <div className="space-y-pc-s p-pc-m">
          {milestones.length > 0 ? milestones.map((milestone, index) => {
            const progress = Math.max(0, Math.min(100, Math.round(milestone.progress || 0)))
            return (
              <div key={milestone.id}>
                <div className="mb-pc-xs flex items-start justify-between gap-pc-s text-sm">
                  <span className="break-words font-semibold text-pc-gray-800">{milestone.name}</span>
                  <span className="shrink-0 font-bold text-pc-gray-700">{progress}%</span>
                </div>
                <div aria-hidden="true" className="h-2 overflow-hidden rounded-full border border-pc-gray-400 bg-white">
                  <div
                    className="h-full rounded-full"
                    style={{ width: `${progress}%`, background: phaseColor(milestone.color, index) }}
                  />
                </div>
                <progress className="sr-only" aria-label={`إنجاز ${milestone.name}`} value={progress} max={100} />
              </div>
            )
          }) : (
            <p className="text-sm text-pc-gray-600">لا توجد مراحل لهذا المشروع بعد</p>
          )}
        </div>
      </Card>
    </div>
  )
}
