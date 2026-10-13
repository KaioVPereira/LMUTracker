import { Link, useSearchParams } from 'react-router-dom'
import type { Paged, RaceListItem } from '../api/types'
import { ClassChips, EmptyState, ErrorBox, RowsSkeleton, TierChip } from '../components/Bits'
import { Pager } from '../components/Pager'
import { TopBar } from '../components/TopBar'
import { fmtDayShort, fmtInt, fmtTime, typeLabel } from '../lib/format'
import { useApi } from '../lib/useApi'

const FILTERS = [
  { key: '', label: 'Todas' },
  { key: 'beginner', label: 'Iniciante' },
  { key: 'intermediate', label: 'Intermediário' },
  { key: 'advanced', label: 'Avançado' },
  { key: 'weekly', label: 'Semanais' },
]

export function RacesPage() {
  const [params, setParams] = useSearchParams()
  const tier = params.get('nivel') ?? ''
  const track = params.get('pista') ?? ''
  const car = params.get('carro') ?? ''
  const page = Math.max(1, Number(params.get('pagina') ?? 1))
  const q = new URLSearchParams({ page: String(page) })
  if (tier) q.set('tier', tier)
  if (track) q.set('track', track)
  if (car) q.set('car', car)
  const { data, loading, error, reload } = useApi<Paged<RaceListItem>>(`/lmu/races?${q}`)

  function go(next: { nivel?: string; pista?: string; carro?: string; pagina?: number }) {
    const p = new URLSearchParams()
    const n = next.nivel ?? tier
    const t = next.pista ?? track
    const c = next.carro ?? car
    if (n) p.set('nivel', n)
    if (t) p.set('pista', t)
    if (c) p.set('carro', c)
    if ((next.pagina ?? 1) > 1) p.set('pagina', String(next.pagina))
    setParams(p)
  }

  return (
    <>
      <TopBar />
      <div className="wrap">
        <div className="page-head">
          <span className="eyebrow">Resultados</span>
          <h1>Corridas</h1>
          <p className="note">Corridas oficiais já encerradas que o Pitwall guardou. Toque numa para ver os splits.</p>
        </div>

        {(track || car) && (
          <div className="chips" aria-label="Filtros ativos">
            {track && <button type="button" className="chip active-filter" onClick={() => go({ pista: '', pagina: 1 })} aria-label={`Remover filtro de pista ${track}`}>Pista: {track} <span aria-hidden="true">✕</span></button>}
            {car && <button type="button" className="chip active-filter" onClick={() => go({ carro: '', pagina: 1 })} aria-label={`Remover filtro de carro ${car}`}>Carro: {car} <span aria-hidden="true">✕</span></button>}
          </div>
        )}

        <div className="chips" role="tablist" aria-label="Nível">
          {FILTERS.map((f) => (
            <button key={f.key} type="button" role="tab" aria-selected={tier === f.key}
              className="chip" onClick={() => go({ nivel: f.key, pagina: 1 })}>
              {f.label}
            </button>
          ))}
        </div>

        {error && <ErrorBox message={error} onRetry={reload} />}
        {loading ? <RowsSkeleton rows={8} /> : data?.items.length ? (
          <div className="rows">
            {data.items.map((r) => (
              <Link key={r.eventId} to={`/corridas/${r.eventId}`} className="rowi">
                <span className="rowi-time">{fmtTime(r.startsAt)}<small className="date">{fmtDayShort(r.startsAt)}</small></span>
                <span className="rowi-main">
                  <b>{r.title ?? 'Corrida'}</b>
                  <span>{r.track ?? '—'} · {typeLabel(r.eventType)}</span>
                </span>
                <span className="rowi-side"><ClassChips classes={r.classes} /><TierChip tier={r.tier} /></span>
                <span className="rowi-num">{r.splits} splits · {fmtInt(r.drivers)} pilotos</span>
              </Link>
            ))}
          </div>
        ) : !error && (
          <EmptyState title="Nenhuma corrida neste filtro">
            {track || car
              ? 'Nenhuma corrida com resultado combina com esses filtros. Remova um deles para ver mais.'
              : 'Ainda não há corrida com resultado neste nível. Elas aparecem aqui uns 10 minutos depois da bandeira.'}
          </EmptyState>
        )}
        {data && <Pager page={data.page} pageSize={data.pageSize} total={data.total} onPage={(p) => go({ pagina: p })} />}
      </div>
    </>
  )
}
