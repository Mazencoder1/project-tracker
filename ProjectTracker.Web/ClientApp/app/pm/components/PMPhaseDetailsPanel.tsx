import { Plus, Trash2 } from 'lucide-react'
import { Button, Card, IconButton, Select, handleTabListKeyDown } from '@/app/components/ui'
import { phaseColor } from '@/lib/phaseColor'
import type { Milestone, Note, PMInfoTab } from '../types'

interface PMPhaseDetailsPanelProps {
  milestones: Milestone[]
  activeMilestone: Milestone | null
  taskCount: number
  detailNotes: Note[]
  supportNotes: Note[]
  activeInfoTab: PMInfoTab
  onSelectMilestone: (milestoneId: string) => void
  onChangeTab: (tab: PMInfoTab) => void
  onAddNote: (type: 'detail' | 'support') => void
  onDeleteNote: (noteId: string) => void
}

type TabKind = 'support' | 'details' | 'progress'
const TAB_ACCENT: Record<TabKind, { border: string; bgActive: string; text: string; badgeBg: string; badgeText: string }> = {
  support: { border: 'border-pc-warning-600', bgActive: 'bg-pc-warning-600', text: 'text-pc-warning-600', badgeBg: 'bg-pc-warning-100', badgeText: 'text-pc-warning-800' },
  details: { border: 'border-pc-brand-600', bgActive: 'bg-pc-brand-600', text: 'text-pc-brand-700', badgeBg: 'bg-pc-brand-100', badgeText: 'text-pc-brand-800' },
  progress: { border: 'border-pc-info-600', bgActive: 'bg-pc-info-600', text: 'text-pc-info-600', badgeBg: '', badgeText: '' },
}

function tabButtonClass(active: boolean, kind: TabKind): string {
  const a = TAB_ACCENT[kind]
  return active
    ? `min-h-[2.75rem] rounded-pc-md border px-pc-xs py-pc-s text-xs font-extrabold text-white ${a.border} ${a.bgActive}`
    : `min-h-[2.75rem] rounded-pc-md border border-pc-gray-200 bg-white px-pc-xs py-pc-s text-xs font-extrabold ${a.text}`
}

function tabBadgeClass(active: boolean, kind: 'support' | 'details'): string {
  if (active) return kind === 'support' ? 'bg-white text-pc-warning-800' : 'bg-white text-pc-brand-800'
  const a = TAB_ACCENT[kind]
  return `${a.badgeBg} ${a.badgeText}`
}

const SELECT_ID = 'pm-phase-select'
const PANEL_ID = 'pm-phase-tabpanel'
const tabId = (key: PMInfoTab) => `pm-phase-tab-${key}`
const TAB_ORDER: readonly PMInfoTab[] = ['support', 'details', 'progress']

// Note: لوحة تفاصيل المرحلة تحتوي التبويبات والدعم والإنجاز، لذلك فصلها يقلل حجم page.tsx بدون تغيير المنطق.
export function PMPhaseDetailsPanel({
  milestones,
  activeMilestone,
  taskCount,
  detailNotes,
  supportNotes,
  activeInfoTab,
  onSelectMilestone,
  onChangeTab,
  onAddNote,
  onDeleteNote,
}: Readonly<PMPhaseDetailsPanelProps>) {
  const activeColor = activeMilestone
    ? phaseColor(activeMilestone.color, milestones.findIndex(m => m.id === activeMilestone.id))
    : phaseColor(null, 1)
  return (
    <div
      className="flex flex-col gap-pc-s"
      style={{ gridColumn: '1 / 2', gridRow: '1', direction: 'rtl' }}
    >
      {/* Note: لوحة الدعم والتفاصيل صارت بتبويبات وبطاقات مرقمة مثل صفحة الاجتماع مع إبقاء أزرار الإضافة والحذف لمدير المشروع. */}
      <Card as="section" padded={false} className="overflow-hidden">
        <div className="flex items-center justify-between gap-pc-s border-b border-pc-gray-100 px-pc-m py-pc-s">
          <div>
            <h2 className="text-sm font-extrabold text-pc-gray-800">تفاصيل المرحلة المحددة</h2>
            <p className={`mt-1 text-xs font-bold ${activeMilestone ? 'text-pc-brand-700' : 'text-pc-gray-600'}`}>
              {activeMilestone ? activeMilestone.name : 'حدد مرحلة من القائمة'}
            </p>
          </div>
          {activeMilestone && (
            <span className="whitespace-nowrap rounded-full border border-pc-brand-100 bg-pc-brand-50 px-pc-s py-pc-xs text-xs font-extrabold text-pc-brand-800">
              أسبوع {activeMilestone.week_start} — {activeMilestone.week_end}
            </span>
          )}
        </div>

        {milestones.length > 0 && (
          <div className="border-b border-pc-gray-100 bg-pc-gray-50 p-pc-s">
            <Select
              id={SELECT_ID}
              label="اختيار المرحلة الرئيسية"
              value={activeMilestone?.id || ''}
              onChange={onSelectMilestone}
              placeholder="اختر مرحلة"
              options={milestones.map(ms => ({ value: ms.id, label: ms.name }))}
            />
          </div>
        )}

        <div className="border-b border-pc-gray-100 p-pc-s">
          <div
            role="tablist"
            tabIndex={-1}
            aria-label="أقسام تفاصيل المرحلة"
            onKeyDown={(e) => handleTabListKeyDown(e, TAB_ORDER, activeInfoTab, onChangeTab, tabId)}
            className="grid grid-cols-3 gap-pc-s"
          >
            <button
              type="button"
              role="tab"
              id={tabId('support')}
              aria-selected={activeInfoTab === 'support'}
              aria-controls={PANEL_ID}
              tabIndex={activeInfoTab === 'support' ? 0 : -1}
              onClick={() => onChangeTab('support')}
              className={tabButtonClass(activeInfoTab === 'support', 'support')}
            >
              الدعم{' '}
              <span className={`ms-1 rounded-full px-pc-xs py-1 ${tabBadgeClass(activeInfoTab === 'support', 'support')}`}>
                {supportNotes.length}
              </span>
            </button>
            <button
              type="button"
              role="tab"
              id={tabId('details')}
              aria-selected={activeInfoTab === 'details'}
              aria-controls={PANEL_ID}
              tabIndex={activeInfoTab === 'details' ? 0 : -1}
              onClick={() => onChangeTab('details')}
              className={tabButtonClass(activeInfoTab === 'details', 'details')}
            >
              التفاصيل{' '}
              <span className={`ms-1 rounded-full px-pc-xs py-1 ${tabBadgeClass(activeInfoTab === 'details', 'details')}`}>
                {detailNotes.length}
              </span>
            </button>
            <button
              type="button"
              role="tab"
              id={tabId('progress')}
              aria-selected={activeInfoTab === 'progress'}
              aria-controls={PANEL_ID}
              tabIndex={activeInfoTab === 'progress' ? 0 : -1}
              onClick={() => onChangeTab('progress')}
              className={tabButtonClass(activeInfoTab === 'progress', 'progress')}
            >
              الإنجاز
            </button>
          </div>
        </div>

        <div
          role="tabpanel"
          id={PANEL_ID}
          aria-labelledby={tabId(activeInfoTab)}
          tabIndex={0}
        >
          {(activeInfoTab === 'support' || activeInfoTab === 'details') && (
            <div className="flex justify-end px-pc-s pt-pc-s">
              {/* لا يُعطَّل الزر عند غياب مرحلة محددة: الضغط يعرض Toast يشرح سبب عدم الإضافة
                  (openAddNote في page.tsx) — أوضح من زر معطّل بلا نص سبب. */}
              <Button
                variant="secondary"
                size="sm"
                icon={<Plus aria-hidden="true" className="h-4 w-4" />}
                onClick={() => onAddNote(activeInfoTab === 'support' ? 'support' : 'detail')}
              >
                إضافة {activeInfoTab === 'support' ? 'دعم' : 'تفصيل'}
              </Button>
            </div>
          )}

          <div className="p-pc-s xl:max-h-[26.875rem] xl:overflow-y-auto">
            {activeInfoTab === 'support' && (
              <>
                {supportNotes.length > 0 ? supportNotes.map((n, index) => (
                  <div key={n.id} className="mb-pc-s flex items-start gap-pc-s rounded-pc-md bg-pc-warning-100 p-pc-s text-xs leading-7 text-pc-gray-700">
                    <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-pc-warning-600 font-black text-white">
                      {index + 1}
                    </span>
                    <span className="flex-1">{n.content}</span>
                    <IconButton label="حذف هذه الملاحظة" variant="danger" className="shrink-0" onClick={() => onDeleteNote(n.id)}>
                      <Trash2 aria-hidden="true" className="h-5 w-5" />
                    </IconButton>
                  </div>
                )) : (
                  <p className="py-pc-l text-center text-xs text-pc-gray-600">
                    {activeMilestone ? 'لا يوجد دعم مطلوب' : 'حدد مرحلة رئيسية أولًا'}
                  </p>
                )}
              </>
            )}

            {activeInfoTab === 'details' && (
              <>
                {detailNotes.length > 0 ? detailNotes.map((n, index) => (
                  <div key={n.id} className="mb-pc-s flex items-start gap-pc-s rounded-pc-md bg-pc-brand-50 p-pc-s text-xs leading-7 text-pc-gray-700">
                    <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-pc-brand-600 font-black text-white">
                      {index + 1}
                    </span>
                    <span className="flex-1">{n.content}</span>
                    <IconButton label="حذف هذه الملاحظة" variant="danger" className="shrink-0" onClick={() => onDeleteNote(n.id)}>
                      <Trash2 aria-hidden="true" className="h-5 w-5" />
                    </IconButton>
                  </div>
                )) : (
                  <p className="py-pc-l text-center text-xs text-pc-gray-600">
                    {activeMilestone ? 'لا توجد تفاصيل' : 'حدد مرحلة رئيسية أولًا'}
                  </p>
                )}
              </>
            )}

            {activeInfoTab === 'progress' && (
              <div className="grid grid-cols-2 gap-pc-s">
                <div className="rounded-pc-md border border-pc-gray-100 bg-pc-gray-50 p-pc-s text-center">
                  <div className="mb-pc-xs text-xs text-pc-gray-600">إنجاز المرحلة</div>
                  <div className="text-xl font-black" style={{ color: activeColor }}>
                    {Math.round(activeMilestone?.progress || 0)}%
                  </div>
                </div>
                <div className="rounded-pc-md border border-pc-gray-100 bg-pc-gray-50 p-pc-s text-center">
                  <div className="mb-pc-xs text-xs text-pc-gray-600">عدد المهام</div>
                  <div className="text-xl font-black text-pc-info-600">
                    {taskCount}
                  </div>
                </div>
                <div className="col-span-2 rounded-pc-md border border-pc-gray-100 bg-white p-pc-s">
                  <div className="h-2 overflow-hidden rounded-full border border-pc-gray-400 bg-white">
                    <div
                      className="h-full rounded-full"
                      style={{ width: `${Math.round(activeMilestone?.progress || 0)}%`, background: activeColor }}
                    />
                  </div>
                </div>
              </div>
            )}
          </div>
        </div>
      </Card>
    </div>
  )
}
