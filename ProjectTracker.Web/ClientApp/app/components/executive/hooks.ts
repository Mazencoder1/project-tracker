'use client'
import { useEffect, useState } from 'react'
import type { PersonRef } from '@/lib/portfolio/types'
import { apiJson, errorMessage } from './api'

export function usePeople(enabled = true) {
  const [people, setPeople] = useState<PersonRef[]>([])
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(enabled)

  useEffect(() => {
    if (!enabled) return
    let cancelled = false
    apiJson<{ people: PersonRef[] }>('/api/people')
      .then(data => {
        if (!cancelled) setPeople(data.people ?? [])
      })
      .catch(e => {
        if (!cancelled) setError(errorMessage(e, 'تعذّر تحميل قائمة الموظفين.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [enabled])

  return { people, error, loading }
}

export function useSessionRole() {
  const [role, setRole] = useState<string | null>(null)
  const [ready, setReady] = useState(false)
  useEffect(() => {
    let cancelled = false
    fetch('/api/auth/me', { cache: 'no-store' })
      .then(res => (res.ok ? res.json() : null))
      .then((data: { role?: string } | null) => {
        if (!cancelled) setRole(data?.role ?? null)
      })
      .catch(() => {})
      .finally(() => {
        if (!cancelled) setReady(true)
      })
    return () => {
      cancelled = true
    }
  }, [])
  return { role, isAdmin: role === 'admin', ready }
}
