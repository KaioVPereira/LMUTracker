import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthContext'

interface FollowsValue {
  ids: Set<string>
  isFollowing: (driverId: string | null | undefined) => boolean
  toggle: (driverId: string) => Promise<void>
}

const Ctx = createContext<FollowsValue>({ ids: new Set(), isFollowing: () => false, toggle: async () => {} })

/** Quem o usuário logado segue (para o botão do perfil e o destaque nas tabelas). Sem login, ninguém. */
export function FollowsProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  const [ids, setIds] = useState<Set<string>>(new Set())
  const uid = user?.id

  useEffect(() => {
    let alive = true
    if (!uid) return
    api<string[]>('/lmu/follows/ids').then((list) => { if (alive) setIds(new Set(list)) }).catch(() => {})
    return () => { alive = false }
  }, [uid])

  const toggle = useCallback(async (driverId: string) => {
    const on = ids.has(driverId)
    // Otimista: muda na hora; se a Api falhar, volta.
    setIds((s) => { const n = new Set(s); if (on) n.delete(driverId); else n.add(driverId); return n })
    try {
      await api(`/lmu/follows/${driverId}`, { method: on ? 'DELETE' : 'PUT' })
    } catch {
      setIds((s) => { const n = new Set(s); if (on) n.add(driverId); else n.delete(driverId); return n })
    }
  }, [ids])

  const value = useMemo<FollowsValue>(() => ({
    ids: uid ? ids : new Set(),
    isFollowing: (id) => !!uid && !!id && ids.has(id),
    toggle,
  }), [ids, toggle, uid])
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export const useFollows = () => useContext(Ctx)
