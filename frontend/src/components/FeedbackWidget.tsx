import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import type { TicketType } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { useImageAttachments } from '../lib/tickets'
import { Modal } from './Modal'
import { ImageAttachmentsField, TypeChoice } from './Tickets'

/**
 * Botão de feedback fixo no canto inferior direito (igual ao GameTracker): sempre à mão, abre uma janela para
 * reportar bug ou sugerir melhoria, com prints por anexo ou colados. Só para quem está logado.
 */
export function FeedbackWidget() {
  const { user } = useAuth()
  const [open, setOpen] = useState(false)
  const [type, setType] = useState<TicketType>('Bug')
  const [message, setMessage] = useState('')
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [number, setNumber] = useState<number | null>(null)
  const attachments = useImageAttachments()

  if (!user) return null

  function close() {
    setOpen(false)
    setMessage('')
    setType('Bug')
    setError(null)
    setNumber(null)
    attachments.reset()
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!message.trim() || sending) return
    setSending(true)
    setError(null)
    try {
      const r = await api<{ number: number }>('/tickets', { method: 'POST', body: { type, message: message.trim(), images: attachments.newDataUrls() } })
      setNumber(r.number)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Não foi possível enviar.')
    } finally {
      setSending(false)
    }
  }

  return (
    <>
      <button type="button" className="fb-fab" onClick={() => setOpen(true)} aria-label="Reportar bug ou sugerir melhoria" title="Reportar bug ou sugerir melhoria">
        <svg aria-hidden="true" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><path d="M21 12a8 8 0 0 1-11.6 7.1L4 20l1-4.6A8 8 0 1 1 21 12zM12 8v6M9 11h6" /></svg>
        <span className="fb-fab-text">Feedback</span>
      </button>

      {open && (
        <Modal title="Feedback" onClose={close}>
          {number !== null ? (
            <div className="fb-done">
              <svg aria-hidden="true" width="44" height="44" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="12" cy="12" r="9" /><path d="m8 12 3 3 5-6" /></svg>
              <b>Obrigado!</b>
              <span className="mono">Chamado #{number}</span>
              <p>Acompanhe o status e as respostas em <Link to="/chamados" onClick={close}>Meus chamados</Link>.</p>
              <button type="button" className="btn btn-primary" onClick={close}>Fechar</button>
            </div>
          ) : (
            <form onSubmit={submit} onPaste={attachments.onPaste} className="tk-form">
              <p className="tk-intro">Achou um problema ou tem uma ideia para o Pitwall? Conte pra gente.</p>
              <TypeChoice value={type} onChange={setType} />
              <div className="field">
                <label htmlFor="fb-msg">{type === 'Bug' ? 'O que aconteceu?' : 'Qual é a sua ideia?'}</label>
                <textarea id="fb-msg" value={message} onChange={(e) => setMessage(e.target.value)} rows={5} maxLength={5000} required autoFocus
                  placeholder={type === 'Bug' ? 'Em qual página, o que você fez e o que esperava que acontecesse…' : 'O que você gostaria de ver no site…'} />
              </div>
              <ImageAttachmentsField attachments={attachments} />
              {error && <p className="form-error" role="alert">{error}</p>}
              <div className="tk-actions">
                <span className="tk-hint">As respostas aparecem em Meus chamados.</span>
                <button type="submit" className="btn btn-primary" disabled={sending || !message.trim()}>{sending ? 'Enviando…' : 'Enviar'}</button>
              </div>
            </form>
          )}
        </Modal>
      )}
    </>
  )
}
