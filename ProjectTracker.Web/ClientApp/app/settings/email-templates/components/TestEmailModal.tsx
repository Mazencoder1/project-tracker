'use client'

import { Input, Modal } from '@/app/components/ui'

export function TestEmailModal({
  email,
  error,
  sending,
  onEmailChange,
  onSend,
  onClose,
}: Readonly<{
  email: string
  error: string
  sending: boolean
  onEmailChange: (value: string) => void
  onSend: () => void
  onClose: () => void
}>) {
  return (
    <Modal
      title="إرسال بريد تجريبي"
      subtitle="تُرسل الرسالة الحالية ببيانات تجريبية إلى البريد أدناه فقط."
      onClose={onClose}
      onSave={onSend}
      saveLabel="إرسال"
      saveLoading={sending}
      saveDisabled={email.trim().length === 0}
    >
      <Input
        id="tpl-test-email"
        label="البريد الإلكتروني المستلم"
        type="email"
        dir="ltr"
        required
        autoFocus
        value={email}
        onChange={e => onEmailChange(e.target.value)}
        error={error || undefined}
        onKeyDown={e => {
          if (e.key === 'Enter') {
            e.preventDefault()
            onSend()
          }
        }}
      />
    </Modal>
  )
}
