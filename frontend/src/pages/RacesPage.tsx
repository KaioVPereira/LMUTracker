import { Link, useSearchParams } from 'react-router-dom'
import type { Paged, RaceListItem, SearchTrack } from '../api/types'
import { ClassChips, EmptyState, ErrorBox, RowsSkeleton, TierChip } from '../components/Bits'
import { Pager } from '../components/Pager'
import { TopBar } from '../components/TopBar'
import { fmtDayShort, fmtInt, fmtTime, typeLabel } from '../lib/format'
import { useApi } from '../lib/useApi'

const CLASSES = [
  { key: '', label: 'Todas as classes' },
  { key: 'hyper', label: 'Hyper' },
  { key: 'lmp2', label: 'LMP2' },
  { key: 'lmp3', label: 'LMP3' },
  { key: 'gt3', label: 'GT3' },
]

/** "2026-10-13" (data local do campo) -> início do dia e início do dia seguinte, em ISO (UTC). */
function dayRange(day: string): [string, string] | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(day)
  if (!m) return null
  const start = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]))
  const end = new Date(start)
  end.setDate(start.getDate() + 1)
  return [start.toISOString(), end.toISOString()]
}

const FILTERS = [
  { key: '', label: 'Todas' },
  { key: 'beginner', label: 'Iniciante' },
  { key: 'intermediate', label: 'Intermediário' },
  { key: 'advanced', label: 'Avançado' },
  { key: 'weekly', label: 'Semanais' },
  { key: 'special', label: 'Especiais' },
  { key: 'championship', label: 'Campeonatos' },
]

export function RacesPage() {
  const [params, setParams] = useSearchParams()
  const tier = params.get('nivel') ?? ''
  const track = params.get('pista') ?? ''
  const car = params.get('carro') ?? ''
  const cls = params.get('classe') ?? ''
  const day = params.get('dia') ?? ''
  const page = Math.max(1, Number(params.get('pagina') ?? 1))
  const q = new URLSearchParams({ page: String(page) })
  if (tier) q.set('tier', tier)
  if (track) q.set('track', track)
  if (car) q.set('car', car)
  if (cls) q.set('class', cls)
  const range = day ? dayRange(day) : null
  if (range) { q.set('from', range[0]); q.set('to', range[1]) }
  const { data, loading, error, reload } = useApi<Paged<RaceListItem>>(`/lmu/races?${q}`)
  const tracks = useApi<SearchTrack[]>('/lmu/tracks')

  type F = { nivel?: string; pista?: string; carro?: string; classe?: string; dia?: string; pagina?: number }
  function go(next: F) {
    const p = new URLSearchParams()
    const cur: Record<string, string> = { nivel: tier, pista: track, carro: car, classe: cls, dia: day }
    for (const k of Object.keys(cur) as (keyof typeof cur)[]) {
      const v = (next as Record<string, string | undefined>)[k] ?? cur[k]
      if (v) p.set(k, v)
    }
    if ((next.pagina ?? 1) > 1) p.set('pagina', String(next.pagina))
    setParams(p)
  }
  const anyFilter = !!(tier || track || car || cls || day)

  return (
    <>
      <TopBar />
      <div className="wrap">
        <div className="page-head">
          <span className="eyebrow">Resultados</span>
          <h1>Corridas</h1>
          <p className="note">Corridas oficiais já encerradas que o Pitwall guardou. Toque numa para ver os splits.</p>
        </div>

        <div className="filters">
          <label className="filter">
            <span>Pista</span>
            <select value={track} onChange={(e) => go({ pista: e.target.value, pagina: 1 })}>
              <option value="">Todas</option>
              {(tracks.data ?? []).map((t) => <option key={t.track} value={t.track}>{t.track} ({t.races})</option>)}
              {track && !(tracks.data ?? []).some((t) => t.track === track) && <option value={track}>{track}</option>}
            </select>
          </label>
          <label className="filter">
            <span>Dia</span>
            <input type="date" value={day} onChange={(e) => go({ dia: e.target.value, pagina: 1 })} />
          </label>
          {car && <button type="button" className="chip active-filter" onClick={() => go({ carro: '', pagina: 1 })} aria-label={`Remover filtro de carro ${car}`}>Carro: {car} <span aria-hidden="true">✕</span></button>}
          {anyFilter && <button type="button" className="btn btn-ghost btn-sm" onClick={() => setParams(new URLSearchParams())}>Limpar filtros</button>}
        </div>

        <div className="chips" role="tablist" aria-label="Classe">
          {CLASSES.map((c) => (
            <button key={c.key} type="button" role="tab" aria-selected={cls === c.key}
              className={`chip${c.key ? ` chip-cls cls-${c.key}` : ''}`} onClick={() => go({ classe: c.key, pagina: 1 })}>
              {c.key && <span className="cls-dot" aria-hidden="true" />}{c.label}
            </button>
          ))}
        </div>

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
            {anyFilter
              ? 'Nenhuma corrida com resultado combina com esses filtros. Remova um deles para ver mais.'
              : 'Ainda não há corrida com resultado neste nível. Elas aparecem aqui uns 10 minutos depois da bandeira.'}
          </EmptyState>
        )}
        {data && <Pager page={data.page} pageSize={data.pageSize} total={data.total} onPage={(p) => go({ pagina: p })} />}
      </div>
    </>
  )
}
