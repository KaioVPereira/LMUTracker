import { Link, Navigate } from 'react-router-dom'
import type { MyDriver } from '../api/types'
import { EmptyState, ErrorBox, Loading } from '../components/Bits'
import { LinkBanner, UnlinkSteamButton } from '../components/SteamLink'
import { TopBar } from '../components/TopBar'
import { useApi } from '../lib/useApi'

/** Atalho "Perfil": vai para a página de piloto da pessoa logada; sem vínculo ou sem corrida, explica o porquê. */
export function MyProfilePage() {
  const { data, loading, error, reload } = useApi<MyDriver>('/lmu/me')

  if (data?.driverId) return <Navigate to={`/pilotos/${data.driverId}`} replace />

  return (
    <>
      <TopBar />
      <div className="wrap">
        {loading && <Loading what="Procurando seu piloto…" rows={2} />}
        {error && <ErrorBox message={error} onRetry={reload} />}
        {data && !data.linked && (
          <>
            <div className="page-head"><span className="eyebrow">Meu perfil</span><h1>Seu piloto</h1></div>
            <LinkBanner />
            <p className="note">O perfil é montado a partir das suas corridas — e é pelo SteamID que encontramos você nelas.</p>
          </>
        )}
        {data && data.linked && !data.driverId && (
          <EmptyState title="Ainda não vimos você correr"
            action={<span className="chips"><Link to="/" className="btn">Ver a agenda</Link><UnlinkSteamButton onDone={reload} /></span>}>
            Sua Steam está vinculada. O Pitwall registra as corridas oficiais desde que o scanner começou a rodar:
            depois da sua próxima corrida, o perfil aparece aqui uns 10 minutos após a bandeira.
          </EmptyState>
        )}
      </div>
    </>
  )
}
