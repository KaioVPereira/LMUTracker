import { useEffect, useId, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { useAppUpdate } from '../lib/appUpdate'
import { initials, rankClass } from '../lib/format'
import { isNative } from '../lib/native'
import { startSteamLink } from '../lib/steam'

/** "Silver 2" -> "S2" (cabe na barra do celular; o título completo vai no title e no menu). */
const shortRank = (title: string, tier: number | null) => `${title[0].toUpperCase()}${tier ?? ''}`

/** Avatar no canto da barra: abre um menu com o perfil e sair (e vincular a Steam, enquanto não vinculou). */
export function AccountMenu() {
  const { user, logout } = useAuth()
  const [open, setOpen] = useState(false)
  const root = useRef<HTMLDivElement>(null)
  const button = useRef<HTMLButtonElement>(null)
  const menuId = useId()

  // Fecha ao clicar fora ou com Esc (e devolve o foco ao avatar).
  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => { if (!root.current?.contains(e.target as Node)) setOpen(false) }
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') { setOpen(false); button.current?.focus() } }
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => { document.removeEventListener('mousedown', onDown); document.removeEventListener('keydown', onKey) }
  }, [open])

  const { updateAvailable } = useAppUpdate()
  if (!user) return null
  const close = () => setOpen(false)
  const d = user.driver

  return (
    <div className="acct" ref={root}>
      <button ref={button} type="button" className="user-chip acct-btn" aria-haspopup="menu" aria-expanded={open}
        aria-controls={menuId} onClick={() => setOpen((o) => !o)}>
        <span className="ava" aria-hidden="true">{initials(user.displayName)}{updateAvailable && <i className="ava-dot" />}</span>
        <span className="name">{user.displayName}</span>
        {d?.driverRankTitle && (
          <span className="acct-ranks">
            <span className={`acct-rank ${rankClass(d.driverRankTitle)}`} title={`Driver Rank: ${d.driverRankTitle} ${d.driverRankTier ?? ''}`}>
              <small>DR</small>{shortRank(d.driverRankTitle, d.driverRankTier)}
            </span>
            {d.safetyRankTitle && (
              <span className={`acct-rank acct-rank-sr ${rankClass(d.safetyRankTitle)}`} title={`Safety Rank: ${d.safetyRankTitle} ${d.safetyRankTier ?? ''}`}>
                <small>SR</small>{shortRank(d.safetyRankTitle, d.safetyRankTier)}
              </span>
            )}
          </span>
        )}
        <svg aria-hidden="true" className="acct-caret" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round"><path d="m6 9 6 6 6-6" /></svg>
        <span className="sr-only">Menu da conta</span>
      </button>
      {open && (
        <div className="acct-menu" id={menuId} role="menu" aria-label="Conta">
          <div className="acct-head">
            <b>{user.displayName}</b>
            <span>{user.steamId ? 'Steam vinculada' : 'Steam não vinculada'}</span>
            {d?.driverRankTitle && (
              <span className="acct-head-ranks">
                Driver Rank <b className={rankClass(d.driverRankTitle)}>{d.driverRankTitle} {d.driverRankTier}</b>
                {d.safetyRankTitle && <> · Safety Rank <b className={rankClass(d.safetyRankTitle)}>{d.safetyRankTitle} {d.safetyRankTier}</b></>}
              </span>
            )}
          </div>
          <Link to="/perfil" role="menuitem" className="acct-item" onClick={close}>
            <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="8" r="4" /><path d="M4 21c1-4 4-6 8-6s7 2 8 6" /></svg>
            Meu perfil
          </Link>
          <Link to="/chamados" role="menuitem" className="acct-item" onClick={close}>
            <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M22 12h-6l-2 3h-4l-2-3H2" /><path d="M5.5 5h13L22 12v6a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2v-6z" /></svg>
            Meus chamados
          </Link>
          <Link to="/app" role="menuitem" className="acct-item" onClick={close}>
            <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="6" y="2.5" width="12" height="19" rx="2.5" /><path d="M11 18h2" /></svg>
            {isNative ? 'Versão do app' : 'App Android'}
            {updateAvailable && <span className="pill me acct-new">nova versão</span>}
          </Link>
          {user.isSuperAdmin && (
            <Link to="/admin/chamados" role="menuitem" className="acct-item" onClick={close}>
              <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M12 3 4 6v6c0 5 3.5 8 8 9 4.5-1 8-4 8-9V6z" /><path d="m9 12 2 2 4-4" /></svg>
              Painel de chamados
            </Link>
          )}
          {!user.steamId && (
            <button type="button" role="menuitem" className="acct-item" onClick={() => { close(); void startSteamLink() }}>
              <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1" /></svg>
              Vincular Steam
            </button>
          )}
          <button type="button" role="menuitem" className="acct-item is-danger" onClick={() => { close(); logout() }}>
            <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M15 4h4v16h-4M10 8l-4 4 4 4M6 12h11" /></svg>
            Sair
          </button>
        </div>
      )}
    </div>
  )
}
