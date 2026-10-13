import type { ReactNode } from 'react'
import { classKey, classShort, splitClasses } from '../lib/classes'
import { fmtDelta, fmtInt, rankClass, tierLabel } from '../lib/format'
import { useCountUp } from '../lib/useCountUp'

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

/** Classe do carro com a cor dela. `solid` = pílula cheia (heros e destaques). */
export function ClassChip({ cls, solid = false }: { cls: string | null; solid?: boolean }) {
  if (!cls) return <span className="flat">—</span>
  return <span className={`cls cls-${classKey(cls)}${solid ? ' solid' : ''}`}>{classShort(cls)}</span>
}

/** Todas as classes de uma corrida ("Hyper,GT3"). */
export function ClassChips({ classes, solid = false }: { classes: string | null; solid?: boolean }) {
  const list = splitClasses(classes)
  if (!list.length) return null
  return <>{list.map((c) => <ClassChip key={c} cls={c} solid={solid} />)}</>
}

/** Faixa de cor da classe na primeira coluna de uma tabela. */
export function ClassBarCell({ cls }: { cls: string | null }) {
  return <td className={`clsbar cls-${classKey(cls)}`} aria-hidden="true"><i /></td>
}

/** Número que conta até o valor na primeira exibição. */
export function CountUp({ value }: { value: number | null | undefined }) {
  const shown = useCountUp(value)
  return <>{shown === null ? '—' : fmtInt(shown)}</>
}

export function Skeleton({ w = '100%', h = 14, r }: { w?: string | number; h?: number; r?: number }) {
  return <span className="sk" style={{ width: w, height: h, borderRadius: r }} aria-hidden="true" />
}

/** Placeholder do formato do conteúdo enquanto carrega — nunca tela em branco. */
export function Loading({ what = 'Carregando…', rows = 4 }: { what?: string; rows?: number }) {
  return (
    <div className="card pad sk-stack" role="status" aria-label={what}>
      <Skeleton w="40%" h={22} />
      <Skeleton w="65%" h={40} r={10} />
      {Array.from({ length: rows }, (_, i) => <Skeleton key={i} w={`${92 - i * 9}%`} />)}
    </div>
  )
}

/** Linhas de lista em carregamento (mesma altura das linhas reais). */
export function RowsSkeleton({ rows = 5 }: { rows?: number }) {
  return (
    <div className="rows" role="status" aria-label="Carregando">
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="rowi">
          <Skeleton w={44} h={14} />
          <span className="sk-stack" style={{ gap: 6 }}><Skeleton w="60%" h={14} /><Skeleton w="35%" h={11} /></span>
          <Skeleton w={48} h={18} r={999} />
        </div>
      ))}
    </div>
  )
}

const FLAG = (
  <svg aria-hidden="true" width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
    <path d="M5 21V4h11l-2 4 2 4H5" />
  </svg>
)

export function EmptyState({ title, children, action }: { title: string; children?: ReactNode; action?: ReactNode }) {
  return (
    <div className="empty-state">
      {FLAG}
      <h3>{title}</h3>
      {children && <p>{children}</p>}
      {action}
    </div>
  )
}

export function ErrorBox({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="form-error" role="alert" style={{ display: 'flex', gap: 12, alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap' }}>
      <span>{message}</span>
      {onRetry && <button type="button" className="btn btn-sm" onClick={onRetry}>Tentar de novo</button>}
    </div>
  )
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

/** Traçado decorativo de fundo (heros). Não representa a pista da corrida — os traçados reais vêm depois. */
export function TrackArt({ className }: { className?: string }) {
  const d = 'M48 128 C40 70 92 40 146 50 L262 70 C304 78 324 112 302 142 L272 182 C256 204 220 204 200 188 L152 152 C132 138 112 150 102 172 C90 198 54 194 46 162 Z'
  return (
    <svg className={className} viewBox="0 0 340 230" aria-hidden="true">
      <path d={d} fill="none" stroke="rgba(255,255,255,0.08)" strokeWidth="16" strokeLinejoin="round" />
      <path d={d} fill="none" stroke="rgba(242,245,255,0.45)" strokeWidth="3" strokeLinejoin="round" />
    </svg>
  )
}
