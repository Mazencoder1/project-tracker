// Note: تم فصل أنواع بيانات صفحة meeting هنا حتى تبقى العقود المشتركة واضحة بين الصفحة والمكونات والحسابات.
export interface Project {
  id: string
  name: string
  status: string
  total_weeks: number
  start_date: string
  end_date: string
  description?: string
  project_code?: string | null
  department?: string | null
}

export interface Milestone {
  id: string
  name: string
  week_start: number
  week_end: number
  color: string
  progress: number
  tasks_count?: number
}

export interface Task {
  id: string
  name: string
  week_start: number
  week_end: number
  progress: number
  milestone_id: string
  status?: string
  updated_at?: string
}

export interface Assignment {
  user_id: string
  full_name: string
  email: string
  role: string
}

export interface Note {
  id: string
  type: string
  content: string
  milestone_id: string
}
