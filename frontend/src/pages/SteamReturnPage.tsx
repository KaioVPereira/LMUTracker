import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import type { UserDto } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { TopBar } from '../components/TopBar'

/** A Steam devolve o navegador aqui com os parâmetros openid.* na URL; a Api confere com a Steam e vincula. */
export function SteamReturnPage() {
  const { refreshUser } = useAuth()
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)
  const started = useRef(false) // a resposta da Steam só pode ser validada uma vez

  useEffect(() => {
    if (started.current) return
    started.current = true

    const params = Object.fromEntries(new URLSearchParams(window.location.search))
    api<UserDto>('/link/steam/verify', { method: 'POST', body: { params } })
      .then(async () => {
        await refreshUser()
        navigate('/', { replace: true, state: { flash: 'Conta Steam vinculada.' } })
      })
      .catch((e) => setError(e instanceof ApiError ? e.message : 'Não foi possível concluir o vínculo.'))
  }, [navigate, refreshUser])

  return (
    <>
      <TopBar />
      <div className="center-page">
        <div className="card">
          {error ? (
            <>
              <h2>Vínculo não concluído</h2>
              <div className="form-error">{error}</div>
              <Link to="/" className="btn">Voltar ao painel</Link>
            </>
          ) : (
            <>
              <h2>Confirmando com a Steam…</h2>
              <p className="note">Só um instante.</p>
            </>
          )}
        </div>
      </div>
    </>
  )
}
