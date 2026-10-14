import { Link, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { useFollows } from '../lib/follows'

/** Seguir / deixar de seguir. Sem login, leva para entrar (e volta para o perfil depois). */
export function FollowButton({ driverId }: { driverId: string }) {
  const { user } = useAuth()
  const { isFollowing, toggle } = useFollows()
  const location = useLocation()
  if (!user) {
    return <Link to="/login" state={{ from: location.pathname }} className="btn btn-sm">Entrar para seguir</Link>
  }
  const on = isFollowing(driverId)
  return (
    <button type="button" className={`btn btn-sm${on ? '' : ' btn-primary'}`} aria-pressed={on} onClick={() => void toggle(driverId)}>
      <svg aria-hidden="true" width="16" height="16" viewBox="0 0 24 24" fill={on ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="2" strokeLinejoin="round"><path d="m12 3 2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1L3.2 9.5l6.1-.9z" /></svg>
      {on ? 'Seguindo' : 'Seguir'}
    </button>
  )
}
