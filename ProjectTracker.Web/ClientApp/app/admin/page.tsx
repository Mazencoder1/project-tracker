'use client'
import { fetchAllPages } from '@/lib/fetchAllPages'
import { listQuery, useDebouncedValue, useServerList } from '@/lib/useServerList'
import { useState, useEffect, useId, useRef, type ReactNode } from 'react'
import { Ban, MoreHorizontal, Pencil, RotateCcw, Search, SlidersHorizontal, Trash2, UserPlus, type LucideIcon } from 'lucide-react'
import {
  AppShell,
  Badge,
  Button,
  buttonClassName,
  BidiText,
  Column,
  ConfirmModal,
  DateText,
  Input,
  Modal,
  FormErrorSummary,
  FormRequiredHint,
  Select,
  Pagination,
  Table,
  Tabs,
  TextArea,
  usePagination,
  type TableSort,
  useToast,
} from '@/app/components/ui'
import { PROJECT_STATUS_LABEL, projectStatusBadgeClass, userStatusBadgeClass } from '@/lib/statusStyles'
import { formatCount } from '@/lib/arabicCount'
import { PROJECT_NAME_MAX, validateProjectSchedule } from '@/lib/projectSchedule'
import { USER_DEPARTMENT_MAX, USER_EMAIL_MAX, USER_NAME_MAX, USER_ROLE_LABEL, passwordProblem, validateUserProfile, type UserRole } from '@/lib/userRules'
import { useSessionRole } from '@/app/components/executive/hooks'
import { employeeNumberProblem, normalizeEmployeeNumber } from '@/lib/employeeNumber'
import { EditProjectModal, ProjectMetaFields, ScheduleFields, metaPayload, schedulePayload } from './components/ProjectMetaFields'
import { EditUserModal } from './components/EditUserModal'

interface Project {
  id: string
  name: string
  status: string
  total_weeks: number
  start_date: string
  end_date: string
  description?: string | null
  project_code?: string | null
  department?: string | null
  priority?: string | null
}

type AdminTab = 'projects' | 'users'

interface Profile {
  id: string
  full_name: string
  email: string
  role: string
  department: string
  employee_number?: string | null
  job_title?: string | null
  mobile_no?: string | null
  status: string
  /** صف الحساب الحالي (من GET /api/users) — لا يُعرض له إيقاف/حذف. */
  is_self?: boolean
}

interface Assignment {
  user_id: string
  full_name: string
  email: string
  role: string
  status?: string
}

interface NewProjectForm {
  name: string
  description: string
  start_date: string
  end_date: string
  total_weeks: string
  project_code: string
  department: string
  priority: string
}

const EMPTY_PROJECT: NewProjectForm = {
  name: '', description: '', start_date: '', end_date: '', total_weeks: '',
  project_code: '', department: '', priority: '',
}

interface NewUserForm {
  employee_number: string
  full_name: string
  email: string
  role: string
  department: string
  job_title: string
  mobile_no: string
  main_department: string
  section_name: string
  extension_number: string
  manager_name: string
  manager_number: string
  authorized_manager_name: string
  authorized_manager_number: string
  password_hash: string
  confirm_password: string
}

const EMPTY_USER: NewUserForm = {
  employee_number: '', full_name: '', email: '', role: 'pm', department: '',
  job_title: '', mobile_no: '', main_department: '', section_name: '', extension_number: '',
  manager_name: '', manager_number: '', authorized_manager_name: '', authorized_manager_number: '',
  password_hash: '', confirm_password: '',
}

const ROLE_BADGE_TONE: Record<UserRole, 'info' | 'success' | 'accent'> = {
  admin: 'success',
  deputy: 'accent',
  pm: 'info',
}

interface RowMenuItem {
  key: string
  label: string
  tone: 'default' | 'warning' | 'danger'
  /** أيقونة ترافق النص حتى يتميّز «الحذف النهائي» بصريًا لا باللون وحده. */
  icon: LucideIcon
  onSelect: () => void
}

const MENU_TONE: Record<RowMenuItem['tone'], string> = {
  default: 'text-pc-gray-900',
  warning: 'text-pc-warning-800',
  danger: 'text-pc-error-800',
}

/**
 * قائمة «المزيد» لإجراءات الصف الثانوية. عمود الإجراءات كان «إسناد موظف /
 * إلغاء / حذف» يلتف على سطرين، و«إلغاء» تُقرأ كإغلاق نافذة. القائمة بموضع fixed حتى لا تقصّها
 * حدود البطاقة، وتدعم الأسهم وHome/End وEscape وتُغلق عند النقر خارجها أو التمرير.
 */
function RowActionsMenu({ label, items }: Readonly<{ label: string; items: RowMenuItem[] }>) {
  const [open, setOpen] = useState(false)
  const [pos, setPos] = useState<{ top: number; left: number }>({ top: 0, left: 0 })
  const buttonRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const menuId = useId()

  function openMenu() {
    const rect = buttonRef.current?.getBoundingClientRect()
    if (!rect) return
    const scale = (Number.parseFloat(getComputedStyle(document.documentElement).fontSize) || 16) / 16
    const menuHeight = (items.length * 44 + 16) * scale
    const menuWidth = 200 * scale
    const fitsBelow = rect.bottom + menuHeight + 8 < window.innerHeight
    setPos({
      top: fitsBelow ? rect.bottom + 4 : Math.max(8, rect.top - menuHeight - 4),
      left: Math.max(8, Math.min(rect.left, window.innerWidth - menuWidth - 8)),
    })
    setOpen(true)
  }

  function closeMenu(returnFocus: boolean) {
    setOpen(false)
    if (returnFocus) buttonRef.current?.focus()
  }

  useEffect(() => {
    if (!open) return
    menuRef.current?.querySelector<HTMLButtonElement>('[role="menuitem"]')?.focus()
    function onPointerDown(event: PointerEvent) {
      const target = event.target as Node
      if (!menuRef.current?.contains(target) && !buttonRef.current?.contains(target)) setOpen(false)
    }
    function onViewportChange() {
      setOpen(false)
    }
    document.addEventListener('pointerdown', onPointerDown)
    window.addEventListener('scroll', onViewportChange, true)
    window.addEventListener('resize', onViewportChange)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      window.removeEventListener('scroll', onViewportChange, true)
      window.removeEventListener('resize', onViewportChange)
    }
  }, [open])

  function onMenuKeyDown(event: React.KeyboardEvent<HTMLDivElement>) {
    const buttons = Array.from(menuRef.current?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]') ?? [])
    const index = buttons.indexOf(document.activeElement as HTMLButtonElement)
    if (event.key === 'Escape') { event.preventDefault(); closeMenu(true); return }
    if (event.key === 'Tab') { setOpen(false); return }
    let next = -1
    if (event.key === 'ArrowDown') next = (index + 1) % buttons.length
    if (event.key === 'ArrowUp') next = (index - 1 + buttons.length) % buttons.length
    if (event.key === 'Home') next = 0
    if (event.key === 'End') next = buttons.length - 1
    if (next >= 0) { event.preventDefault(); buttons[next]?.focus() }
  }

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        onClick={() => (open ? closeMenu(false) : openMenu())}
        className={buttonClassName({ variant: 'secondary', size: 'sm' })}
      >
        <MoreHorizontal aria-hidden="true" className="h-4 w-4" />
        المزيد{' '}
        <span className="sr-only">من إجراءات {label}</span>
      </button>
      {open && (
        <div
          ref={menuRef}
          id={menuId}
          role="menu"
          tabIndex={-1}
          aria-label={`إجراءات ${label}`}
          onKeyDown={onMenuKeyDown}
          style={{ position: 'fixed', top: pos.top, left: pos.left, width: '12.5rem', zIndex: 60 }}
          className="rounded-pc-md border border-pc-gray-200 bg-white p-pc-xs shadow-pc-float"
        >
          {items.map(item => (
            <button
              key={item.key}
              type="button"
              role="menuitem"
              tabIndex={-1}
              onClick={() => { closeMenu(false); item.onSelect() }}
              className={`flex min-h-[2.75rem] w-full items-center gap-pc-s rounded-pc-md px-pc-s text-start text-sm font-medium hover:bg-pc-gray-50 focus:bg-pc-gray-100 focus:outline-none ${MENU_TONE[item.tone]}`}
            >
              <item.icon aria-hidden="true" className="h-4 w-4 shrink-0" />
              {item.label}
            </button>
          ))}
        </div>
      )}
    </>
  )
}

// Note: تسميات الحالة (نصوص عربية) بقيت كما هي بدون أي تغيير في المحتوى — فقط
// ألوان الخلفية/النص انتقلت لملف مشترك lib/statusStyles.ts (راجع الاستيراد أعلى).
const statusLabel = PROJECT_STATUS_LABEL
const userStatusLabel: Record<string, string> = { active: 'نشط', disabled: 'موقوف', deleted: 'محذوف' }

const ADMIN_PAGE_SIZE = 20
const DEFAULT_SORT: TableSort = { key: 'name', dir: 'asc' }
/** قيمة قائمة «ترتيب النتائج» على الجوال (`key:dir`) ← حالة الفرز نفسها التي يستعملها رأس الجدول. */
function parseSort(value: string): TableSort {
  const [key, dir] = value.split(':')
  return key ? { key, dir: dir === 'desc' ? 'desc' : 'asc' } : DEFAULT_SORT
}

type FieldError = { fieldId: string; message: string }

const PROJECT_FIELD_IDS: Record<string, string> = {
  name: 'new-project-name',
  project_code: 'new-project-code',
  department: 'new-project-department',
  priority: 'new-project-priority',
  start_date: 'new-project-start',
  end_date: 'new-project-end',
  total_weeks: 'new-project-weeks',
}

const CONFIRM_FIELD_ID = 'new-user-confirm'

const USER_FIELD_IDS: Record<string, string> = {
  employee_number: 'new-user-employee-number',
  full_name: 'new-user-name',
  email: 'new-user-email',
  password_hash: 'new-user-password',
  confirm_password: CONFIRM_FIELD_ID,
  department: 'new-user-dept',
  role: 'new-user-role',
}

/** قائمة أخطاء مرتبة بترتيب الحقول في النموذج. */
function toFieldErrors(errors: Record<string, string>, ids: Record<string, string>): FieldError[] {
  return Object.keys(ids).filter(key => errors[key]).map(key => ({ fieldId: ids[key], message: errors[key] }))
}

function validateNewProject(p: NewProjectForm): Record<string, string> {
  const errors: Record<string, string> = {}
  const name = p.name.trim()
  if (!name) errors.name = 'اسم المشروع مطلوب — اكتب اسمًا يميّز المشروع.'
  else if (name.length > PROJECT_NAME_MAX) errors.name = `اسم المشروع يجب ألا يزيد على ${PROJECT_NAME_MAX} حرف — اختصره.`
  Object.assign(errors, validateProjectSchedule(p).errors)
  if (!p.start_date && !errors.start_date) errors.start_date = 'تاريخ البداية مطلوب.'
  if (!p.end_date && !errors.end_date) errors.end_date = 'تاريخ النهاية مطلوب.'
  return errors
}

function validateNewUser(u: NewUserForm): Record<string, string> {
  const errors: Record<string, string> = { ...validateUserProfile(u).errors }
  const employeeProblem = employeeNumberProblem(u.employee_number)
  if (employeeProblem) errors.employee_number = employeeProblem
  const problem = passwordProblem(u.password_hash)
  if (problem) errors.password_hash = problem
  else if (!u.confirm_password) errors.confirm_password = 'أعد كتابة كلمة المرور للتأكيد.'
  else if (u.confirm_password !== u.password_hash) errors.confirm_password = 'كلمتا المرور غير متطابقتين — أعد كتابتها بنفس الحروف.'
  return errors
}

/** ينقل التركيز إلى حقل بعد إعادة الرسم (حتى تظهر رسالة الخطأ المرتبطة به أولًا). */
function focusField(id: string) {
  requestAnimationFrame(() => document.getElementById(id)?.focus())
}

function AdminFilterPanel({
  description,
  resultCount,
  totalCount,
  resultLabel,
  hasActiveFilters,
  onReset,
  children,
}: Readonly<{
  description: string
  resultCount: number
  totalCount: number
  resultLabel: string
  hasActiveFilters: boolean
  onReset: () => void
  children: ReactNode
}>) {
  return (
    <section
      aria-label="أدوات البحث والتصفية"
      className="mb-pc-m rounded-pc-md border border-pc-brand-100 bg-white p-pc-m shadow-pc-float"
    >
      <div className="flex flex-wrap items-start justify-between gap-pc-s">
        <div className="flex min-w-0 items-start gap-pc-s">
          <span
            aria-hidden="true"
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-pc-md bg-pc-brand-50 text-pc-brand-700"
          >
            <SlidersHorizontal className="h-5 w-5" />
          </span>
          <div className="min-w-0">
            <h2 className="text-base font-bold text-pc-gray-900">البحث والتصفية</h2>
            <p className="mt-1 text-sm leading-6 text-pc-gray-600">{description}</p>
          </div>
        </div>
        <Button
          variant="ghost"
          size="sm"
          icon={<RotateCcw aria-hidden="true" className="h-4 w-4" />}
          disabled={!hasActiveFilters}
          disabledReason="لا توجد مرشحات لإزالتها"
          onClick={onReset}
          className="border border-pc-gray-200 bg-white shadow-pc-card hover:border-pc-brand-100 hover:bg-pc-brand-50"
        >
          إعادة الضبط
        </Button>
      </div>

      <div className="mt-pc-m rounded-pc-md border border-pc-gray-200 bg-pc-gray-50 p-pc-s sm:p-pc-m">
        {children}
      </div>

      <div className="mt-pc-s flex items-center gap-pc-s border-t border-pc-gray-100 pt-pc-s" aria-live="polite">
        <span
          aria-hidden="true"
          className="h-2.5 w-2.5 shrink-0 rounded-full bg-pc-brand-600 ring-4 ring-pc-brand-50"
        />
        <p className="text-sm text-pc-gray-600">
          عرض <span className="font-bold text-pc-brand-700">{resultCount}</span> من أصل{' '}
          <span className="font-semibold text-pc-gray-900">{totalCount}</span> {resultLabel}
        </p>
      </div>
    </section>
  )
}

/** وصف نافذة تأكيد واحدة تحل محل كل استدعاءات confirm() الأصلية. */
interface ConfirmState {
  title: string
  message: string
  /** صياغة الإجراء نفسه — قاعدة إرشادات التصميم: ممنوع «موافق». */
  confirmLabel: string
  destructive?: boolean
  /** '' يخفي «لا يمكن التراجع» حين يكون الإجراء قابلًا للتراجع (إيقاف الموظف). */
  irreversibleNote?: string
  onConfirm: () => void | Promise<void>
}

/** نصوص الحالة الفارغة: لا بيانات أصلًا، أو لا نتائج مطابقة للمرشحات. */
function emptyCopy(noData: boolean, copy: { title: string; description: string; filteredDescription: string }) {
  return noData
    ? { title: copy.title, description: copy.description }
    : { title: 'لا توجد نتائج مطابقة', description: copy.filteredDescription }
}

/** أخطاء الخادم + أخطاء التحقق المحلي بعد أول محاولة حفظ. */
function mergeErrors(server: Record<string, string>, tried: boolean, validate: () => Record<string, string>): Record<string, string> {
  return tried ? { ...server, ...validate() } : server
}

/** مدير النظام وحده يرى الموظفين؛ غيره لا يُطلب منه شيء (null). */
function usersUrl(canManage: boolean, f: { q: string; role: string; status: string; sort: TableSort }): string | null {
  if (!canManage) return null
  return `/api/users?${listQuery({ q: f.q, role: f.role, status: f.status, sort: f.sort.key, dir: f.sort.dir })}`
}

function permissionsFor(role: string | null | undefined) {
  return { canManage: role === 'admin', canCreateProject: role === 'admin' || role === 'deputy' }
}

function readProjectStats(data: Record<string, unknown> | null | undefined): { total: number; overdue: number } {
  return (data?.stats as { total: number; overdue: number } | undefined) ?? { total: 0, overdue: 0 }
}

function readAllTotal(data: Record<string, unknown> | null | undefined): number {
  return (data?.allTotal as number | undefined) ?? 0
}

interface ProjectColumnHandlers {
  canManage: boolean
  onAssign: (p: Project) => void
  onEdit: (p: Project) => void
  onActivate: (p: Project) => void
  onCancel: (p: Project) => void
  onDelete: (p: Project) => void
}

/** أعمدة جدول المشاريع (مكوّن Table الموحّد). */
function buildProjectColumns(h: ProjectColumnHandlers): Column<Project>[] {
  return [
    {
      key: 'name',
      header: 'المشروع',
      sortValue: p => p.name,
      cell: p => <BidiText className="font-semibold text-pc-gray-900">{p.name}</BidiText>,
    },
    {
      key: 'start',
      header: 'البداية',
      sortValue: p => p.start_date ?? '',
      cell: p => <DateText value={p.start_date} />,
    },
    {
      key: 'end',
      header: 'النهاية',
      sortValue: p => p.end_date ?? '',
      cell: p => <DateText value={p.end_date} />,
    },
    {
      key: 'weeks',
      header: 'الأسابيع',
      sortValue: p => p.total_weeks ?? 0,
      cell: p => p.total_weeks,
    },
    {
      key: 'status',
      header: 'الحالة',
      sortValue: p => p.status,
      cell: p => (
        <span className={`rounded-pc-md px-2 py-1 text-sm font-medium ${projectStatusBadgeClass(p.status)}`}>
          {statusLabel[p.status] || p.status}
        </span>
      ),
    },
    {
      key: 'actions',
      header: 'الإجراءات',
      isActions: true,
      cell: p => (
        <div className="flex flex-nowrap items-center justify-end gap-pc-s">
          <Button variant="secondary" size="sm" onClick={() => h.onAssign(p)} icon={<UserPlus aria-hidden="true" className="h-4 w-4" />} className="whitespace-nowrap">
            إسناد موظفين{' '}
            <span className="sr-only">لمشروع {p.name}</span>
          </Button>
          {h.canManage && (
            <RowActionsMenu
              label={`مشروع ${p.name}`}
              items={[
                { key: 'edit', label: 'تعديل بيانات المشروع', tone: 'default', icon: Pencil, onSelect: () => h.onEdit(p) },
                p.status === 'cancelled'
                  ? { key: 'activate', label: 'إعادة تفعيل المشروع', tone: 'default', icon: RotateCcw, onSelect: () => h.onActivate(p) }
                  : { key: 'cancel', label: 'إلغاء المشروع', tone: 'warning', icon: Ban, onSelect: () => h.onCancel(p) },
                { key: 'delete', label: 'حذف المشروع نهائيًا', tone: 'danger', icon: Trash2, onSelect: () => h.onDelete(p) },
              ]}
            />
          )}
        </div>
      ),
    },
  
  ]
}

interface UserColumnHandlers {
  onEdit: (u: Profile) => void
  onToggleStatus: (u: Profile) => void
  onDelete: (u: Profile) => void
}

/** أعمدة جدول الموظفين. */
function buildUserColumns(h: UserColumnHandlers): Column<Profile>[] {
  return [
    {
      key: 'name',
      header: 'الاسم',
      sortValue: u => u.full_name,
      cell: u => <BidiText className="font-semibold text-pc-gray-900">{u.full_name}</BidiText>,
    },
    { key: 'employee_number', header: 'الرقم الوظيفي', sortValue: u => u.employee_number ?? '', cell: u => <BidiText direction="ltr">{u.employee_number || '—'}</BidiText> },
    { key: 'job_title', header: 'المسمى الوظيفي', sortValue: u => u.job_title ?? '', cell: u => <BidiText>{u.job_title || '—'}</BidiText> },
    { key: 'email', header: 'البريد', sortValue: u => u.email ?? '', cell: u => <BidiText direction="ltr">{u.email}</BidiText> },
    {
      key: 'role',
      header: 'الدور',
      sortValue: u => u.role,
      cell: u => (
        <Badge tone={ROLE_BADGE_TONE[u.role as UserRole] ?? 'neutral'}>
          {USER_ROLE_LABEL[u.role as UserRole] ?? u.role}
        </Badge>
      ),
    },
    { key: 'dept', header: 'القسم', sortValue: u => u.department ?? '', cell: u => <BidiText>{u.department || '—'}</BidiText> },
    {
      key: 'status',
      header: 'الحالة',
      sortValue: u => u.status ?? '',
      cell: u => (
        <span className={`rounded-pc-md px-2 py-1 text-sm font-medium ${userStatusBadgeClass(u.status)}`}>
          {userStatusLabel[u.status] || u.status || 'نشط'}
        </span>
      ),
    },
    {
      key: 'actions',
      header: 'الإجراءات',
      isActions: true,
      // Note: جمعنا التحكم بالحساب في نفس الخلية حتى يستطيع الأدمن تفعيل/إيقاف/حذف الموظف من مكان واحد.
      cell: u => (
        <div className="flex flex-nowrap items-center justify-end gap-pc-s whitespace-nowrap">
          <Button variant="secondary" size="sm" onClick={() => h.onEdit(u)} icon={<Pencil aria-hidden="true" className="h-4 w-4" />}>
            تعديل{' '}
            <span className="sr-only">بيانات {u.full_name}</span>
          </Button>
          {u.is_self ? (
            <span className="text-sm text-pc-gray-600">حسابك الحالي</span>
          ) : (
            <>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => h.onToggleStatus(u)}
                icon={u.status === 'disabled'
                  ? <RotateCcw aria-hidden="true" className="h-4 w-4" />
                  : <Ban aria-hidden="true" className="h-4 w-4" />}
              >
                {u.status === 'disabled' ? 'تفعيل' : 'إيقاف'}
                <span className="sr-only">{' '}حساب {u.full_name}</span>
              </Button>
              <Button variant="danger-outline" size="sm" onClick={() => h.onDelete(u)} icon={<Trash2 aria-hidden="true" className="h-4 w-4" />}>
                حذف{' '}
                <span className="sr-only">الموظف {u.full_name}</span>
              </Button>
            </>
          )}
        </div>
      ),
    },
  
  ]
}

export default function AdminPage() {
  const toast = useToast()
  const { role } = useSessionRole()
  const { canManage, canCreateProject } = permissionsFor(role)
  const [tab, setTab] = useState<AdminTab>('projects')
  const [editProject, setEditProject] = useState<Project | null>(null)
  const [editUser, setEditUser] = useState<Profile | null>(null)
  const [showAddProject, setShowAddProject] = useState(false)
  const [showAddUser, setShowAddUser] = useState(false)
  const [showAssign, setShowAssign] = useState(false)
  const [assignProject, setAssignProject] = useState<Project | null>(null)
  const [assignments, setAssignments] = useState<Assignment[]>([])
  const [assignSearch, setAssignSearch] = useState('')
  const assigningRef = useRef(new Set<string>())
  const [confirmState, setConfirmState] = useState<ConfirmState | null>(null)
  const [confirmBusy, setConfirmBusy] = useState(false)
  const [newProject, setNewProject] = useState<NewProjectForm>(EMPTY_PROJECT)
  const [newUser, setNewUser] = useState<NewUserForm>(EMPTY_USER)
  const [assignablePms, setAssignablePms] = useState<Profile[]>([])
  const [projectSearch, setProjectSearch] = useState('')
  const [userSearch, setUserSearch] = useState('')
  const [projectStatusFilter, setProjectStatusFilter] = useState('all')
  const [userStatusFilter, setUserStatusFilter] = useState('all')
  const [userRoleFilter, setUserRoleFilter] = useState('all')
  const [projectSort, setProjectSort] = useState<TableSort>(DEFAULT_SORT)
  const [userSort, setUserSort] = useState<TableSort>(DEFAULT_SORT)
  const [projectSaving, setProjectSaving] = useState(false)
  const [userSaving, setUserSaving] = useState(false)
  const [projectTried, setProjectTried] = useState(false)
  const [userTried, setUserTried] = useState(false)
  const [projectServerErrors, setProjectServerErrors] = useState<Record<string, string>>({})
  const [userServerErrors, setUserServerErrors] = useState<Record<string, string>>({})
  const projectErrorMap = mergeErrors(projectServerErrors, projectTried, () => validateNewProject(newProject))
  const userErrorMap = mergeErrors(userServerErrors, userTried, () => validateNewUser(newUser))
  const projectErrors = toFieldErrors(projectErrorMap, PROJECT_FIELD_IDS)
  const userErrors = toFieldErrors(userErrorMap, USER_FIELD_IDS)

  const debouncedProjectSearch = useDebouncedValue(projectSearch.trim())
  const debouncedUserSearch = useDebouncedValue(userSearch.trim())
  const projectsList = useServerList<Project>(
    `/api/projects?${listQuery({ q: debouncedProjectSearch, status: projectStatusFilter, sort: projectSort.key, dir: projectSort.dir })}`,
    'projects',
    ADMIN_PAGE_SIZE
  )
  const usersList = useServerList<Profile>(
    usersUrl(canManage, { q: debouncedUserSearch, role: userRoleFilter, status: userStatusFilter, sort: userSort }),
    'users',
    ADMIN_PAGE_SIZE
  )
  const projectStats = readProjectStats(projectsList.data)
  const usersTotal = readAllTotal(usersList.data)

  /** إعادة جلب الصفحة الحالية من الجدولين بعد أي إضافة/تعديل/حذف. */
  function fetchAll() {
    projectsList.reload()
    usersList.reload()
  }

  /** ينفّذ إجراء نافذة التأكيد ثم يغلقها (يحل محل `if (!confirm(...)) return`). */
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

  /** رسالة نجاح موحّدة: Toast. كان يُعرض معه Alert أعلى الصفحة بنفس النص فتظهر الرسالة مرتين. */
  function notifyOk(text: string) {
    toast.success(text)
  }

  /**
   * حذف المشروع وإلغاؤه وتفعيله، وإيقاف الموظف وحذفه، كانت تعرض «تم بنجاح» دون
   * فحص رد الخادم (أثبته الاختبار باعتراض الطلبات وإرجاع 500). هذا المساعد يعرض النجاح فقط إن ردّ
   * الخادم بنجاح، وإلا رسالة الخادم نفسها (مثل «لا يمكنك إيقاف حسابك»).
   */
  async function mutate(url: string, init: { method: string; body?: unknown }, successText: string, failureText: string): Promise<boolean> {
    let res: Response | null = null
    try {
      res = await fetch(url, {
        method: init.method,
        headers: init.body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
        body: init.body !== undefined ? JSON.stringify(init.body) : undefined,
      })
    } catch {
      res = null
    }
    if (!res) {
      toast.error(`${failureText} تعذّر الاتصال بالخادم؛ تحقّق من الاتصال ثم أعد المحاولة.`)
      return false
    }
    if (!res.ok) {
      const data = await res.json().catch(() => null)
      toast.error(`${failureText} ${data?.error || 'أعد المحاولة بعد قليل.'}`)
      // العنصر لم يعد موجودًا: نحدّث القائمة حتى لا يبقى صف قديم.
      if (res.status === 404) fetchAll()
      return false
    }
    notifyOk(successText)
    fetchAll()
    return true
  }

  async function addProject() {
    if (projectSaving) return
    setProjectTried(true)
    setProjectServerErrors({})
    const errors = toFieldErrors(validateNewProject(newProject), PROJECT_FIELD_IDS)
    if (errors.length > 0) { focusField(errors[0].fieldId); return }
    setProjectSaving(true)
    try {
      const res = await fetch('/api/projects', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: newProject.name.trim(),
          description: newProject.description,
          ...schedulePayload(newProject),
          ...metaPayload(newProject),
        })
      })
      if (res.ok) {
        setShowAddProject(false)
        setProjectTried(false)
        notifyOk('تمت إضافة المشروع بنجاح')
        setNewProject(EMPTY_PROJECT)
        fetchAll()
      } else {
        const data = await res.json().catch(() => null)
        const serverErrors: Record<string, string> = data?.errors ?? {}
        setProjectServerErrors(serverErrors)
        const first = toFieldErrors(serverErrors, PROJECT_FIELD_IDS)[0]
        if (first) {
          focusField(first.fieldId)
          toast.error('لم يُحفظ المشروع — صحّح الحقول المعلَّمة ثم أعد المحاولة.')
        } else {
          toast.error(data?.error ? `لم يُحفظ المشروع. ${data.error}` : 'تعذّر إضافة المشروع. أعد المحاولة.')
        }
      }
    } catch {
      toast.error('تعذّر الاتصال بالخادم فلم يُحفظ المشروع. تحقّق من الاتصال ثم اضغط «حفظ المشروع» مرة أخرى.')
    } finally {
      setProjectSaving(false)
    }
  }

  async function addUser() {
    if (userSaving) return
    setUserTried(true)
    setUserServerErrors({})
    const currentNumber = normalizeEmployeeNumber(newUser.employee_number)
    const errors = toFieldErrors(validateNewUser(newUser), USER_FIELD_IDS)
    if (errors.length > 0) { focusField(errors[0].fieldId); return }
    setUserSaving(true)
    try {
      const payload = {
        employee_number: currentNumber,
        full_name: newUser.full_name,
        email: newUser.email,
        department: newUser.department,
        job_title: newUser.job_title,
        mobile_no: newUser.mobile_no,
        main_department: newUser.main_department,
        section_name: newUser.section_name,
        extension_number: newUser.extension_number,
        manager_name: newUser.manager_name,
        manager_number: newUser.manager_number,
        authorized_manager_name: newUser.authorized_manager_name,
        authorized_manager_number: newUser.authorized_manager_number,
        role: newUser.role,
        password_hash: newUser.password_hash,
      }
      const res = await fetch('/api/users', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      })
      const data = await res.json().catch(() => null)
      if (res.ok) {
        setShowAddUser(false)
        setUserTried(false)
        notifyOk('تمت إضافة الموظف بنجاح؛ سيصله إشعار بالبريد خلال لحظات')
        setNewUser(EMPTY_USER)
        fetchAll()
      } else {
        const serverErrors: Record<string, string> = data?.errors ?? {}
        setUserServerErrors(serverErrors)
        const first = toFieldErrors(serverErrors, USER_FIELD_IDS)[0]
        if (first) focusField(first.fieldId)
        toast.error(data?.error || 'تعذّر إضافة الموظف. تحقّق من البيانات ثم أعد المحاولة.')
      }
    } catch {
      toast.error('تعذّر الاتصال بالخادم فلم يُحفظ الموظف. تحقّق من الاتصال ثم اضغط «حفظ الموظف» مرة أخرى.')
    } finally {
      setUserSaving(false)
    }
  }

  function updateNewProject(patch: Partial<NewProjectForm>) {
    setNewProject(prev => ({ ...prev, ...patch }))
    if (Object.keys(projectServerErrors).length > 0) setProjectServerErrors({})
  }

  function updateNewUser(patch: Partial<NewUserForm>) {
    setNewUser(prev => ({ ...prev, ...patch }))
    if (Object.keys(userServerErrors).length > 0) setUserServerErrors({})
  }

  function deleteProject(project: Project) {
    setConfirmState({
      title: 'حذف المشروع نهائيًا',
      message: `سيُحذف مشروع "${project.name}" نهائيًا مع جميع مراحله ومهامه وملاحظاته وإسناداته وتحديثاته ومخاطره وإجراءات اجتماعاته. استخدم «إلغاء المشروع» إن كنت تريد الاحتفاظ بسجله.`,
      confirmLabel: 'حذف المشروع',
      destructive: true,
      onConfirm: async () => {
        await mutate(`/api/projects/${project.id}`, { method: 'DELETE' }, 'تم حذف المشروع', 'لم يُحذف المشروع.')
      },
    })
  }

  function cancelProject(project: Project) {
    setConfirmState({
      title: 'إلغاء المشروع',
      message: `سيختفي مشروع "${project.name}" من صفحة الاجتماع بدون حذف بياناته، ويمكنك إعادة تفعيله لاحقًا.`,
      confirmLabel: 'إلغاء المشروع',
      destructive: false,
      onConfirm: async () => {
        // Note: عند إلغاء المشروع لا نحذف السجل، فقط نرسل status = cancelled للـ API حتى تبقى بياناته محفوظة.
        await mutate(`/api/projects/${project.id}`, { method: 'PATCH', body: { status: 'cancelled' } },
          'تم إلغاء المشروع وإخفاؤه من صفحة الاجتماع', 'لم يُلغَ المشروع.')
      },
    })
  }

  function activateProject(project: Project) {
    setConfirmState({
      title: 'إعادة تفعيل المشروع',
      message: `سيظهر مشروع "${project.name}" مرة أخرى في صفحات المتابعة.`,
      confirmLabel: 'تفعيل المشروع',
      destructive: false,
      onConfirm: async () => {
        // Note: إعادة التفعيل تستخدم نفس endpoint وتعيد status إلى active حتى يرجع المشروع للظهور بدون إنشاء سجل جديد.
        await mutate(`/api/projects/${project.id}`, { method: 'PATCH', body: { status: 'active' } },
          'تم تفعيل المشروع وإعادته لصفحات المتابعة', 'لم يُفعَّل المشروع.')
      },
    })
  }

  async function openAssign(project: Project) {
    setAssignProject(project)
    setAssignSearch('')
    try {
      const [projectAssignments, pms] = await Promise.all([
        fetchAllPages<Assignment>(`/api/assignments?project_id=${project.id}`, 'assignments', 300),
        fetchAllPages<Profile>('/api/users?role=pm&status=active', 'users', 100),
      ])
      setAssignments(projectAssignments)
      setAssignablePms(pms)
      setShowAssign(true)
    } catch {
      toast.error('تعذّر تحميل موظفي المشروع. أعد المحاولة.')
    }
  }

  async function refreshAssignments() {
    try {
      setAssignments(await fetchAllPages<Assignment>(`/api/assignments?project_id=${assignProject?.id}`, 'assignments', 300))
    } catch {
      toast.error('تم الحفظ، لكن تعذّر تحديث القائمة المعروضة. أغلق النافذة وافتحها مجددًا.')
    }
  }

  async function assignUser(user: Profile) {
    if (assigningRef.current.has(user.id)) return
    assigningRef.current.add(user.id)
    setAssignments(prev => [...prev, { user_id: user.id, full_name: user.full_name, email: user.email, role: user.role }])
    const res = await fetch('/api/assignments', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ project_id: assignProject?.id, user_id: user.id })
    }).catch(() => null)
    assigningRef.current.delete(user.id)
    if (!res?.ok) {
      setAssignments(prev => prev.filter(a => a.user_id !== user.id))
      const data = await res?.json().catch(() => null)
      toast.error(`تعذّر إسناد الموظف. ${data?.error || 'أعد المحاولة.'}`)
      return
    }
    notifyOk('تم إسناد الموظف بنجاح')
    await refreshAssignments()
  }

  async function removeAssign(userId: string) {
    const res = await fetch('/api/assignments', {
      method: 'DELETE',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ project_id: assignProject?.id, user_id: userId })
    }).catch(() => null)
    if (!res?.ok) {
      const data = await res?.json().catch(() => null)
      toast.error(`تعذّر إزالة الموظف من المشروع. ${data?.error || 'أعد المحاولة.'}`)
      if (res?.status === 404) await refreshAssignments()
      return
    }
    notifyOk('تمت إزالة الموظف من المشروع')
    await refreshAssignments()
  }

  function toggleUserStatus(user: Profile) {
    // Note: إذا كان الموظف موقوفًا نفعله، وإذا كان نشطًا نوقفه بنفس الزر.
    const nextStatus = user.status === 'disabled' ? 'active' : 'disabled'
    const disabling = nextStatus === 'disabled'
    setConfirmState({
      title: disabling ? 'إيقاف حساب الموظف' : 'إعادة تفعيل حساب الموظف',
      message: disabling
        ? `لن يستطيع "${user.full_name}" تسجيل الدخول أو الإسناد لمشاريع جديدة، وتُنهى جلساته المفتوحة فورًا. يمكنك إعادة تفعيله لاحقًا.`
        : `سيستعيد "${user.full_name}" إمكانية تسجيل الدخول والإسناد للمشاريع.`,
      confirmLabel: disabling ? 'إيقاف الموظف' : 'تفعيل الموظف',
      destructive: disabling,
      irreversibleNote: '',
      onConfirm: async () => {
        // Note: إيقاف الموظف يغير status فقط حتى يمكن إعادة تفعيله لاحقًا بدون حذف الحساب.
        await mutate(`/api/users/${user.id}`, { method: 'PATCH', body: { status: nextStatus } },
          disabling ? 'تم إيقاف الموظف' : 'تم تفعيل الموظف',
          disabling ? 'لم يُوقف الموظف.' : 'لم يُفعَّل الموظف.')
      },
    })
  }

  function deleteUser(user: Profile) {
    setConfirmState({
      title: 'حذف الموظف نهائيًا',
      message: `سيُحذف حساب "${user.full_name}" نهائيًا من قاعدة البيانات، وتُزال جلساته ورموز دخوله وإسناداته. تبقى سجلات المشاريع نفسها دون ربطها بالحساب المحذوف.`,
      confirmLabel: 'حذف الموظف',
      destructive: true,
      irreversibleNote: 'سيُحذف الحساب نهائيًا من قاعدة البيانات مع جلساته ورموز دخوله وإسناداته، ولا يمكن التراجع عن ذلك.',
      onConfirm: async () => {
        await mutate(`/api/users/${user.id}`, { method: 'DELETE' },
          'تم حذف الموظف نهائيًا', 'لم يُحذف الموظف.')
      },
    })
  }

  const cls = {
    statCard: 'rounded-pc-md border border-pc-gray-200 bg-white p-pc-m shadow-pc-card transition-[box-shadow,border-color,transform] duration-200 hover:-translate-y-0.5 hover:border-pc-brand-100 hover:shadow-pc-float',
  }

  const unassignedUsers = assignablePms.filter(
    // Note: الموظف الموقوف لا يظهر في قائمة الإسناد لمشاريع جديدة حتى لو كان حسابه محفوظًا.
    u => u.role === 'pm' && u.status === 'active' && !assignments.some(a => a.user_id === u.id)
  )

  const assignQuery = assignSearch.trim().toLocaleLowerCase('ar')
  const visibleUnassigned = assignQuery
    ? unassignedUsers.filter(u =>
        [u.full_name, u.email, u.department].some(v => String(v ?? '').toLocaleLowerCase('ar').includes(assignQuery))
      )
    : unassignedUsers

  const ASSIGN_PAGE_SIZE = 6
  const assignedPage = usePagination(assignments, ASSIGN_PAGE_SIZE)
  const unassignedPage = usePagination(visibleUnassigned, ASSIGN_PAGE_SIZE)

  const sameSort = (a: TableSort, b: TableSort) => a.key === b.key && a.dir === b.dir

  const hasActiveFilters = tab === 'projects'
    ? projectSearch !== '' || projectStatusFilter !== 'all' || !sameSort(projectSort, DEFAULT_SORT)
    : userSearch !== '' || userStatusFilter !== 'all' || userRoleFilter !== 'all' || !sameSort(userSort, DEFAULT_SORT)

  function resetFilters() {
    if (tab === 'projects') {
      setProjectSearch('')
      setProjectStatusFilter('all')
      setProjectSort(DEFAULT_SORT)
    } else {
      setUserSearch('')
      setUserStatusFilter('all')
      setUserRoleFilter('all')
      setUserSort(DEFAULT_SORT)
    }
  }

  const projectColumns = buildProjectColumns({
    canManage,
    onAssign: openAssign,
    onEdit: setEditProject,
    onActivate: activateProject,
    onCancel: cancelProject,
    onDelete: deleteProject,
  })
  const userColumns = buildUserColumns({ onEdit: setEditUser, onToggleStatus: toggleUserStatus, onDelete: deleteUser })

  const projectsEmpty = emptyCopy(projectStats.total === 0, {
    title: 'لا توجد مشاريع بعد',
    description: 'ابدأ بإضافة أول مشروع حتى تظهر بياناته في صفحات المتابعة والاجتماع.',
    filteredDescription: 'غيّر عبارة البحث أو المرشحات لعرض مشاريع أخرى.',
  })
  const usersEmpty = emptyCopy(usersTotal === 0, {
    title: 'لا يوجد موظفون',
    description: 'أضف موظفًا حتى تتمكن من إسناده إلى المشاريع.',
    filteredDescription: 'غيّر عبارة البحث أو المرشحات لعرض موظفين آخرين.',
  })
  let projectsEmptyAction: React.ReactNode
  if (projectStats.total > 0) projectsEmptyAction = <Button variant="secondary" onClick={resetFilters}>إزالة المرشحات</Button>
  else if (canCreateProject) projectsEmptyAction = <Button variant="secondary" onClick={() => setShowAddProject(true)}>إضافة مشروع جديد</Button>

  function renderUnassigned() {
    if (unassignedUsers.length === 0) return (
                <p className="rounded-pc-md bg-pc-gray-50 p-pc-m text-center text-sm text-pc-gray-600">
                  جميع مديري المشاريع مسندون لهذا المشروع
                </p>
    );
    if (visibleUnassigned.length === 0) return (
                <p className="rounded-pc-md bg-pc-gray-50 p-pc-m text-center text-sm text-pc-gray-600">
                  لا يوجد موظف يطابق «{assignSearch.trim()}». جرّب جزءًا من الاسم أو البريد.
                </p>
    );
    return (
                <ul className="flex flex-col gap-pc-xs">
                  {unassignedPage.visible.map(u => (
                    <li key={u.id} className="flex items-center justify-between gap-pc-s rounded-pc-md border border-pc-gray-200 bg-pc-gray-50 px-pc-s py-pc-s">
                      <div className="min-w-0">
                        <p className="text-sm font-medium text-pc-gray-900">{u.full_name}</p>
                        <p className="mt-1 text-sm text-pc-gray-600">{u.email}</p>
                      </div>
                      <Button variant="secondary" size="sm" onClick={() => assignUser(u)}>
                        إسناد{' '}
                        <span className="sr-only">{u.full_name} للمشروع</span>
                      </Button>
                    </li>
                  ))}
                </ul>
    );
  }

  return (
    <AppShell title={canManage ? 'لوحة إدارة المشاريع والموظفين' : 'إضافة المشاريع وإسنادها إلى الموظفين'}>
      <div>
          {/* Stats */}
          <section aria-label="مؤشرات عامة" className={`mb-pc-l grid grid-cols-1 gap-pc-s ${canManage ? 'sm:grid-cols-3' : 'sm:grid-cols-2'}`}>
            <div className={cls.statCard}>
              {/* Note: العدد الكلي يأتي من pagination.total (كل الصفحات) وليس طول الصفحة الحالية فقط. */}
              <p className="text-pc-h3 text-pc-gray-900">{projectStats.total}</p>
              <p className="mt-1 text-sm text-pc-gray-600">إجمالي المشاريع</p>
            </div>
            <div className={cls.statCard}>
              <p className={`text-pc-h3 ${projectStats.overdue > 0 ? 'text-pc-error-600' : 'text-pc-brand-700'}`}>{projectStats.overdue}</p>
              <p className="mt-1 text-sm text-pc-gray-600">مشاريع تجاوزت تاريخ النهاية</p>
            </div>
            {canManage && (
              <div className={cls.statCard}>
                <p className="text-pc-h3 text-pc-info-800">{usersTotal}</p>
                <p className="mt-1 text-sm text-pc-gray-600">الموظفون</p>
              </div>
            )}
          </section>

          {/* Tabs + الإجراء الأساسي الوحيد في الشاشة (قاعدة: زر أساسي واحد) */}
          <div className="mb-pc-m flex flex-wrap items-end justify-between gap-pc-s">
            {canManage ? (
              <Tabs
                ariaLabel="أقسام لوحة الإدارة"
                active={tab}
                onChange={id => setTab(id as AdminTab)}
                items={[
                  { id: 'projects', label: 'المشاريع', count: projectStats.total },
                  { id: 'users', label: 'الموظفون', count: usersTotal },
                ]}
                className="min-w-full flex-1 sm:min-w-0"
              />
            ) : (
              <p className="text-sm text-pc-gray-600">
                أضف مشروعًا جديدًا، أو اختر مشروعًا ثم «إسناد موظفين» لتحديد مدير المشروع المسؤول عنه.
              </p>
            )}
            {canCreateProject && (
              <div>
                <Button
                  onClick={() => (tab === 'projects' || !canManage ? setShowAddProject(true) : setShowAddUser(true))}
                >
                  {tab === 'projects' || !canManage ? 'إضافة مشروع جديد' : 'إضافة موظف جديد'}
                </Button>
              </div>
            )}
          </div>

          {/* Projects Table */}
          {tab === 'projects' && (
            <section
              role="tabpanel"
              id="panel-projects"
              aria-labelledby="tab-projects"
              aria-label="جدول المشاريع"
            >
              <h2 className="sr-only">قائمة المشاريع</h2>
              <AdminFilterPanel
                description="ابحث أو رشّح قائمة المشاريع."
                resultCount={projectsList.total}
                totalCount={projectStats.total}
                resultLabel="مشروعًا"
                hasActiveFilters={hasActiveFilters}
                onReset={resetFilters}
              >
                <div className="grid grid-cols-1 gap-pc-s sm:grid-cols-2 xl:grid-cols-[minmax(17.5rem,1fr)_minmax(13.75rem,0.55fr)]">
                  <Input
                    id="admin-project-search"
                    label="البحث في المشاريع"
                    type="search"
                    placeholder="الاسم أو الرقم أو الإدارة"
                    value={projectSearch}
                    onChange={event => setProjectSearch(event.target.value)}
                    inputClassName="bg-white shadow-pc-card"
                    endAdornment={
                      <Search aria-hidden="true" className="pointer-events-none h-4 w-4 text-pc-gray-600" />
                    }
                  />
                  <Select
                    id="admin-project-status"
                    placeholder={null}
                    label="ترشيح حسب الحالة"
                    value={projectStatusFilter}
                    onChange={setProjectStatusFilter}
                    options={[
                      { value: 'all', label: 'كل الحالات' },
                      { value: 'active', label: 'نشط' },
                      { value: 'completed', label: 'مكتمل' },
                      { value: 'delayed', label: 'متأخر' },
                      { value: 'cancelled', label: 'ملغى' },
                    ]}
                  />
                  <div className="xl:hidden">
                    <Select
                      id="admin-project-sort"
                      placeholder={null}
                      label="ترتيب النتائج"
                      value={`${projectSort.key}:${projectSort.dir}`}
                      onChange={value => setProjectSort(parseSort(value))}
                      options={[
                        { value: 'name:asc', label: 'الاسم: أ–ي' },
                        { value: 'name:desc', label: 'الاسم: ي–أ' },
                        { value: 'start:desc', label: 'الأحدث بدايةً' },
                        { value: 'start:asc', label: 'الأقدم بدايةً' },
                        { value: 'status:asc', label: 'حسب الحالة' },
                      ]}
                    />
                  </div>
                </div>
              </AdminFilterPanel>
              <Table
                caption="قائمة المشاريع مع تواريخها وحالتها وإجراءات إدارتها"
                cardColumns={2}
                columns={projectColumns}
                rows={projectsList.rows}
                rowKey={p => p.id}
                loading={projectsList.loading}
                unit="project"
                server={{
                  page: projectsList.page,
                  pageSize: projectsList.pageSize,
                  total: projectsList.total,
                  onPageChange: projectsList.setPage,
                  sort: projectSort,
                  onSortChange: setProjectSort,
                  refreshing: projectsList.refreshing,
                }}
                cardTitleKey="name"
                emptyTitle={projectsEmpty.title}
                emptyDescription={projectsEmpty.description}
                emptyAction={projectsEmptyAction}
              />
            </section>
          )}

          {/* Users Table */}
          {canManage && tab === 'users' && (
            <section
              role="tabpanel"
              id="panel-users"
              aria-labelledby="tab-users"
              aria-label="جدول الموظفين"
            >
              <h2 className="sr-only">قائمة الموظفين</h2>
              <AdminFilterPanel
                description="ابحث أو رشّح قائمة الموظفين."
                resultCount={usersList.total}
                totalCount={usersTotal}
                resultLabel="موظفًا"
                hasActiveFilters={hasActiveFilters}
                onReset={resetFilters}
              >
                <div className="grid grid-cols-1 gap-pc-s sm:grid-cols-2 xl:grid-cols-[minmax(16.25rem,1fr)_minmax(11.25rem,0.5fr)_minmax(11.25rem,0.5fr)]">
                  <Input
                    id="admin-user-search"
                    label="البحث في الموظفين"
                    type="search"
                    placeholder="الاسم أو الرقم الوظيفي أو البريد أو القسم"
                    value={userSearch}
                    onChange={event => setUserSearch(event.target.value)}
                    inputClassName="bg-white shadow-pc-card"
                    endAdornment={
                      <Search aria-hidden="true" className="pointer-events-none h-4 w-4 text-pc-gray-600" />
                    }
                  />
                  <Select
                    id="admin-user-role"
                    placeholder={null}
                    label="الدور"
                    value={userRoleFilter}
                    onChange={setUserRoleFilter}
                    options={[
                      { value: 'all', label: 'كل الأدوار' },
                      { value: 'admin', label: 'مدير النظام' },
                      { value: 'deputy', label: 'المدير التنفيذي' },
                      { value: 'pm', label: 'مدير مشروع' },
                    ]}
                  />
                  <Select
                    id="admin-user-status"
                    placeholder={null}
                    label="الحالة"
                    value={userStatusFilter}
                    onChange={setUserStatusFilter}
                    options={[
                      { value: 'all', label: 'كل الحالات' },
                      { value: 'active', label: 'نشط' },
                      { value: 'disabled', label: 'موقوف' },
                    ]}
                  />
                  <div className="xl:hidden">
                    <Select
                      id="admin-user-sort"
                      placeholder={null}
                      label="ترتيب النتائج"
                      value={`${userSort.key}:${userSort.dir}`}
                      onChange={value => setUserSort(parseSort(value))}
                      options={[
                        { value: 'name:asc', label: 'الاسم: أ–ي' },
                        { value: 'name:desc', label: 'الاسم: ي–أ' },
                        { value: 'dept:asc', label: 'حسب القسم' },
                        { value: 'status:asc', label: 'حسب الحالة' },
                      ]}
                    />
                  </div>
                </div>
              </AdminFilterPanel>
              <Table
                caption="قائمة الموظفين مع أدوارهم وأقسامهم وحالة حساباتهم"
                cardColumns={2}
                columns={userColumns}
                rows={usersList.rows}
                rowKey={u => u.id}
                loading={usersList.loading}
                unit="employee"
                server={{
                  page: usersList.page,
                  pageSize: usersList.pageSize,
                  total: usersList.total,
                  onPageChange: usersList.setPage,
                  sort: userSort,
                  onSortChange: setUserSort,
                  refreshing: usersList.refreshing,
                }}
                cardTitleKey="name"
                emptyTitle={usersEmpty.title}
                emptyDescription={usersEmpty.description}
                emptyAction={
                  usersTotal === 0
                    ? <Button variant="secondary" onClick={() => setShowAddUser(true)}>إضافة موظف جديد</Button>
                    : <Button variant="secondary" onClick={resetFilters}>إزالة المرشحات</Button>
                }
              />
            </section>
          )}

        {editProject && (
          <EditProjectModal
            project={editProject}
            onClose={() => setEditProject(null)}
            onSaved={() => { setEditProject(null); notifyOk('تم تحديث بيانات المشروع'); fetchAll() }}
          />
        )}

        {editUser && (
          <EditUserModal
            user={editUser}
            onClose={() => setEditUser(null)}
            onSaved={text => { setEditUser(null); notifyOk(text); fetchAll() }}
          />
        )}

        {/* Modal: Add Project */}
        {showAddProject && (
          <Modal
            title="إضافة مشروع جديد"
            maxWidth={520}
            onClose={() => { setShowAddProject(false); setProjectTried(false); setProjectServerErrors({}) }}
            onSave={addProject}
            saveLabel="حفظ المشروع"
            saveLoading={projectSaving}
          >
            <div className="flex flex-col gap-pc-m">
              <FormRequiredHint />
              <FormErrorSummary errors={projectErrors} autoFocus={false} />
              <Input
                id="new-project-name"
                label="اسم المشروع"
                required
                maxLength={PROJECT_NAME_MAX}
                placeholder="اسم المشروع"
                value={newProject.name}
                onChange={e => updateNewProject({ name: e.target.value })}
                error={projectErrorMap.name}
              />
              <TextArea
                id="new-project-desc"
                label="الوصف (اختياري)"
                rows={3}
                maxLength={4000}
                placeholder="وصف مختصر"
                value={newProject.description}
                onChange={e => updateNewProject({ description: e.target.value })}
              />
              <ProjectMetaFields
                idPrefix="new-project"
                value={newProject}
                onChange={meta => updateNewProject(meta)}
                errors={projectErrorMap}
              />
              <ScheduleFields
                idPrefix="new-project"
                value={newProject}
                onChange={schedule => updateNewProject(schedule)}
                errors={projectErrorMap}
              />
            </div>
          </Modal>
        )}

        {/* Modal: Add User */}
        {showAddUser && (
          <Modal
            title="إضافة موظف جديد"
            subtitle="أدخل بيانات الموظف وكلمة مرور دخوله"
            maxWidth={680}
            onClose={() => {
              setShowAddUser(false)
              setUserTried(false)
              setUserServerErrors({})
              setNewUser(EMPTY_USER)
            }}
            onSave={addUser}
            saveLabel="حفظ الموظف"
            saveLoading={userSaving}
          >
            <div className="flex flex-col gap-pc-m">
              <FormRequiredHint />
              <FormErrorSummary errors={userErrors} autoFocus={false} />
              <Input
                id="new-user-employee-number"
                label="الرقم الوظيفي"
                required
                inputMode="numeric"
                dir="ltr"
                maxLength={20}
                placeholder="أدخل الرقم الوظيفي"
                value={newUser.employee_number}
                onChange={event => updateNewUser({ employee_number: event.target.value })}
                error={userErrorMap.employee_number}
              />

              <Input
                id="new-user-name"
                label="الاسم الكامل"
                required
                maxLength={USER_NAME_MAX}
                placeholder="الاسم الكامل"
                value={newUser.full_name}
                onChange={e => updateNewUser({ full_name: e.target.value })}
                error={userErrorMap.full_name}
              />
              <Input
                id="new-user-email"
                label="البريد الإلكتروني"
                required
                type="email"
                inputMode="email"
                maxLength={USER_EMAIL_MAX}
                placeholder="name@example.com"
                value={newUser.email}
                onChange={e => updateNewUser({ email: e.target.value })}
                error={userErrorMap.email}
              />
              <div className="grid gap-pc-m sm:grid-cols-2">
                <Input
                  id="new-user-job-title"
                  label="المسمى الوظيفي"
                  placeholder="اختياري"
                  value={newUser.job_title}
                  onChange={event => updateNewUser({ job_title: event.target.value })}
                />
                <Input
                  id="new-user-mobile"
                  label="رقم الجوال"
                  type="tel"
                  dir="ltr"
                  placeholder="اختياري"
                  value={newUser.mobile_no}
                  onChange={event => updateNewUser({ mobile_no: event.target.value })}
                />
              </div>
              <Input
                id="new-user-password"
                label="كلمة المرور"
                required
                type="password"
                autoComplete="new-password"
                help="8 محارف على الأقل، وليست مسافات فقط."
                value={newUser.password_hash}
                onChange={e => updateNewUser({ password_hash: e.target.value })}
                error={userErrorMap.password_hash}
              />
              <Input
                id={CONFIRM_FIELD_ID}
                label="تأكيد كلمة المرور"
                required
                type="password"
                autoComplete="new-password"
                value={newUser.confirm_password}
                onChange={e => updateNewUser({ confirm_password: e.target.value })}
                error={userErrorMap.confirm_password}
              />
              <Input
                id="new-user-dept"
                label="الإدارة / القسم"
                maxLength={USER_DEPARTMENT_MAX}
                placeholder="اختياري"
                value={newUser.department}
                onChange={e => updateNewUser({ department: e.target.value })}
                error={userErrorMap.department}
              />
              <Select
                id="new-user-role"
                label="الدور"
                required
                value={newUser.role}
                onChange={v => updateNewUser({ role: v })}
                error={userErrorMap.role}
                options={[
                  { value: 'pm', label: 'مدير مشروع' },
                  { value: 'deputy', label: 'المدير التنفيذي' },
                  { value: 'admin', label: 'مدير النظام' },
                ]}
              />
            </div>
          </Modal>
        )}

        {/* Modal: Assign */}
        {showAssign && assignProject && (
          <Modal
            title="إسناد موظفين للمشروع"
            subtitle={assignProject.name}
            maxWidth={540}
            onClose={() => setShowAssign(false)}
            footer={
              <Button onClick={() => setShowAssign(false)} block>
                إغلاق نافذة الإسناد
              </Button>
            }
          >
            <section className="mb-pc-l">
              <h3 className="mb-pc-s text-sm font-semibold text-pc-brand-800">
                الموظفون المسندون حاليًا
              </h3>
              {assignments.length === 0 ? (
                <p className="rounded-pc-md bg-pc-gray-50 p-pc-m text-center text-sm text-pc-gray-600">
                  لم يتم إسناد أي موظف لهذا المشروع بعد
                </p>
              ) : (
                <ul className="flex flex-col gap-pc-xs">
                  {assignedPage.visible.map(a => (
                    <li key={a.user_id} className="flex items-center justify-between gap-pc-s rounded-pc-md border border-pc-brand-200 bg-pc-brand-50 px-pc-s py-pc-s">
                      <div className="min-w-0">
                        <p className="text-sm font-medium text-pc-gray-900">{a.full_name}</p>
                        <p className="mt-1 text-sm text-pc-gray-600">{a.email}</p>
                      </div>
                      <Button variant="danger-outline" size="sm" className="shrink-0" onClick={() => removeAssign(a.user_id)}>
                        إزالة{' '}
                        <span className="sr-only">{a.full_name} من المشروع</span>
                      </Button>
                    </li>
                  ))}
                </ul>
              )}
              <Pagination
                page={assignedPage.page}
                pageCount={assignedPage.pageCount}
                total={assignedPage.total}
                onPageChange={assignedPage.setPage}
                unit="employee"
                ariaLabel="تصفّح الموظفين المسندين للمشروع"
              />
            </section>

            <section>
              <h3 className="mb-pc-s text-sm font-semibold text-pc-info-800">إضافة موظف للمشروع</h3>
              {unassignedUsers.length > 0 && (
                <div className="mb-pc-s">
                  <Input
                    id="assign-search"
                    label="البحث عن موظف"
                    type="search"
                    placeholder="الاسم أو البريد أو القسم"
                    value={assignSearch}
                    onChange={event => setAssignSearch(event.target.value)}
                    endAdornment={<Search aria-hidden="true" className="pointer-events-none h-4 w-4 text-pc-gray-600" />}
                  />
                  <p className="mt-pc-xs text-sm text-pc-gray-600" aria-live="polite">
                    {assignQuery
                      ? `${formatCount(visibleUnassigned.length, 'employee')} من أصل ${unassignedUsers.length}`
                      : `${formatCount(unassignedUsers.length, 'employee')} متاح للإسناد`}
                  </p>
                </div>
              )}
              {renderUnassigned()}
              <Pagination
                page={unassignedPage.page}
                pageCount={unassignedPage.pageCount}
                total={unassignedPage.total}
                onPageChange={unassignedPage.setPage}
                unit="employee"
                ariaLabel="تصفّح الموظفين المتاحين للإسناد"
              />
            </section>
          </Modal>
        )}

        {confirmState && (
          <ConfirmModal
            title={confirmState.title}
            message={confirmState.message}
            confirmLabel={confirmState.confirmLabel}
            destructive={confirmState.destructive ?? true}
            irreversibleNote={confirmState.irreversibleNote}
            loading={confirmBusy}
            onConfirm={runConfirm}
            onClose={() => setConfirmState(null)}
          />
        )}
      </div>
    </AppShell>
  )
}
