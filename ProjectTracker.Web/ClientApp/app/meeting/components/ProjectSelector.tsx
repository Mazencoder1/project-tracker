import { Select } from '@/app/components/ui'
import type { Project } from '../types'

interface ProjectSelectorProps {
  projects: Project[]
  selected: Project | null
  onSelectProject: (project: Project) => void
  statusLabel: Record<string, string>
}

export function ProjectSelector({
  projects,
  selected,
  onSelectProject,
  statusLabel,
}: Readonly<ProjectSelectorProps>) {
  return (
    <div className="meeting-no-pdf w-full min-w-[15rem] sm:w-72">
      <Select
        id="meeting-project-select"
        label="المشروع المعروض"
        value={selected?.id ?? ''}
        placeholder="اختر مشروعًا"
        onChange={(id) => {
          const project = projects.find(p => p.id === id)
          if (project) onSelectProject(project)
        }}
        options={projects.map(project => ({
          value: project.id,
          label: `${project.name} — ${statusLabel[project.status] || project.status}`,
        }))}
      />
    </div>
  )
}
