import { AppShell } from '@/app/components/ui'
import { MeetingWorkspace } from './components/MeetingWorkspace'

export default function MeetingPage() {
  return (
    <AppShell
      title="متابعة المشاريع"
      subtitle="ملخص سريع للمشاريع، ثم متابعة المراحل والمدة الزمنية للمشروع المختار"
      defaultCollapsed
    >
      <MeetingWorkspace />
    </AppShell>
  )
}
