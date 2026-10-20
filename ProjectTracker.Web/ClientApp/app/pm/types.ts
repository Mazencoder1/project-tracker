export interface Project {
  id: string
  name: string
  status: string
  total_weeks: number
  start_date?: string
  end_date?: string
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
  status: string
  milestone_id: string
}

export interface Note {
  id: string
  type: string
  content: string
  milestone_id: string
}

export type PMInfoTab = 'support' | 'details' | 'progress'
