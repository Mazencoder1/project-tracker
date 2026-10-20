import {
  AlertCircle,
  AlertTriangle,
  CheckCircle2,
  ChevronsDown,
  ChevronsUp,
  CircleCheckBig,
  CircleDot,
  Clock,
  Equal,
  HelpCircle,
  Loader,
  OctagonX,
  ShieldAlert,
  type LucideIcon,
} from 'lucide-react'
import type {
  HealthStatus,
  ProjectPriority,
  ReportedHealth,
  RiskProbability,
  RiskSeverity,
  RiskStatus,
  RiskType,
} from './types'

export interface LabelStyle {
  label: string
  /** خلفية + لون نص الشارة (className جاهز). */
  badge: string
  icon: LucideIcon
}

/* ───────────────────────────── صحة التنفيذ ───────────────────────────── */
export const HEALTH: Record<HealthStatus, LabelStyle> = {
  on_track: { label: 'ضمن الخطة', badge: 'bg-pc-success-100 text-pc-success-800', icon: CheckCircle2 },
  needs_attention: { label: 'يحتاج متابعة', badge: 'bg-pc-warning-100 text-pc-warning-800', icon: AlertCircle },
  delayed: { label: 'متأخر', badge: 'bg-pc-error-100 text-pc-error-800', icon: Clock },
  at_risk: { label: 'متعثر', badge: 'bg-pc-error-100 text-pc-error-800', icon: AlertTriangle },
  stopped: { label: 'متوقف', badge: 'bg-pc-error-600 text-white', icon: OctagonX },
  completed: { label: 'مكتمل', badge: 'bg-pc-brand-50 text-pc-brand-800', icon: CircleCheckBig },
  unknown: { label: 'غير متوفر', badge: 'bg-pc-gray-100 text-pc-gray-700', icon: HelpCircle },
}

/** الصحة التي يختارها مدير المشروع في التحديث الأسبوعي. */
export const REPORTED_HEALTH_ORDER: ReportedHealth[] = ['on_track', 'needs_attention', 'delayed', 'at_risk', 'stopped']

/** الصحة التي تستوجب ذكر سبب التأخير (نفس قاعدة validateProjectUpdate). */
export const HEALTH_REQUIRES_REASON: ReportedHealth[] = ['delayed', 'at_risk', 'stopped']

/* ───────────────────────────── الأولوية ───────────────────────────── */
export const PRIORITY: Record<ProjectPriority, LabelStyle> = {
  high: { label: 'عالية', badge: 'bg-pc-error-100 text-pc-error-800', icon: ChevronsUp },
  medium: { label: 'متوسطة', badge: 'bg-pc-warning-100 text-pc-warning-800', icon: Equal },
  low: { label: 'منخفضة', badge: 'bg-pc-gray-100 text-pc-gray-700', icon: ChevronsDown },
}
export const PRIORITY_ORDER: ProjectPriority[] = ['high', 'medium', 'low']

/* ───────────────────────────── المخاطر والتعثرات ───────────────────────────── */
export const RISK_TYPE: Record<RiskType, LabelStyle> = {
  risk: { label: 'خطر', badge: 'bg-pc-warning-100 text-pc-warning-800', icon: ShieldAlert },
  issue: { label: 'تعثر', badge: 'bg-pc-error-100 text-pc-error-800', icon: AlertTriangle },
  blocker: { label: 'عائق', badge: 'bg-pc-error-600 text-white', icon: OctagonX },
}
export const RISK_TYPE_ORDER: RiskType[] = ['risk', 'issue', 'blocker']

export const RISK_SEVERITY: Record<RiskSeverity, LabelStyle> = {
  low: { label: 'منخفضة', badge: 'bg-pc-gray-100 text-pc-gray-700', icon: ChevronsDown },
  medium: { label: 'متوسطة', badge: 'bg-pc-warning-100 text-pc-warning-800', icon: Equal },
  high: { label: 'عالية', badge: 'bg-pc-error-100 text-pc-error-800', icon: ChevronsUp },
  critical: { label: 'حرجة', badge: 'bg-pc-error-600 text-white', icon: AlertTriangle },
}
export const RISK_SEVERITY_ORDER: RiskSeverity[] = ['critical', 'high', 'medium', 'low']

export const RISK_PROBABILITY: Record<RiskProbability, string> = {
  low: 'منخفض',
  medium: 'متوسط',
  high: 'مرتفع',
}
export const RISK_PROBABILITY_ORDER: RiskProbability[] = ['high', 'medium', 'low']

export const RISK_STATUS: Record<RiskStatus, LabelStyle> = {
  open: { label: 'مفتوح', badge: 'bg-pc-error-100 text-pc-error-800', icon: CircleDot },
  mitigating: { label: 'قيد المعالجة', badge: 'bg-pc-warning-100 text-pc-warning-800', icon: Loader },
  closed: { label: 'مغلق', badge: 'bg-pc-success-100 text-pc-success-800', icon: CheckCircle2 },
}
export const RISK_STATUS_ORDER: RiskStatus[] = ['open', 'mitigating', 'closed']

