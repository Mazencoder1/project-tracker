'use client'
import { useCallback, useEffect, useState } from 'react'
import { ClipboardEdit, ShieldAlert } from 'lucide-react'
import { Alert, BidiText, Button, Card, DateText, Skeleton, useToast } from '@/app/components/ui'
import { ProjectUpdateForm } from '@/app/components/executive/ProjectUpdateForm'
import { RiskForm } from '@/app/components/executive/RiskForm'
import { HealthBadge, StaleBadge } from '@/app/components/executive/StatusBadges'
import { apiJson, errorMessage } from '@/app/components/executive/api'
import { fetchAllPages } from '@/lib/fetchAllPages'
import type { PortfolioProject, PortfolioResponse } from '@/lib/portfolio/types'
import type { Milestone } from '../types'

type Dialog = { kind: 'update' | 'risk'; project: PortfolioProject; milestones: { id: string; name: string }[] } | null

export function PMWeeklyUpdatesPanel({
  projectId,
  refreshKey = 0,
}: Readonly<{
  /** المشروع المحدَّد في أعلى الصفحة — لا يُعرض غيره. */
  projectId?: string | null
  refreshKey?: number
}>) {
  const toast = useToast()
  const [projects, setProjects] = useState<PortfolioProject[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [dialog, setDialog] = useState<Dialog>(null)
  const [opening, setOpening] = useState<string | null>(null)

  const load = useCallback(async () => {
    setError('')
    try {
      const data = await apiJson<PortfolioResponse>('/api/portfolio')
      setProjects(data.projects.filter(p => p.assignedToMe && p.status !== 'cancelled'))
    } catch (e) {
      setError(errorMessage(e, 'تعذّر تحميل حالة التحديثات الأسبوعية.'))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    load()
  }, [load, refreshKey])

  async function open(kind: 'update' | 'risk', project: PortfolioProject) {
    setOpening(`${kind}-${project.id}`)
    try {
      const ms = await fetchAllPages<Milestone>(`/api/milestones?project_id=${project.id}`, 'milestones', 200)
      setDialog({ kind, project, milestones: ms.map(m => ({ id: m.id, name: m.name })) })
    } catch {
      toast.error('تعذّر تحميل مراحل المشروع. أعد المحاولة.')
    } finally {
      setOpening(null)
    }
  }

  // معرّفات UNIQUEIDENTIFIER تعود بأحرف كبيرة من SQL Server — المقارنة بلا حساسية لحالة الأحرف.
  const visible = projectId
    ? projects.filter(p => p.id.toLowerCase() === projectId.toLowerCase())
    : []

  function renderBody() {
    if (error) return (
        <div className="p-pc-m">
          <Alert tone="danger" action={<Button variant="secondary" size="sm" onClick={load}>إعادة المحاولة</Button>}>{error}</Alert>
        </div>
    );
    if (loading) return (
        <output className="flex flex-col gap-pc-s p-pc-m" aria-label="جارٍ التحميل">
          <Skeleton className="h-12 w-full" />
          <Skeleton className="h-12 w-full" />
        </output>
    );
    if (projects.length === 0) return (
        <p className="p-pc-m text-sm text-pc-gray-700">لا توجد مشاريع مُسندة إليك حاليًا.</p>
    );
    if (visible.length === 0) return (
        <p className="p-pc-m text-sm text-pc-gray-700">
          {projectId
            ? 'المشروع المحدَّد غير متاح للتحديث حاليًا (قد يكون ملغى أو غير مُسند إليك).'
            : 'اختر مشروعًا من الأعلى ليظهر تحديثه الأسبوعي ومخاطره.'}
        </p>
    );
    return (
        <ul className="divide-y divide-pc-gray-100">
          {visible.map(p => (
            <li key={p.id} className="flex flex-col gap-pc-s px-pc-m py-pc-s xl:flex-row xl:items-center">
              <div className="min-w-0 flex-1">
                <p className="font-bold text-pc-gray-900"><BidiText>{p.name}</BidiText></p>
                <div className="mt-pc-xs flex flex-wrap items-center gap-pc-s text-sm text-pc-gray-700">
                  <HealthBadge health={p.health} size="sm" />
                  <span>
                    آخر تحديث أسبوعي: {p.lastReportDate ? <DateText value={p.lastReportDate} /> : 'لم يُسجَّل'}
                  </span>
                  {p.isStale && <StaleBadge lastReportDate={p.lastReportDate} />}
                  <span className="inline-flex items-center gap-1">
                    <ShieldAlert aria-hidden="true" className="h-4 w-4" />
                    {p.openRisks === null ? 'لم يتم إدخال بيانات المخاطر' : `مخاطر مفتوحة: ${p.openRisks}`}
                  </span>
                </div>
              </div>
              <div className="flex flex-wrap items-center gap-pc-s">
                <Button
                  variant="secondary"
                  onClick={() => open('update', p)}
                  loading={opening === `update-${p.id}`}
                  icon={<ClipboardEdit aria-hidden="true" className="h-4 w-4" />}
                >
                  التحديث الأسبوعي{' '}<span className="sr-only">لمشروع {p.name}</span>
                </Button>
                <Button variant="secondary" onClick={() => open('risk', p)} loading={opening === `risk-${p.id}`}>
                  تسجيل خطر أو تعثر{' '}<span className="sr-only">لمشروع {p.name}</span>
                </Button>
              </div>
            </li>
          ))}
        </ul>
    );
  }

  return (
    <Card as="section" padded={false} className="mb-pc-m overflow-hidden">
      <div className="flex flex-wrap items-center justify-between gap-pc-s border-b border-pc-gray-100 px-pc-m py-pc-s">
        <div>
          <h2 className="text-base font-bold text-pc-gray-900">التحديث الأسبوعي والمخاطر</h2>
          <p className="text-sm text-pc-gray-600">حدّث حالة المشروع المحدَّد أسبوعيًا حتى تظهر بيانات موثوقة في لوحة الاجتماع.</p>
        </div>
      </div>

      {renderBody()}

      {dialog?.kind === 'update' && (
        <ProjectUpdateForm
          projectId={dialog.project.id}
          projectName={dialog.project.name}
          milestones={dialog.milestones}
          onClose={() => setDialog(null)}
          onSaved={() => {
            setDialog(null)
            load()
          }}
          onRequestRisk={() => setDialog({ ...dialog, kind: 'risk' })}
        />
      )}
      {dialog?.kind === 'risk' && (
        <RiskForm
          projectId={dialog.project.id}
          projectName={dialog.project.name}
          milestones={dialog.milestones}
          onClose={() => setDialog(null)}
          onSaved={() => {
            setDialog(null)
            load()
          }}
        />
      )}
    </Card>
  )
}
