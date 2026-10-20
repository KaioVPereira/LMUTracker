import { useEffect, useSyncExternalStore } from 'react'
import { api } from '../api/client'
import type { ChangelogEntry } from '../api/types'
import { useAuth } from '../auth/AuthContext'

// Novidades ainda não vistas. Logado: o servidor guarda até onde a pessoa viu (vale no site e no app). Sem login:
// a data fica no navegador — e quem chega pela primeira vez só vê as das últimas 2 semanas, não o histórico todo.
// As duas datas valem juntas: o que já foi dispensado aqui não volta a aparecer depois de entrar na conta.

const SEEN_KEY = 'pitwall.changelog.seenAt'
const FIRST_VISIT_DAYS = 14

const readSeen = () => { try { return localStorage.getItem(SEEN_KEY) } catch { return null } }
const writeSeen = (iso: string) => { try { localStorage.setItem(SEEN_KEY, iso) } catch { /* só nesta sessão */ } }

let state: { unseen: ChangelogEntry[]; loadedFor: string | null } = { unseen: [], loadedFor: null }
const listeners = new Set<() => void>()
const emit = () => listeners.forEach((l) => l())
const subscribe = (l: () => void) => { listeners.add(l); return () => { listeners.delete(l) } }
const snapshot = () => state.unseen

function notSeenHere(list: ChangelogEntry[], firstVisit: boolean) {
  const seen = readSeen()
  const cutoff = seen ? Date.parse(seen) : firstVisit ? Date.now() - FIRST_VISIT_DAYS * 864e5 : 0
  return list.filter((e) => Date.parse(e.date) > cutoff)
}

export const KIND_LABEL: Record<string, string> = { feature: 'Novo', improvement: 'Melhoria', fix: 'Correção' }

export function useUnseenChangelog() {
  const { user, loading } = useAuth()
  const who = loading ? null : user ? `u:${user.id}` : 'anon'

  useEffect(() => {
    if (!who || state.loadedFor === who) return
    state = { unseen: [], loadedFor: who }
    const load = who === 'anon'
      ? api<ChangelogEntry[]>('/changelog').then((all) => notSeenHere(all, true))
      : api<ChangelogEntry[]>('/changelog/unseen').then((list) => notSeenHere(list, false))
    load
      .then((unseen) => { if (state.loadedFor === who) { state = { unseen, loadedFor: who }; emit() } })
      .catch(() => { /* sem novidades nesta visita */ })
  }, [who])

  const unseen = useSyncExternalStore(subscribe, snapshot)
  return { unseen, markSeen: () => markSeen(!!user) }
}

/** Tudo visto: guarda a data aqui e, logado, no servidor. */
export function markSeen(logged: boolean) {
  const latest = Math.max(Date.now(), ...state.unseen.map((e) => Date.parse(e.date)))
  writeSeen(new Date(latest).toISOString())
  if (logged) api('/changelog/seen', { method: 'POST' }).catch(() => { /* tenta de novo na próxima visita */ })
  if (state.unseen.length) { state = { ...state, unseen: [] }; emit() }
}
