import { useState, type FormEvent } from 'react'
import { api, ApiError } from '../api/client'
import type { Paged, TicketDetail, TicketListItem, TicketType } from '../api/types'
import { EmptyState, ErrorBox, Loading } from '../components/Bits'
import { Modal } from '../components/Modal'
import { Pager } from '../components/Pager'
import { ImageAttachmentsField, StatusBadge, TicketImages, TicketUpdates, TypeBadge, TypeChoice } from '../components/Tickets'
import { TopBar } from '../components/TopBar'
import { fmtDateTime, fmtRelative } from '../lib/format'
import { isClosed, useImageAttachments } from '../lib/tickets'
import { useApi } from '../lib/useApi'

/** O autor corrige ou complementa o chamado: tipo, mensagem e prints. */
function TicketEditForm({ ticket, onSaved, onCancel }: { ticket: TicketDetail; onSaved: (t: TicketDetail) => void; onCancel: () => void }) {
  const [type, setType] = useState<TicketType>(ticket.type)
  const [message, setMessage] = useState(ticket.message)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const attachments = useImageAttachments(ticket.images)

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!message.trim() || saving) return
    setSaving(true)
    setError(null)
    try {
      onSaved(await api<TicketDetail>(`/tickets/${ticket.id}`, {
        method: 'PUT',
        body: { type, message: message.trim(), keepImages: attachments.keptUrls(), newImages: attachments.newDataUrls() },
      }))
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Não foi possível salvar.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <form onSubmit={submit} onPaste={attachments.onPaste} className="tk-form">
      <TypeChoice value={type} onChange={setType} />
      <div className="field">
        <label htmlFor="tk-edit-msg">Mensagem</label>
        <textarea id="tk-edit-msg" value={message} onChange={(e) => setMessage(e.target.value)} rows={6} maxLength={5000} required autoFocus />
      </div>
      <ImageAttachmentsField attachments={attachments} />
      {error && <p className="form-error" role="alert">{error}</p>}
      <div className="tk-actions end">
        <button type="button" className="btn" onClick={onCancel} disabled={saving}>Cancelar</button>
        <button type="submit" className="btn btn-primary" disabled={saving || !message.trim()}>{saving ? 'Salvando…' : 'Salvar alterações'}</button>
      </div>
    </form>
  )
}

function TicketDetailModal({ id, onClose, onChanged }: { id: string; onClose: () => void; onChanged: () => void }) {
  const d = useApi<TicketDetail>(`/tickets/${id}`)
  const [saved, setSaved] = useState<TicketDetail | null>(null)
  const [editing, setEditing] = useState(false)
  const t = saved ?? d.data

  return (
    <Modal title={t ? `Chamado #${t.number}` : 'Chamado'} onClose={onClose} wide>
      {!t ? (d.error ? <ErrorBox message={d.error} /> : <Loading rows={3} />) : (
        <div className="tk-detail">
          <div className="tk-meta">
            <TypeBadge type={t.type} />
            <StatusBadge status={t.status} />
            <span className="rel">{fmtDateTime(t.createdAt)}{t.editedAt && <> · editado {fmtRelative(t.editedAt)}</>}</span>
            {!editing && !isClosed(t.status) && (
              <button type="button" className="btn btn-sm tk-edit" onClick={() => setEditing(true)}>
                <svg aria-hidden="true" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><path d="M4 20h4L19 9l-4-4L4 16zM14 6l4 4" /></svg>
                Editar
              </button>
            )}
          </div>
          {editing ? (
            <TicketEditForm ticket={t} onCancel={() => setEditing(false)} onSaved={(n) => { setSaved(n); setEditing(false); onChanged() }} />
          ) : (
            <>
              <p className="tk-message">{t.message}</p>
              <TicketImages urls={t.images} />
            </>
          )}
          <TicketUpdates updates={t.updates} title="Atualizações" empty="Ainda sem atualizações — a equipe do Pitwall vai responder em breve." />
        </div>
      )}
    </Modal>
  )
}

export function TicketRow({ t, onOpen, showReporter = false }: { t: TicketListItem; onOpen: () => void; showReporter?: boolean }) {
  return (
    <button type="button" className="tk-row" onClick={onOpen}>
      <span className="tk-num mono">#{t.number}</span>
      <span className="tk-row-main">
        <span className="tk-row-top">
          {showReporter && <b>{t.reporter.displayName}</b>}
          <TypeBadge type={t.type} />
          {t.editedAt && <span className="rel">editado</span>}
        </span>
        <span className="tk-row-msg">{t.message}</span>
      </span>
      <span className="tk-row-side">
        <StatusBadge status={t.status} />
        <span className="rel">{fmtRelative(t.updatedAt)}</span>
      </span>
    </button>
  )
}

/** "Meus chamados": bugs e sugestões que o usuário abriu, com o status e as respostas. */
export function MyTicketsPage() {
  const [page, setPage] = useState(1)
  const [openId, setOpenId] = useState<string | null>(null)
  const list = useApi<Paged<TicketListItem>>(`/tickets?page=${page}`)

  return (
    <>
      <TopBar />
      <div className="wrap">
        <header className="page-head">
          <span className="eyebrow">Conta</span>
          <h1>Meus chamados</h1>
          <p className="rel">Bugs e sugestões que você enviou pelo botão de feedback, com o status e as respostas.</p>
        </header>
        {list.loading ? <Loading /> : list.error ? <ErrorBox message={list.error} onRetry={list.reload} /> : !list.data?.items.length ? (
          <EmptyState title="Nenhum chamado ainda">Use o botão Feedback, no canto da tela, para reportar um bug ou sugerir uma melhoria.</EmptyState>
        ) : (
          <>
            <div className="rows">
              {list.data.items.map((t) => <TicketRow key={t.id} t={t} onOpen={() => setOpenId(t.id)} />)}
            </div>
            <Pager page={list.data.page} pageSize={list.data.pageSize} total={list.data.total} onPage={setPage} />
          </>
        )}
      </div>
      {openId && <TicketDetailModal id={openId} onClose={() => setOpenId(null)} onChanged={list.reload} />}
    </>
  )
}
