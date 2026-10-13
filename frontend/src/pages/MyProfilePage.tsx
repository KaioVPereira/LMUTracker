import { Link, Navigate } from 'react-router-dom'
import type { MyDriver } from '../api/types'
import { EmptyState, ErrorBox, Loading } from '../components/Bits'
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
          <EmptyState title="Vincule sua conta Steam" action={<Link to="/" className="btn btn-primary">Vincular no início</Link>}>
            O perfil é montado a partir das suas corridas — e é pelo SteamID que encontramos você nelas.
          </EmptyState>
        )}
        {data && data.linked && !data.driverId && (
          <EmptyState title="Ainda não vimos você correr" action={<Link to="/" className="btn">Ver a agenda</Link>}>
            Sua Steam está vinculada. Depois da sua próxima corrida oficial, o perfil aparece aqui.
          </EmptyState>
        )}
      </div>
    </>
  )
}
