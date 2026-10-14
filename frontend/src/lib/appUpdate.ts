import { useEffect, useState } from 'react'
import { api, apiUrl } from '../api/client'
import { getAppInfo, isNative } from './native'

/** GET /api/app/android: a versão mais nova liberada do app. Sem APK publicado, `available` vem false. */
export interface AndroidRelease {
  available: boolean
  version: string | null
  versionCode: number | null
  notes: string | null
  publishedAt: string | null
  sizeBytes: number | null
  downloadUrl: string
}

interface UpdateState {
  latest: AndroidRelease | null
  installed: { version: string; build: number } | null
  /** Só no app: a versão liberada tem versionCode maior que a instalada. */
  updateAvailable: boolean
  loading: boolean
  /** Endereço completo do download (no app o link abre no navegador do celular, que baixa e instala). */
  downloadHref: string
}

// Uma consulta por abertura do app/site, compartilhada por quem precisar (aviso, menu, página do app).
let pending: Promise<Omit<UpdateState, 'loading'>> | null = null

function load() {
  pending ??= Promise.all([
    api<AndroidRelease>('/app/android').catch(() => null),
    getAppInfo(),
  ]).then(([release, installed]) => {
    const latest = release?.available ? release : null
    return {
      latest,
      installed,
      updateAvailable: !!(isNative && installed && latest?.versionCode && latest.versionCode > installed.build),
      downloadHref: apiUrl(latest?.downloadUrl ?? '/api/app/android/download'),
    }
  })
  return pending
}

/**
 * Versão do app: no APK compara a instalada com a mais nova liberada (pelo versionCode, inteiro que só cresce); no
 * site `installed` é nulo e nunca há "atualização disponível" — lá só se oferece o download.
 */
export function useAppUpdate(): UpdateState {
  const [state, setState] = useState<UpdateState>({
    latest: null, installed: null, updateAvailable: false, loading: true, downloadHref: apiUrl('/api/app/android/download'),
  })
  useEffect(() => {
    let alive = true
    void load().then((s) => { if (alive) setState({ ...s, loading: false }) })
    return () => { alive = false }
  }, [])
  return state
}

/** Tamanho do APK: 12.3 MB -> "12,3 MB". */
export const fmtSize = (bytes: number | null) => (bytes ? `${(bytes / 1024 / 1024).toFixed(1).replace('.', ',')} MB` : '—')
