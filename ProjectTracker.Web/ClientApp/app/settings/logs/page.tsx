'use client'

import { useState } from 'react'

import { FolderPlus, Mail, MessageSquareText, Users } from 'lucide-react'
import { Alert, AppShell, Badge, Button, Table, type Column, Tabs, TabPanel } from '@/app/components/ui'
import { useServerList } from '@/lib/useServerList'

const LOG_PAGE_SIZE = 25

interface AuditEntry {
  id: string
  user_id: string | null
  user_name: string | null
  action: string
  entity_type: string
  entity_id: string | null
  project_id: string | null
  details: string | null
  created_at: string
}

function parseDetails(entry: AuditEntry): Record<string, unknown> {
  if (!entry.details) return {}
  try {
    const value = JSON.parse(entry.details)
    return value && typeof value === 'object' ? value as Record<string, unknown> : {}
  } catch {
    return {}
  }
}

const dateTimeFormatter = new Intl.DateTimeFormat('ar-SA-u-ca-gregory-nu-latn', {
  year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit',
})

function formatDateTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return dateTimeFormatter.format(date).replace(/[‎‏؜]/g, '')
}

function DateTimeCell({ value }: Readonly<{ value: string }>) {
  return (
    <time dateTime={value} dir="ltr" className="inline-block whitespace-nowrap text-start [unicode-bidi:isolate]">
      {formatDateTime(value)}
    </time>
  )
}

function StatusBadge({ ok }: { ok: boolean }) {
  return ok ? <Badge tone="success">نجح</Badge> : <Badge tone="danger">فشل</Badge>
}

type TabId = 'email' | 'sms' | 'projects' | 'assignments'

const TABS: { id: TabId; label: string; icon: React.ReactNode; query: string }[] = [
  { id: 'email', label: 'البريد الإلكتروني', icon: <Mail className="h-4 w-4" />, query: 'entity_type=email_notification' },
  { id: 'sms', label: 'الرسائل النصية', icon: <MessageSquareText className="h-4 w-4" />, query: 'entity_type=sms_notification' },
  { id: 'projects', label: 'إنشاء المشاريع', icon: <FolderPlus className="h-4 w-4" />, query: 'entity_type=project&action=create' },
  { id: 'assignments', label: 'إسناد المشاريع', icon: <Users className="h-4 w-4" />, query: 'entity_type=assignment' },
]

const emailColumns: Column<AuditEntry>[] = [
  { key: 'time', header: 'وقت الإرسال', cell: e => <DateTimeCell value={e.created_at} /> },
  { key: 'to', header: 'المستلم', cell: e => (Array.isArray(parseDetails(e).to) ? (parseDetails(e).to as string[]).join('، ') : '—') },
  { key: 'subject', header: 'الموضوع', cell: e => (parseDetails(e).subject as string) || '—' },
  { key: 'status', header: 'الحالة', cell: e => <StatusBadge ok={e.action === 'send'} /> },
  { key: 'error', header: 'سبب الفشل', cell: e => (parseDetails(e).error as string) || '—' },
]

const smsColumns: Column<AuditEntry>[] = [
  { key: 'time', header: 'وقت الإرسال', cell: e => <DateTimeCell value={e.created_at} /> },
  { key: 'mobile', header: 'رقم الجوال', cell: e => <span dir="ltr">{(parseDetails(e).mobile as string) || '—'}</span> },
  { key: 'status', header: 'الحالة', cell: e => <StatusBadge ok={e.action === 'send'} /> },
  { key: 'error', header: 'سبب الفشل', cell: e => (parseDetails(e).error as string) || '—' },
]

const projectColumns: Column<AuditEntry>[] = [
  { key: 'time', header: 'التاريخ', cell: e => <DateTimeCell value={e.created_at} /> },
  { key: 'name', header: 'اسم المشروع', cell: e => (parseDetails(e).name as string) || '—' },
  { key: 'by', header: 'أنشأه', cell: e => e.user_name || '—' },
]

const assignmentColumns: Column<AuditEntry>[] = [
  { key: 'time', header: 'التاريخ', cell: e => <DateTimeCell value={e.created_at} /> },
  { key: 'action', header: 'الإجراء', cell: e => <Badge tone={e.action === 'create' ? 'info' : 'neutral'}>{e.action === 'create' ? 'إسناد' : 'إلغاء إسناد'}</Badge> },
  { key: 'project', header: 'المشروع', cell: e => (parseDetails(e).project_name as string) || '—' },
  { key: 'employee', header: 'الموظف', cell: e => (parseDetails(e).user_name as string) || '—' },
  { key: 'by', header: 'بواسطة', cell: e => e.user_name || '—' },
]

const columnsByTab: Record<TabId, Column<AuditEntry>[]> = {
  email: emailColumns, sms: smsColumns, projects: projectColumns, assignments: assignmentColumns,
}

export default function SystemLogsPage() {
  const [active, setActive] = useState<TabId>('email')

  const captionByTab: Record<TabId, string> = {
    email: 'سجل إرسال البريد الإلكتروني', sms: 'سجل إرسال الرسائل النصية',
    projects: 'سجل إنشاء المشاريع', assignments: 'سجل إسناد المشاريع',
  }

  return (
    <AppShell
      title="سجلات النظام"
      subtitle="تتبّع إرسال البريد الإلكتروني والرسائل النصية وإنشاء المشاريع وإسنادها. متاح لمدير النظام فقط."
      breadcrumbs={[{ label: 'الإعدادات', href: '/settings' }, { label: 'سجلات النظام' }]}
    >
      <div className="flex flex-col gap-pc-m">
        <Tabs items={TABS.map(t => ({ id: t.id, label: t.label, icon: t.icon }))} active={active} onChange={id => setActive(id as TabId)} ariaLabel="أنواع السجلات" />

        {TABS.map(tab => (
          <TabPanel key={tab.id} id={tab.id} active={active === tab.id}>
            {/* يُركَّب جدول التبويب النشط وحده، فلا يُطلب سجلّ تبويب لم يُفتح. */}
            {active === tab.id && (
              <LogTable query={tab.query} caption={captionByTab[tab.id]} columns={columnsByTab[tab.id]} />
            )}
          </TabPanel>
        ))}
      </div>
    </AppShell>
  )
}

function LogTable({ query, caption, columns }: Readonly<{ query: string; caption: string; columns: Column<AuditEntry>[] }>) {
  const list = useServerList<AuditEntry>(`/api/audit-log?${query}`, 'entries', LOG_PAGE_SIZE)
  const forbidden = list.errorStatus === 403

  if (list.error) {
    return (
      <Alert
        tone="danger"
        title={forbidden ? 'هذه الصفحة لمدير النظام فقط' : 'تعذّر تحميل السجلات'}
        action={!forbidden && <Button variant="secondary" onClick={list.reload}>إعادة المحاولة</Button>}
      >
        {forbidden ? 'لا تملك صلاحية عرض سجلات النظام.' : list.error}
      </Alert>
    )
  }

  return (
    <Table<AuditEntry>
      caption={caption}
      columns={columns}
      rows={list.rows}
      rowKey={r => r.id}
      loading={list.loading}
      emptyTitle="لا توجد سجلات بعد"
      emptyDescription="ستظهر هنا العمليات فور حدوثها."
      cardTitleKey={columns[0]?.key}
      server={{
        page: list.page,
        pageSize: list.pageSize,
        total: list.total,
        onPageChange: list.setPage,
        refreshing: list.refreshing,
      }}
    />
  )
}
