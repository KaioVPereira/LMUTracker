import { useSearchParams } from 'react-router-dom'
import type { CarListItem, Leaderboard, SearchTrack } from '../api/types'
import { LeaderboardTable } from '../components/Leaderboard'
import { TopBar } from '../components/TopBar'
import { classShort } from '../lib/classes'
import { CLASS_TABS, SETUP_TABS } from '../lib/filters'
import { useApi } from '../lib/useApi'

/** Rankings: melhor volta por piloto, recortado por pista, classe, carro e tipo de setup (tudo na URL). */
export function RankingsPage() {
  const [params, setParams] = useSearchParams()
  const tracks = useApi<SearchTrack[]>('/lmu/tracks')
  const cars = useApi<CarListItem[]>('/lmu/cars')
  const track = params.get('pista') ?? tracks.data?.[0]?.track ?? ''
  const cls = params.get('classe') ?? 'gt3'
  const setup = params.get('setup') ?? ''
  const car = params.get('carro') ?? ''

  const q = new URLSearchParams({ take: '50' })
  if (track) q.set('track', track)
  if (cls) q.set('class', cls)
  if (setup) q.set('setup', setup)
  if (car) q.set('car', car)
  const lb = useApi<Leaderboard>(track ? `/lmu/leaderboard?${q}` : null)

  const set = (next: Record<string, string>) => {
    const p = new URLSearchParams(params)
    for (const [k, v] of Object.entries(next)) { if (v) p.set(k, v); else p.delete(k) }
    if ('classe' in next) p.delete('carro') // carro de outra classe não faz sentido
    setParams(p, { replace: true })
  }
  const carsOfClass = (cars.data ?? []).filter((c) => !cls || classShort(c.carClass).toLowerCase() === cls)

  return (
    <>
      <TopBar />
      <div className="wrap">
        <div className="page-head">
          <span className="eyebrow">Rankings</span>
          <h1>Voltas mais rápidas</h1>
          <p className="note">A melhor volta de cada piloto em corridas oficiais, por pista e classe. Setup fixo e livre são rankings separados.</p>
        </div>

        <div className="filters">
          <label className="filter">
            <span>Pista</span>
            <select value={track} onChange={(e) => set({ pista: e.target.value })}>
              {(tracks.data ?? []).map((t) => <option key={t.track} value={t.track}>{t.track}</option>)}
            </select>
          </label>
          <label className="filter">
            <span>Carro</span>
            <select value={car} onChange={(e) => set({ carro: e.target.value })}>
              <option value="">Todos da classe</option>
              {carsOfClass.map((c) => <option key={c.car} value={c.car}>{c.car}</option>)}
            </select>
          </label>
        </div>
        <div className="chips" role="tablist" aria-label="Classe">
          {CLASS_TABS.map((c) => (
            <button key={c.key} type="button" role="tab" aria-selected={cls === c.key} className={`chip chip-cls cls-${c.key}`} onClick={() => set({ classe: c.key })}>
              <span className="cls-dot" aria-hidden="true" />{c.label}
            </button>
          ))}
        </div>
        <div className="chips" role="tablist" aria-label="Setup">
          {SETUP_TABS.map((s) => (
            <button key={s.key} type="button" role="tab" aria-selected={setup === s.key} className="chip" onClick={() => set({ setup: s.key })}>{s.label}</button>
          ))}
        </div>

        <LeaderboardTable data={lb.data} loading={lb.loading || tracks.loading} />
      </div>
    </>
  )
}
