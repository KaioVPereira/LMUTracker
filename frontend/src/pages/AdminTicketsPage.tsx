import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import type { Paged, TicketDetail, TicketListItem, TicketStatus } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { EmptyState, ErrorBox, Loading } from '../components/Bits'
import { Modal } from '../components/Modal'
import { Pager } from '../components/Pager'
import { StatusBadge, TicketImages, TicketUpdates, TypeBadge } from '../components/Tickets'
import { TopBar } from '../components/TopBar'
import { fmtDateTime, fmtRelative } from '../lib/format'
import { STATUS_META, STATUS_ORDER } from '../lib/tickets'
import { useApi } from '../lib/useApi'
import { TicketRow } from './MyTicketsPage'

const TYPE_FILTERS = [
  { key: '', label: 'Todos os tipos' },
  { key: 'Bug', label: 'Bugs' },
  { key: 'Idea', label: 'Sugestões' },
]

function AdminTicketModal({ id, onClose, onChanged }: { id: string; onClose: () => void; onChanged: () => void }) {
  const d = useApi<TicketDetail>(`/admin/tickets/${id}`)
  const [saved, setSaved] = useState<TicketDetail | null>(null)
  const [nextStatus, setNextStatus] = useState<TicketStatus | null>(null)
  const [note, setNote] = useState('')
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const t = saved ?? d.data

  if (!t) {
    return <Modal title="Chamado" onClose={onClose} wide>{d.error ? <ErrorBox message={d.error} /> : <Loading rows={3} />}</Modal>
  }

  const status = nextStatus ?? t.status
  const changedStatus = status !== t.status
  const canSubmit = changedStatus || note.trim().length > 0

  async function submit() {
    if (!canSubmit || sending) return
    setSending(true)
    setError(null)
    try {
      const r = await api<TicketDetail>(`/admin/tickets/${t!.id}/updates`, {
        method: 'POST',
        body: { status: changedStatus ? status : undefined, note: note.trim() || undefined },
      })
      setSaved(r)
      setNote('')
      setNextStatus(null)
      onChanged()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Não foi possível salvar.')
    } finally {
      setSending(false)
    }
  }

  return (
    <Modal title={`Chamado #${t.number}`} onClose={onClose} wide>
      <div className="tk-detail">
        <div className="tk-reporter">
          <span className="ava" aria-hidden="true">{t.reporter.displayName.slice(0, 2).toUpperCase()}</span>
          <span><b>{t.reporter.displayName}</b> <span className="rel">@{t.reporter.username}</span></span>
        </div>
        <div className="tk-meta">
          <TypeBadge type={t.type} />
          <StatusBadge status={t.status} />
          <span className="rel">{fmtDateTime(t.createdAt)}{t.editedAt && <> · editado pelo autor {fmtRelative(t.editedAt)}</>}</span>
        </div>
        <p className="tk-message">{t.message}</p>
        <TicketImages urls={t.images} />
        <TicketUpdates updates={t.updates} title="Histórico" />

        <div className="tk-reply">
          <div className="field">
            <label htmlFor="tk-status">Status</label>
            <select id="tk-status" value={status} onChange={(e) => setNextStatus(e.target.value as TicketStatus)}>
              {STATUS_ORDER.map((s) => <option key={s} value={s}>{STATUS_META[s].label}</option>)}
            </select>
          </div>
          <div className="field">
            <label htmlFor="tk-note">Resposta para {t.reporter.displayName} (opcional)</label>
            <textarea id="tk-note" value={note} onChange={(e) => setNote(e.target.value)} rows={3} maxLength={2000}
              placeholder="Aparece para o usuário em Meus chamados…" />
          </div>
          {error && <p className="form-error" role="alert">{error}</p>}
          <div className="tk-actions end">
            <button type="button" className="btn btn-primary" disabled={!canSubmit || sending} onClick={submit}>{sending ? 'Salvando…' : 'Salvar'}</button>
          </div>
        </div>
      </div>
    </Modal>
  )
}

/** Painel do super admin: todos os chamados, com filtro por status e tipo. */
export function AdminTicketsPage() {
  const { user } = useAuth()
  const [params, setParams] = useSearchParams()
  const status = params.get('status') ?? ''
  const type = params.get('tipo') ?? ''
  const page = Number(params.get('pagina') ?? 1) || 1
  const [openId, setOpenId] = useState<string | null>(null)
  const q = new URLSearchParams({ page: String(page) })
  if (status) q.set('status', status)
  if (type) q.set('type', type)
  const list = useApi<Paged<TicketListItem>>(user?.isSuperAdmin ? `/admin/tickets?${q}` : null)

  const set = (next: Record<string, string>) => {
    const p = new URLSearchParams(params)
    for (const [k, v] of Object.entries(next)) { if (v) p.set(k, v); else p.delete(k) }
    if (!('pagina' in next)) p.delete('pagina')
    setParams(p, { replace: true })
  }

  return (
    <>
      <TopBar />
      <div className="wrap">
        <header className="page-head">
          <span className="eyebrow">Super admin</span>
          <h1>Chamados</h1>
          <p className="rel">Bugs e sugestões enviados pelos usuários pelo botão de feedback.</p>
        </header>
        {!user?.isSuperAdmin ? (
          <EmptyState title="Acesso restrito">Este painel é só do administrador do Pitwall.</EmptyState>
        ) : (
          <>
            <div className="tk-filters">
            <div className="chips" role="tablist" aria-label="Filtrar por status">
              {[{ key: '', label: 'Todos' }, ...STATUS_ORDER.map((s) => ({ key: s, label: STATUS_META[s].label }))].map((f) => (
                <button key={f.key || 'all'} type="button" role="tab" aria-selected={status === f.key} className="chip" onClick={() => set({ status: f.key })}>{f.label}</button>
              ))}
            </div>
            <div className="chips" role="tablist" aria-label="Filtrar por tipo">
              {TYPE_FILTERS.map((f) => (
                <button key={f.key || 'all'} type="button" role="tab" aria-selected={type === f.key} className="chip" onClick={() => set({ tipo: f.key })}>{f.label}</button>
              ))}
            </div>
            </div>
            {list.loading ? <Loading /> : list.error ? <ErrorBox message={list.error} onRetry={list.reload} /> : !list.data?.items.length ? (
              <EmptyState title="Nenhum chamado por aqui" />
            ) : (
              <>
                <div className="tk-list">
                <p className="rel">{list.data.total.toLocaleString('pt-BR')} chamado{list.data.total === 1 ? '' : 's'}</p>
                <div className="rows">
                  {list.data.items.map((t) => <TicketRow key={t.id} t={t} showReporter onOpen={() => setOpenId(t.id)} />)}
                </div>
                </div>
                <Pager page={list.data.page} pageSize={list.data.pageSize} total={list.data.total} onPage={(p) => set({ pagina: String(p) })} />
              </>
            )}
          </>
        )}
      </div>
      {openId && <AdminTicketModal id={openId} onClose={() => setOpenId(null)} onChanged={list.reload} />}
    </>
  )
}
