// Note: عقد البيانات المشترك للوحة القيادة التنفيذية (المتطلبات-Executive-Dashboard.md).
// يستخدمه الخادم (lib/portfolio/calc.ts + app/api/portfolio) والواجهة (app/meeting, app/decisions).
// لا تغيّر أسماء الحقول دون تحديث.
// هذا الملف بلا أي import حتى يمكن استيراده من اختبارات node مباشرة.

/** صحة التنفيذ — منفصلة عن حالة المشروع التشغيلية (projects.status). */
export type HealthStatus =
  | 'on_track' // ضمن الخطة
  | 'needs_attention' // يحتاج متابعة
  | 'delayed' // متأخر
  | 'at_risk' // متعثر
  | 'stopped' // متوقف
  | 'completed' // مكتمل (للمشاريع ذات الحالة completed)
  | 'unknown' // غير متوفر: لا توجد بيانات كافية للحكم

/** صحة يُسمح لمدير المشروع باختيارها في التحديث الأسبوعي. */
export type ReportedHealth = 'on_track' | 'needs_attention' | 'delayed' | 'at_risk' | 'stopped'

export type ProjectPriority = 'high' | 'medium' | 'low'

/** مصدر الإنجاز الفعلي: من مهام المراحل، أو من آخر تحديث أسبوعي (عند غياب المراحل). */
export type ActualSource = 'tasks' | 'reported'
/** مصدر الإنجاز المخطط: خط أساس من التحديث الأسبوعي، أو تقدير خطي من الزمن (يُوسم «تقديري»). */
export type PlannedSource = 'reported' | 'estimated'

export type RiskType = 'risk' | 'issue' | 'blocker' // خطر / تعثر / عائق
export type RiskSeverity = 'low' | 'medium' | 'high' | 'critical'
export type RiskProbability = 'low' | 'medium' | 'high'
export type RiskStatus = 'open' | 'mitigating' | 'closed'

export type ActionType = 'decision' | 'support' | 'escalation' | 'corrective'
/** الحالة المخزنة. «متأخر» تُشتق (isOverdue) ولا تُخزَّن. */
export type ActionStatus = 'new' | 'in_progress' | 'completed' | 'cancelled'
export type DecisionStatus = 'new' | 'under_review' | 'taken'

export type MilestoneState = 'not_started' | 'in_progress' | 'completed' | 'delayed'

export interface AppSettings {
  /** عدد الأيام التي يُعد بعدها آخر تحديث أسبوعي قديمًا. افتراضي 14. */
  staleUpdateDays: number
  /** حد «ضمن الخطة»: الانحراف >= هذه القيمة. افتراضي -5. */
  varianceOnTrack: number
  /** حد «يحتاج متابعة»: الانحراف >= هذه القيمة (وأقل من حد ضمن الخطة). افتراضي -15. */
  varianceAttention: number
  /** عدد الأيام قبل نهاية المشروع التي يُعد فيها «قريبًا من موعده». افتراضي 30. */
  nearDeadlineDays: number
}

export interface PersonRef {
  id: string
  full_name: string
}

interface MilestoneSummary {
  id: string
  name: string
  week_start: number
  week_end: number
  color: string | null
  display_order: number
  progress: number
  /** تاريخ بداية/نهاية مشتق من start_date + الأسابيع، أو null إن غاب تاريخ البداية. ISO yyyy-mm-dd */
  start_date: string | null
  end_date: string | null
  state: MilestoneState
  /** أسابيع التأخير (0 إن لم تتأخر). */
  delayWeeks: number
  /** الإنجاز المخطط للمرحلة (تقديري خطي داخل نطاقها) أو null إن تعذّر. */
  plannedProgress: number | null
  tasksCount: number
  isCurrent: boolean
}

export interface PortfolioProject {
  id: string
  name: string
  description: string | null
  project_code: string | null
  department: string | null
  priority: ProjectPriority | null
  status: string // active | completed | delayed | cancelled
  start_date: string | null
  end_date: string | null
  total_weeks: number | null
  managers: PersonRef[]
  assignedToMe: boolean

  milestonesCount: number
  completedMilestones: number
  tasksCount: number
  delayedTasks: number

  actualProgress: number | null
  actualSource: ActualSource | null
  plannedProgress: number | null
  plannedSource: PlannedSource | null
  /** فعلي − مخطط، أو null إن غاب أحدهما. */
  variance: number | null

  health: HealthStatus
  /** أسباب عربية مقروءة لتصنيف الصحة (تُعرض كسبب التنبيه). */
  healthReasons: string[]

  isOverdue: boolean
  /** الأيام حتى تاريخ النهاية بإشارة (سالب = تجاوز)، أو null إن لم يُحدد. */
  daysToEnd: number | null
  /** الأسبوع المنقضي من المشروع (1..)، أو null إن غاب تاريخ البداية. */
  elapsedWeek: number | null
  /** من آخر تحديث أسبوعي، أو null. */
  forecastEndDate: string | null

  currentMilestone: MilestoneSummary | null
  nextMilestone: MilestoneSummary | null

  /** أحدث وقت تغيير فعلي (آخر تحديث أسبوعي أو آخر تحديث مهمة) ISO، أو null. */
  lastActivityAt: string | null
  /** تاريخ آخر تحديث أسبوعي (reporting_date)، أو null إن لم يُسجَّل أي تحديث. */
  lastReportDate: string | null
  /** true إن لم يوجد تحديث أسبوعي أو كان أقدم من staleUpdateDays. */
  isStale: boolean

  /** null = لم يُدخل أي سجل مخاطر لهذا المشروع إطلاقًا (لا تعرض 0). */
  openRisks: number | null
  /** مخاطر/تعثرات مفتوحة بخطورة high أو critical. */
  highRisks: number
  /** عوائق (blocker) مفتوحة بخطورة critical. */
  criticalBlockers: number

  executiveSummary: string | null
  decisionRequired: string | null
  decisionNeededBy: string | null
  decisionStatus: DecisionStatus | null
  supportRequired: string | null
  needsDecision: boolean
  needsSupport: boolean

  openActions: number
  overdueActions: number

  /** ترتيب «الأولوية في الاجتماع» (1 أعلى)، أو null إن لم يحتج تدخلًا. */
  attentionRank: number | null
  attentionReason: string | null
}

interface PortfolioKpis {
  total: number
  onTrack: number
  needsAttention: number
  /** متأخر + متعثر + متوقف */
  delayedOrAtRisk: number
  needsDecisionOrSupport: number
  unknown: number
  completed: number
  /** متوسط الإنجاز الفعلي للمشاريع ذات القيمة المتوفرة، أو null. */
  averageProgress: number | null
}

export interface PortfolioResponse {
  generatedAt: string
  settings: AppSettings
  kpis: PortfolioKpis
  projects: PortfolioProject[]
}

export interface ProjectUpdate {
  id: string
  project_id: string
  reporting_date: string
  executive_summary: string | null
  actual_progress: number | null
  planned_progress: number | null
  variance_percent: number | null
  health_status: ReportedHealth
  current_milestone_id: string | null
  next_milestone_id: string | null
  forecast_end_date: string | null
  delay_reason: string | null
  support_required: string | null
  support_from: string | null
  support_impact: string | null
  support_due_date: string | null
  decision_required: string | null
  decision_from: string | null
  decision_needed_by: string | null
  decision_status: DecisionStatus | null
  updated_by: string | null
  updated_by_name: string | null
  created_at: string
}

export interface RiskIssue {
  id: string
  project_id: string
  project_name?: string
  milestone_id: string | null
  type: RiskType
  title: string
  description: string | null
  severity: RiskSeverity
  probability: RiskProbability | null
  status: RiskStatus
  owner_id: string | null
  owner_name: string | null
  due_date: string | null
  mitigation: string | null
  created_by: string | null
  created_at: string
  updated_at: string
}

export interface MeetingAction {
  id: string
  project_id: string
  project_name?: string
  meeting_date: string
  action_type: ActionType
  description: string
  owner_id: string | null
  owner_name: string | null
  supporting_department: string | null
  due_date: string | null
  status: ActionStatus
  /** مشتق: غير مكتمل/ملغى وتاريخ الاستحقاق قبل اليوم. */
  isOverdue: boolean
  decision_notes: string | null
  closure_notes: string | null
  created_by: string | null
  created_by_name: string | null
  created_at: string
  updated_at: string
}

export interface ProjectSummaryResponse {
  project: PortfolioProject
  milestones: MilestoneSummary[]
  latestUpdate: ProjectUpdate | null
  recentUpdates: ProjectUpdate[]
  risks: RiskIssue[]
  actions: MeetingAction[]
  /** هل يملك المستخدم الحالي صلاحية الكتابة على هذا المشروع: مدير المشروع المُسند وحده — المدير التنفيذي (admin) يشاهد فقط. */
  canWrite: boolean
  /** هل المستخدم مدير نظام (يسجّل قرارات الاجتماع ويرى سجل التدقيق). */
  isAdmin: boolean
  settings: AppSettings
}

export type ValidationResult<T> =
  | { ok: true; value: T }
  | { ok: false; errors: Record<string, string> }

/** الحقول التي يرسلها مدير المشروع في التحديث الأسبوعي (بعد التحقق والتقليم). */
export interface ProjectUpdateInput {
  reporting_date: string
  executive_summary: string | null
  actual_progress: number | null
  planned_progress: number | null
  health_status: ReportedHealth
  current_milestone_id: string | null
  next_milestone_id: string | null
  forecast_end_date: string | null
  delay_reason: string | null
  support_required: string | null
  support_from: string | null
  support_impact: string | null
  support_due_date: string | null
  decision_required: string | null
  decision_from: string | null
  decision_needed_by: string | null
  decision_status: DecisionStatus | null
}

export interface RiskInput {
  type: RiskType
  title: string
  description: string | null
  severity: RiskSeverity
  probability: RiskProbability | null
  status: RiskStatus
  owner_id: string | null
  milestone_id: string | null
  due_date: string | null
  mitigation: string | null
}

export interface MeetingActionInput {
  meeting_date: string
  action_type: ActionType
  description: string
  owner_id: string | null
  supporting_department: string | null
  due_date: string | null
  status: ActionStatus
  decision_notes: string | null
  closure_notes: string | null
}

