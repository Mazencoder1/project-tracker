'use client'

import { useEffect, useRef, useState, type ReactNode } from 'react'
import { FileDown } from 'lucide-react'
import { Button, useToast } from '@/app/components/ui'
import { exportReportPagesToPdf } from '@/lib/pdfExport'

interface DownloadRequest {
  id: number
  preparedAt: Date
}

export function ReportControls({
  fileName,
  children,
  label = 'حفظ PDF',
}: Readonly<{
  /** نص الزر — «تقرير تنفيذي PDF» في لوحة المؤشرات، و«حفظ PDF» افتراضيًا. */
  label?: string
  fileName: string | ((preparedAt: Date) => string)
  children: ReactNode | ((preparedAt: Date) => ReactNode)
}>) {
  const reportRef = useRef<HTMLDivElement | null>(null)
  const nextRequestRef = useRef(0)
  const startedRequestRef = useRef(0)
  const mountedRef = useRef(true)
  const toast = useToast()
  const [request, setRequest] = useState<DownloadRequest | null>(null)
  const downloading = request !== null

  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
    }
  }, [])

  useEffect(() => {
    if (request === null || startedRequestRef.current === request.id) return
    const root = reportRef.current
    if (!root) return
    const reportRoot = root
    startedRequestRef.current = request.id
    const downloadFileName = typeof fileName === 'function' ? fileName(request.preparedAt) : fileName

    async function download() {
      try {
        await exportReportPagesToPdf(reportRoot, downloadFileName)
        toast.success('تم حفظ التقرير بصيغة PDF.')
      } catch {
        toast.error('تعذّر إنشاء ملف PDF. أعد المحاولة بعد اكتمال تحميل بيانات التقرير.')
      } finally {
        if (mountedRef.current) setRequest(null)
      }
    }

    download()
  }, [fileName, request, toast])

  function savePdf() {
    if (downloading) return
    nextRequestRef.current += 1
    setRequest({ id: nextRequestRef.current, preparedAt: new Date() })
  }

  return (
    <>
      <div className="meeting-no-pdf flex justify-end">
        <Button
          onClick={savePdf}
          loading={downloading}
          loadingLabel="جارٍ حفظ PDF…"
          icon={<FileDown aria-hidden="true" className="h-4 w-4" />}
        >
          {label}
        </Button>
      </div>

      {request && (
        <div ref={reportRef} className="meeting-print-report" aria-hidden="true">
          {typeof children === 'function' ? children(request.preparedAt) : children}
        </div>
      )}
    </>
  )
}
