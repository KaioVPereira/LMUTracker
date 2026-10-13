import { useState } from 'react'
import { api, ApiError } from '../api/client'
import type { UserDto } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { startSteamLink } from '../lib/steam'

export function LinkBanner() {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function start() {
    setBusy(true)
    setError(null)
    try {
      await startSteamLink()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível iniciar o vínculo.')
      setBusy(false)
    }
  }

  return (
    <div className="link-banner">
      <div>
        <h3>Vincule sua conta Steam</h3>
        <p>
          É assim que encontramos você nas corridas. O login acontece no site da Steam e só confirma qual é o
          seu SteamID: não recebemos sua senha.
        </p>
        {error && <div className="form-error" style={{ marginTop: 10 }}>{error}</div>}
      </div>
      <button type="button" className="btn btn-primary" onClick={start} disabled={busy}>
        {busy ? 'Abrindo a Steam…' : 'Entrar com a Steam'}
      </button>
    </div>
  )
}

/** Desfaz o vínculo (pede confirmação: o perfil some até vincular de novo). */
export function UnlinkSteamButton({ onDone }: { onDone?: () => void }) {
  const { refreshUser } = useAuth()
  const [busy, setBusy] = useState(false)

  async function unlink() {
    if (!window.confirm('Desvincular sua conta Steam? Seu perfil deixa de aparecer como seu até vincular de novo.')) return
    setBusy(true)
    try {
      await api<UserDto>('/link/steam', { method: 'DELETE' })
      await refreshUser()
      onDone?.()
    } finally {
      setBusy(false)
    }
  }

  return (
    <button type="button" className="btn btn-ghost btn-sm" onClick={unlink} disabled={busy}>
      Desvincular Steam
    </button>
  )
}
