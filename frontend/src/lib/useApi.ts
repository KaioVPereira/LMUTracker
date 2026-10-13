import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api/client'

interface Loaded<T> {
  key: string
  data: T | null
  error: string | null
  notFound: boolean
}

/**
 * Carrega um GET da Api e recarrega quando o caminho muda. path null = não carrega.
 * Os dados ficam marcados com o caminho de onde vieram: ao trocar de corrida/piloto, a tela nunca mostra
 * por um instante os dados da anterior — enquanto o novo não chega, `loading` é true e `data` é null.
 */
export function useApi<T>(path: string | null) {
  const [loaded, setLoaded] = useState<Loaded<T> | null>(null)
  const [nonce, setNonce] = useState(0)
  const key = path === null ? null : `${path}#${nonce}`

  useEffect(() => {
    if (path === null || key === null) return
    let alive = true
    api<T>(path)
      .then((data) => { if (alive) setLoaded({ key, data, error: null, notFound: false }) })
      .catch((e) => {
        if (!alive) return
        setLoaded({
          key,
          data: null,
          error: e instanceof ApiError ? e.message : 'Não foi possível carregar.',
          notFound: e instanceof ApiError && e.status === 404,
        })
      })
    return () => { alive = false }
  }, [path, key])

  const reload = useCallback(() => setNonce((n) => n + 1), [])
  const current = loaded && loaded.key === key ? loaded : null
  return {
    data: current?.data ?? null,
    error: current?.error ?? null,
    notFound: current?.notFound ?? false,
    loading: path !== null && current === null,
    reload,
  }
}
