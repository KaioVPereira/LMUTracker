import { Link, useLocation } from 'react-router-dom'
import type { ChangelogEntry } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { KIND_LABEL, useUnseenChangelog } from '../lib/changelog'
import { fmtDay } from '../lib/format'
import { Modal } from './Modal'

/** "/perfil" vira o perfil do piloto vinculado (sem login, a própria rota pede para entrar). */
function useEntryHref() {
  const { user } = useAuth()
  return (url: string) => (url === '/perfil' && user?.driver ? `/pilotos/${user.driver.driverId}` : url)
}

export function ChangelogCard({ e, onGo }: { e: ChangelogEntry; onGo?: () => void }) {
  const href = useEntryHref()
  return (
    <article className={`news kind-${e.kind}`}>
      <div className="news-top">
        <span className="news-kind">{KIND_LABEL[e.kind] ?? e.kind}</span>
        <time className="rel" dateTime={e.date}>{fmtDay(e.date)}</time>
      </div>
      <h3>{e.title}</h3>
      <p>{e.body}</p>
      {e.url && <Link to={href(e.url)} className="news-go" onClick={onGo}>Conferir →</Link>}
    </article>
  )
}

const HIDDEN_ON = ['/login', '/vincular', '/novidades']

/** Mural que abre no acesso quando há novidade ainda não vista. Fechar (ou seguir um link) marca tudo como visto. */
export function ChangelogModal() {
  const { unseen, markSeen } = useUnseenChangelog()
  const { pathname } = useLocation()
  if (!unseen.length || HIDDEN_ON.some((p) => pathname.startsWith(p))) return null
  const shown = unseen.slice(0, 6)
  return (
    <Modal title="Novidades" onClose={markSeen} wide>
      <div className="news-modal">
        <p className="note">O que mudou no Pitwall desde a sua última visita.</p>
        <div className="news-list">
          {shown.map((e) => <ChangelogCard key={e.key} e={e} onGo={markSeen} />)}
        </div>
        {unseen.length > shown.length && <Link to="/novidades" className="note" onClick={markSeen}>Ver todas as novidades</Link>}
        <button type="button" className="btn btn-primary" onClick={markSeen}>Entendi</button>
      </div>
    </Modal>
  )
}
