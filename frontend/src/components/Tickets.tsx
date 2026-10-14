import { useRef } from 'react'
import type { TicketStatus, TicketType, TicketUpdate } from '../api/types'
import { fmtRelative } from '../lib/format'
import { MAX_TICKET_IMAGES, STATUS_META, TYPE_META, type useImageAttachments } from '../lib/tickets'

const svg = { 'aria-hidden': true, width: 15, height: 15, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 2.2, strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const }

const STATUS_ICON: Record<TicketStatus, React.ReactNode> = {
  Open: <svg {...svg}><path d="M7 3h10M7 21h10M8 3c0 5 8 5 8 9s-8 4-8 9M16 3c0 5-8 5-8 9" /></svg>,
  InReview: <svg {...svg}><path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" /></svg>,
  InDevelopment: <svg {...svg}><path d="m8 7-5 5 5 5M16 7l5 5-5 5M14 4l-4 16" /></svg>,
  Completed: <svg {...svg}><circle cx="12" cy="12" r="9" /><path d="m8 12 3 3 5-6" /></svg>,
  Cancelled: <svg {...svg}><circle cx="12" cy="12" r="9" /><path d="m9 9 6 6M15 9l-6 6" /></svg>,
}

export const BugIcon = ({ size = 15 }: { size?: number }) => (
  <svg {...svg} width={size} height={size}><rect x="7" y="7" width="10" height="13" rx="5" /><path d="M9 7V5a3 3 0 0 1 6 0v2M3 13h4M17 13h4M4 7l3 2M20 7l-3 2M4 19l3-2M20 19l-3-2" /></svg>
)
export const IdeaIcon = ({ size = 15 }: { size?: number }) => (
  <svg {...svg} width={size} height={size}><path d="M9 18h6M10 21h4M12 3a6 6 0 0 0-3.5 10.9c.6.5 1 1.2 1 2.1h5c0-.9.4-1.6 1-2.1A6 6 0 0 0 12 3z" /></svg>
)

/** Status com ícone e texto (a cor só reforça). */
export function StatusBadge({ status }: { status: TicketStatus }) {
  const m = STATUS_META[status]
  return <span className={`tk-status ${m.cls}`}>{STATUS_ICON[status]}{m.label}</span>
}

export function TypeBadge({ type }: { type: TicketType }) {
  return <span className="tk-type">{type === 'Bug' ? <BugIcon /> : <IdeaIcon />}{TYPE_META[type].label}</span>
}

/** Botão grande de escolha do tipo (bug ou sugestão). */
export function TypeChoice({ value, onChange }: { value: TicketType; onChange: (t: TicketType) => void }) {
  return (
    <div className="tk-choice" role="radiogroup" aria-label="Tipo">
      {(['Bug', 'Idea'] as TicketType[]).map((t) => (
        <button key={t} type="button" role="radio" aria-checked={value === t} className="tk-choice-opt" onClick={() => onChange(t)}>
          {t === 'Bug' ? <BugIcon size={20} /> : <IdeaIcon size={20} />}
          {t === 'Bug' ? 'Reportar bug' : 'Sugerir melhoria'}
        </button>
      ))}
    </div>
  )
}

/** Miniaturas + botão "Anexar imagem" + dica de colar print. */
export function ImageAttachmentsField({ attachments }: { attachments: ReturnType<typeof useImageAttachments> }) {
  const input = useRef<HTMLInputElement>(null)
  const { images, error, addFiles, remove } = attachments

  return (
    <div className="tk-attach">
      {images.length > 0 && (
        <div className="tk-thumbs">
          {images.map((img) => (
            <div key={img.id} className="tk-thumb">
              <img src={img.dataUrl ?? img.url} alt="" />
              <button type="button" onClick={() => remove(img.id)} aria-label="Remover imagem">
                <svg aria-hidden="true" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round"><path d="M6 6l12 12M18 6 6 18" /></svg>
              </button>
            </div>
          ))}
        </div>
      )}
      <div className="tk-attach-row">
        <button type="button" className="btn btn-sm" onClick={() => input.current?.click()} disabled={images.length >= MAX_TICKET_IMAGES}>
          <svg {...svg}><rect x="3" y="5" width="18" height="14" rx="2" /><circle cx="9" cy="10" r="1.6" /><path d="m21 16-5-5-8 8" /></svg>
          Anexar imagem
        </button>
        <span className="tk-hint">ou cole um print (Ctrl+V) · até {MAX_TICKET_IMAGES}</span>
      </div>
      <input ref={input} type="file" accept="image/png,image/jpeg,image/webp,image/gif" multiple hidden
        onChange={(e) => {
          if (e.target.files) void addFiles(Array.from(e.target.files))
          e.target.value = '' // permite reanexar o mesmo arquivo
        }} />
      {error && <p className="form-error">{error}</p>}
    </div>
  )
}

/** Prints de um chamado já salvo (abrem em tamanho real numa aba nova). */
export function TicketImages({ urls }: { urls: string[] }) {
  if (!urls.length) return null
  return (
    <div className="tk-thumbs">
      {urls.map((u) => (
        <a key={u} href={u} target="_blank" rel="noreferrer" className="tk-thumb tk-thumb-lg"><img src={u} alt="Print anexado" /></a>
      ))}
    </div>
  )
}

/** Histórico de respostas e mudanças de status. */
export function TicketUpdates({ updates, title, empty }: { updates: TicketUpdate[]; title: string; empty?: string }) {
  if (!updates.length) return empty ? <p className="tk-empty">{empty}</p> : null
  return (
    <div className="tk-updates">
      <h3>{title}</h3>
      {updates.map((u) => (
        <div key={u.id} className="tk-update">
          <div className="tk-update-head">
            <b>{u.admin.displayName}</b>
            <span className="rel">{fmtRelative(u.createdAt)}</span>
          </div>
          {u.statusChanged && <div className="tk-update-status"><span>Status:</span><StatusBadge status={u.status} /></div>}
          {u.note && <p>{u.note}</p>}
        </div>
      ))}
    </div>
  )
}
