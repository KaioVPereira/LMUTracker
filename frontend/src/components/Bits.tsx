import { fmtDelta, rankClass, tierLabel } from '../lib/format'

export function TierChip({ tier }: { tier: string | null }) {
  if (!tier) return <span className="flat">—</span>
  return <span className={`tier tier-${tier}`}>{tierLabel(tier)}</span>
}

/** "Silver 2" com a cor do rank. */
export function Rank({ title, tier }: { title: string | null; tier: number | null }) {
  if (!title) return <span className="flat">—</span>
  return <span className={`rk ${rankClass(title)}`}>{title}{tier !== null ? ` ${tier}` : ''}</span>
}

export function Delta({ value }: { value: number | null }) {
  const d = fmtDelta(value)
  return <span className={d.cls}>{d.text}</span>
}

export function ClassChip({ cls }: { cls: string | null }) {
  if (!cls) return <span className="flat">—</span>
  return <span className="cls">{cls.replace(/_/g, ' ')}</span>
}

export function Loading({ what = 'Carregando…' }: { what?: string }) {
  return <div className="card pad note">{what}</div>
}

export function ErrorBox({ message }: { message: string }) {
  return <div className="form-error" role="alert">{message}</div>
}

/** Barra de progresso do rank (0–100). */
export function Progress({ value, title }: { value: number | null; title: string | null }) {
  if (value === null) return null
  return (
    <div className="prog" aria-label={`Progresso ${value}%`}>
      <i className={rankClass(title).replace('rk-', 'bg-')} style={{ width: `${Math.min(100, Math.max(0, value))}%` }} />
    </div>
  )
}
