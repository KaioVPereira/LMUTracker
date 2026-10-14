import { useState } from 'react'
import { api } from '../api/client'

/**
 * Paginação "mostrar mais" sobre um endpoint com `skip`/`take`: a 1ª página vem do `useApi` da tela, as
 * seguintes são somadas aqui. Trocar de filtro (outro `path`) volta para só a 1ª página. A próxima página começa
 * depois da posição da última linha (não da contagem de linhas), então uma linha que faltar não desalinha nada.
 */
export function useLoadMore<T, R extends { rank: number }>(path: string | null, first: T | null, rowsOf: (d: T) => R[], totalOf: (d: T) => number) {
  const [more, setMore] = useState<{ path: string; rows: R[] } | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const extra = more && more.path === path ? more.rows : []
  const rows = first ? [...rowsOf(first), ...extra] : []
  const total = first ? totalOf(first) : 0
  const shown = rows.length ? rows[rows.length - 1].rank : 0

  const loadMore = () => {
    if (!path || busy === path) return
    setBusy(path)
    api<T>(`${path}${path.includes('?') ? '&' : '?'}skip=${shown}`)
      .then((d) => setMore({ path, rows: [...extra, ...rowsOf(d)] }))
      .catch(() => {})
      .finally(() => setBusy(null))
  }

  return { rows, total, hasMore: !!first && shown < total, loadMore, loading: busy === path }
}
