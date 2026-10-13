import { Link, NavLink } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { initials } from '../lib/format'

export function Logo() {
  return (
    <Link to="/" className="logo" aria-label="Pitwall — início">
      <span className="mk" aria-hidden="true" />
      PIT<b>WALL</b>
    </Link>
  )
}

export function TopBar() {
  const { user, logout } = useAuth()

  return (
    <header className="bar">
      <div className="bar-in">
        <Logo />
        {user && (
          <nav className="nav" aria-label="Principal">
            <NavLink to="/" end>Painel</NavLink>
            <NavLink to="/corridas">Corridas</NavLink>
          </nav>
        )}
        {user && (
          <div className="bar-right">
            <span className="user-chip">
              <span className="ava" aria-hidden="true">{initials(user.displayName)}</span>
              <span className="name">{user.displayName}</span>
            </span>
            <button type="button" className="btn btn-ghost btn-sm" onClick={logout}>
              Sair
            </button>
          </div>
        )}
      </div>
    </header>
  )
}
