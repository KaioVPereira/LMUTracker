import { Link, useParams, useSearchParams } from 'react-router-dom'
import type { Leaderboard, Paged, RaceListItem, TrackDetail } from '../api/types'
import { ClassChip, ClassChips, CountUp, EmptyState, ErrorBox, Loading, TierChip } from '../components/Bits'
import { LeaderboardTable } from '../components/Leaderboard'
import { TopBar } from '../components/TopBar'
import { TrackMap } from '../components/TrackMap'
import { classKey } from '../lib/classes'
import { fmtDay, fmtDayShort, fmtLap, fmtRelative, fmtTime } from '../lib/format'
import { useApi } from '../lib/useApi'
import { CLASS_TABS, SETUP_TABS } from '../lib/filters'

/** Página da pista: traçado, números, recordes por classe, ranking, carros mais rápidos e corridas recentes. */
export function TrackPage() {
  const { track = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const t = useApi<TrackDetail>(`/lmu/tracks/${encodeURIComponent(track)}`)
  const present = (t.data?.classes ?? []).map((c) => classKey(c.carClass))
  const cls = params.get('classe') ?? CLASS_TABS.find((c) => present.includes(c.key as never))?.key ?? 'gt3'
  const setup = params.get('setup') ?? ''
  const q = new URLSearchParams({ track, class: cls, take: '20' })
  if (setup) q.set('setup', setup)
  const lb = useApi<Leaderboard>(t.data ? `/lmu/leaderboard?${q}` : null)
  const races = useApi<Paged<RaceListItem>>(`/lmu/races?track=${encodeURIComponent(track)}`)

  const set = (next: Record<string, string>) => {
    const p = new URLSearchParams(params)
    for (const [k, v] of Object.entries(next)) { if (v) p.set(k, v); else p.delete(k) }
    setParams(p, { replace: true })
  }

  if (t.loading) return <><TopBar /><div className="wrap"><Loading /></div></>
  if (!t.data) return <><TopBar /><div className="wrap"><ErrorBox message={t.notFound ? 'Nenhuma corrida registrada nesta pista ainda.' : (t.error ?? 'Pista não encontrada.')} /></div></>
  const d = t.data
  const carsOfClass = d.cars.filter((c) => classKey(c.carClass) === cls)

  return (
    <>
      <TopBar />
      <div className="wrap">
        <nav className="crumbs" aria-label="Caminho"><Link to="/rankings">Rankings</Link><span>/</span><span>Pistas</span><span>/</span><span>{d.track}</span></nav>

        <header className="race-head race-head-map">
          <div className="race-head-main">
            <span className="eyebrow">Pista</span>
            <h1>{d.track}</h1>
            <div className="meta-line">
              <ClassChips classes={d.classes.map((c) => c.carClass).join(',')} solid />
              {d.firstRace && <span className="rel">registrando desde {fmtDay(d.firstRace)}</span>}
            </div>
            <div className="race-head-nums">
              <div><span className="n"><CountUp value={d.races} /></span><span className="l">Corridas</span></div>
              <div><span className="n"><CountUp value={d.starts} /></span><span className="l">Largadas</span></div>
              <div><span className="n"><CountUp value={d.drivers} /></span><span className="l">Pilotos</span></div>
            </div>
          </div>
          <TrackMap track={d.track} className="race-map" />
        </header>

        <section aria-labelledby="sec-rec">
          <div className="sec-h"><h2 id="sec-rec">Recordes</h2><span className="rel">melhor volta por classe e tipo de setup</span></div>
          <div className="grid-cards">
            {d.records.map((r) => (
              <Link key={`${r.carClass}-${r.fixedSetup}`} to={`/corridas/${r.eventId}`} className={`car-card lift rec-card cls-${classKey(r.carClass)}`}>
                <span className="rec-top"><ClassChip cls={r.carClass} /><span className={`setup ${r.fixedSetup ? 'is-fixed' : ''}`}>{r.fixedSetup ? 'Setup fixo' : 'Setup livre'}</span></span>
                <span className="rec-time">{fmtLap(r.bestLap)}</span>
                <span className="rec-meta"><span>{r.car ?? '—'}</span></span>
                <span className="rec-driver"><b>{r.driverName}</b><span className="rel" style={{ marginLeft: 'auto' }}>{fmtRelative(r.setAt)}</span></span>
              </Link>
            ))}
          </div>
        </section>

        <section aria-labelledby="sec-lb">
          <div className="sec-h"><h2 id="sec-lb">Ranking</h2><Link to={`/rankings?pista=${encodeURIComponent(d.track)}&classe=${cls}`} className="sec-link">Ranking completo →</Link></div>
          <div className="chips" role="tablist" aria-label="Classe">
            {CLASS_TABS.filter((c) => present.includes(c.key as never)).map((c) => (
              <button key={c.key} type="button" role="tab" aria-selected={cls === c.key} className={`chip chip-cls cls-${c.key}`} onClick={() => set({ classe: c.key })}>
                <span className="cls-dot" aria-hidden="true" />{c.label}
              </button>
            ))}
            {SETUP_TABS.map((s) => (
              <button key={s.key} type="button" role="tab" aria-selected={setup === s.key} className="chip" onClick={() => set({ setup: s.key })}>{s.label}</button>
            ))}
          </div>
          <LeaderboardTable data={lb.data} loading={lb.loading} />
        </section>

        <section aria-labelledby="sec-cars">
          <div className="sec-h"><h2 id="sec-cars">Carros mais rápidos</h2><span className="rel">média das 10 melhores voltas (uma por piloto)</span></div>
          {carsOfClass.length ? (
            <div className="rows">
              {carsOfClass.map((c, i) => (
                <Link key={c.car} to={`/carros/${encodeURIComponent(c.car)}`} className="stat-row plain">
                  <span className="hl-rank">{i + 1}</span>
                  <span className="stat-main"><b>{c.car} <ClassChip cls={c.carClass} /></b><span>{c.starts.toLocaleString('pt-BR')} largadas</span></span>
                  <span className="stat-nums">
                    <span><small>média top 10</small><b className="mono">{fmtLap(c.top10Avg)}</b></span>
                    <span><small>melhor</small><b className="mono">{fmtLap(c.bestLap)}</b></span>
                  </span>
                </Link>
              ))}
            </div>
          ) : <EmptyState title="Sem carros nesta classe aqui" />}
        </section>

        <section aria-labelledby="sec-hist">
          <div className="sec-h"><h2 id="sec-hist">Corridas recentes</h2><Link to={`/corridas?pista=${encodeURIComponent(d.track)}`} className="sec-link">Todas →</Link></div>
          {races.data?.items.length ? (
            <div className="rows">
              {races.data.items.slice(0, 8).map((r) => (
                <Link key={r.eventId} to={`/corridas/${r.eventId}`} className="rowi">
                  <span className="rowi-time">{fmtTime(r.startsAt)}<small className="date">{fmtDayShort(r.startsAt)}</small></span>
                  <span className="rowi-main"><b>{r.title ?? 'Corrida'}</b><span>{r.drivers} pilotos · {r.splits} splits</span></span>
                  <span className="rowi-side"><ClassChips classes={r.classes} /><TierChip tier={r.tier} /></span>
                </Link>
              ))}
            </div>
          ) : <EmptyState title="Sem corridas ainda" />}
        </section>
      </div>
    </>
  )
}
