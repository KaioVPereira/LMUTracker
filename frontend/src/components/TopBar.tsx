import { useEffect, useState } from 'react'
import { Link, NavLink, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { useUnseenChangelog } from '../lib/changelog'
import { AccountMenu } from './AccountMenu'
import { CommandPalette } from './CommandPalette'
import { FeedbackWidget } from './FeedbackWidget'

export function Logo() {
  return (
    <Link to="/" className="logo" aria-label="Pitwall — início">
      <span className="mk" aria-hidden="true" />
      PITWALL
    </Link>
  )
}

const ICONS = {
  home: <path d="M3 11 12 4l9 7v9H3z" />,
  flag: <path d="M5 21V4h11l-2 4 2 4H5" />,
  trophy: <path d="M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0zM17 5h3v2a3 3 0 0 1-3 3M7 5H4v2a3 3 0 0 0 3 3" />,
  user: <><circle cx="12" cy="8" r="4" /><path d="M4 21c1-4 4-6 8-6s7 2 8 6" /></>,
  search: <><circle cx="11" cy="11" r="7" /><path d="m20 20-3.5-3.5" /></>,
  spark: <path d="M12 3v4M12 17v4M3 12h4M17 12h4M6 6l2.5 2.5M15.5 15.5 18 18M6 18l2.5-2.5M15.5 8.5 18 6" />,
}

const isMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform)

/** Ctrl+K / ⌘K de qualquer lugar; "/" quando não se está digitando num campo. */
function useSearchShortcut(onOpen: () => void) {
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      const t = e.target
      const typing = t instanceof HTMLElement && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName))
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); onOpen() }
      else if (e.key === '/' && !typing && !e.ctrlKey && !e.metaKey && !e.altKey) { e.preventDefault(); onOpen() }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onOpen])
}

function Icon({ name }: { name: keyof typeof ICONS }) {
  return (
    <svg aria-hidden="true" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      {ICONS[name]}
    </svg>
  )
}

/** Barra de cima (desktop e celular) + barra de abas embaixo no celular. */
export function TopBar() {
  const { user } = useAuth()
  const { unseen } = useUnseenChangelog()
  const location = useLocation()
  const [searchOpen, setSearchOpen] = useState(false)
  const [openSearch] = useState(() => () => setSearchOpen(true))
  useSearchShortcut(openSearch)

  return (
    <>
      <header className="bar">
        <div className="bar-in">
          <Logo />
          <nav className="nav" aria-label="Principal">
            <NavLink to="/" end>Início</NavLink>
            <NavLink to="/corridas">Corridas</NavLink>
            <NavLink to="/rankings">Rankings</NavLink>
            <NavLink to="/novidades" className="nav-news">Novidades{unseen.length > 0 && <i className="news-dot" aria-label="tem novidade" />}</NavLink>
          </nav>
          {(
            <button type="button" className="search-btn" onClick={openSearch} aria-label="Buscar (Ctrl+K)">
              <Icon name="search" />
              <span className="search-btn-text">Pilotos, pistas, carros, corridas…</span>
              <kbd>{isMac ? '⌘K' : 'Ctrl K'}</kbd>
            </button>
          )}
          <div className="bar-right">
            <Link to="/novidades" className="news-ico" aria-label={unseen.length ? 'Novidades (tem novidade)' : 'Novidades'}>
              <Icon name="spark" />{unseen.length > 0 && <i className="news-dot" />}
            </Link>
            {user ? <AccountMenu /> : <Link to="/login" state={{ from: location.pathname + location.search }} className="btn btn-sm btn-primary">Entrar</Link>}
          </div>
        </div>
      </header>
      <nav className="tabbar" aria-label="Navegação">
        <NavLink to="/" end><Icon name="home" />Início</NavLink>
        <NavLink to="/corridas"><Icon name="flag" />Corridas</NavLink>
        <button type="button" onClick={openSearch}><Icon name="search" />Busca</button>
        <NavLink to="/rankings"><Icon name="trophy" />Rankings</NavLink>
        <NavLink to="/perfil"><Icon name="user" />{user ? 'Perfil' : 'Entrar'}</NavLink>
      </nav>
      {searchOpen && <CommandPalette onClose={() => setSearchOpen(false)} />}
      <FeedbackWidget />
    </>
  )
}
