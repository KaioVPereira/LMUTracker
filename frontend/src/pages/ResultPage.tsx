import { Link, useParams } from 'react-router-dom'
import type { Lap, ResultDetail } from '../api/types'
import { ClassChip, Delta, ErrorBox, Loading, Rank, TierChip } from '../components/Bits'
import { LapChart } from '../components/LapChart'
import { TopBar } from '../components/TopBar'
import {
  compoundClass, finishLabel, fmtDateTime, fmtGap, fmtLap, fmtPct, fmtRaceTime, typeLabel,
} from '../lib/format'
import { useApi } from '../lib/useApi'

export function ResultPage() {
  const { resultId = '' } = useParams()
  const { data, loading, error } = useApi<ResultDetail>(`/lmu/results/${encodeURIComponent(resultId)}`)

  if (loading) return <><TopBar /><div className="wrap"><Loading /></div></>
  if (!data) return <><TopBar /><div className="wrap"><ErrorBox message={error ?? 'Resultado não encontrado.'} /></div></>

  const r = data.row
  const laps = data.laps
  const bestSector = (k: 's1' | 's2' | 's3') => Math.min(...laps.map((l) => l[k] ?? Infinity))
  const best = { s1: bestSector('s1'), s2: bestSector('s2'), s3: bestSector('s3') }
  const bestLap = Math.min(...laps.map((l) => (l.pit ? Infinity : (l.lapTime ?? Infinity))))
  const finished = r.finishStatus === 'Finished Normally'

  return (
    <>
      <TopBar />
      <div className="wrap">
        <nav className="crumbs" aria-label="Caminho">
          <Link to="/corridas">Corridas</Link><span>/</span>
          <Link to={`/corridas/${data.eventId}?split=${data.splitNo}`}>{data.title ?? 'Corrida'} · Split {data.splitNo}</Link><span>/</span>
          <span>{r.name}</span>
        </nav>

        <header className="race-head card">
          <div className="race-head-main">
            <span className="eyebrow">{typeLabel(data.eventType)} · Split {data.splitNo}</span>
            <h1>
              {r.driverId ? <Link to={`/pilotos/${r.driverId}`} className="plain">{r.name}</Link> : r.name}
            </h1>
            <div className="meta-line">
              <span>{data.title ?? 'Corrida'}</span><span>{data.track ?? '—'}</span><TierChip tier={data.tier} />
              <span className="mono">{fmtDateTime(data.startsAt)}</span>
            </div>
            <div className="meta-line">
              <ClassChip cls={r.carClass} />
              <span>{r.vehName ?? '—'}</span>
              {r.carNumber && <span className="mono">#{r.carNumber}</span>}
              {r.teamName && <span className="note">{r.teamName}</span>}
            </div>
          </div>
          <div className="race-head-nums">
            <div>
              <span className="n">{finished && r.classPosition ? `P${r.classPosition}` : finishLabel(r.finishStatus)}</span>
              <span className="l">Na classe</span>
            </div>
            <div><span className="n">{r.position ? `P${r.position}` : '—'}</span><span className="l">Geral</span></div>
          </div>
        </header>

        <div className="tiles">
          <Tile n={r.classGridPos ? `P${r.classGridPos}` : '—'} l="Largada (classe)" />
          <Tile n={fmtLap(r.bestLapTime)} l={`Melhor volta · ${fmtGap(r.bestLapTime, data.classFastestLap)} da classe`} />
          <Tile n={String(r.laps)} l="Voltas" />
          <Tile n={String(r.pitStops)} l="Pit stops" />
          <Tile n={r.qualiClassPosition ? `P${r.qualiClassPosition}` : '—'} l={`Quali · ${fmtLap(r.qualiBestLapTime)}`} />
          <Tile n={finished ? fmtRaceTime(r.finishTime) : '—'} l="Tempo total" />
          <div className="tile">
            <span className="l">Driver Rank</span>
            <span className="tile-rank"><Rank title={r.driverRankTitle} tier={r.driverRankTier} /> <Delta value={r.driverRankAdjustment} /></span>
          </div>
          <div className="tile">
            <span className="l">Safety Rank</span>
            <span className="tile-rank"><Rank title={r.safetyRankTitle} tier={r.safetyRankTier} /> <Delta value={r.safetyRankAdjustment} /></span>
          </div>
        </div>

        <section aria-labelledby="sec-voltas">
          <div className="sec-h">
            <span className="eyebrow">Volta a volta</span>
            <h2 id="sec-voltas">Ritmo</h2>
          </div>
          {laps.length === 0 ? (
            <div className="card pad note">
              O volta a volta desta corrida não está disponível (corridas guardadas antes desta versão do scanner
              só têm o resumo).
            </div>
          ) : (
            <>
              <div className="card pad"><LapChart laps={laps} /></div>
              <div className="tbl-wrap">
                <table className="laps">
                  <thead>
                    <tr>
                      <th className="num">Volta</th><th className="num">Pos</th><th className="num">Tempo</th>
                      <th className="num">S1</th><th className="num">S2</th><th className="num">S3</th>
                      <th>Pneus</th><th>Desgaste (DE · DD · TE · TD)</th><th className="num">Comb.</th><th></th>
                    </tr>
                  </thead>
                  <tbody>
                    {laps.map((l) => (
                      <tr key={l.lap} className={l.pit ? 'pit-row' : undefined}>
                        <td className="num">{l.lap}</td>
                        <td className="num">{l.position ?? '—'}</td>
                        <td className={`num ${!l.pit && l.lapTime === bestLap ? 'best' : ''}`}>{fmtLap(l.lapTime)}</td>
                        <td className={`num ${l.s1 === best.s1 ? 'best' : ''}`}>{fmtSector(l.s1)}</td>
                        <td className={`num ${l.s2 === best.s2 ? 'best' : ''}`}>{fmtSector(l.s2)}</td>
                        <td className={`num ${l.s3 === best.s3 ? 'best' : ''}`}>{fmtSector(l.s3)}</td>
                        <td><Compounds lap={l} /></td>
                        <td><Wear lap={l} /></td>
                        <td className="num">{fmtPct(l.fuel)}</td>
                        <td>{l.pit && <span className="pit-tag">PIT</span>}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <p className="note">
                Roxo = melhor da corrida dele (volta e setores). Desgaste = borracha restante em cada pneu no fim
                da volta. Combustível em % do tanque.
              </p>
            </>
          )}
        </section>
      </div>
    </>
  )
}

function Tile({ n, l }: { n: string; l: string }) {
  return (
    <div className="tile">
      <span className="n">{n}</span>
      <span className="l">{l}</span>
    </div>
  )
}

const fmtSector = (s: number | null) => (s ? s.toFixed(3) : '—')

function Compounds({ lap }: { lap: Lap }) {
  const f = lap.frontCompound
  const r = lap.rearCompound
  if (!f && !r) return <span className="flat">—</span>
  if (f === r || !r) return <span className={`cmp ${compoundClass(f)}`}>{f}</span>
  return (
    <span className="cmp-pair">
      <span className={`cmp ${compoundClass(f)}`} title="Dianteiros">{f}</span>
      <span className={`cmp ${compoundClass(r)}`} title="Traseiros">{r}</span>
    </span>
  )
}

function Wear({ lap }: { lap: Lap }) {
  const tyres: [string, number | null][] = [
    ['Dianteiro esquerdo', lap.wearFL], ['Dianteiro direito', lap.wearFR],
    ['Traseiro esquerdo', lap.wearRL], ['Traseiro direito', lap.wearRR],
  ]
  if (tyres.every(([, v]) => v === null)) return <span className="flat">—</span>
  return (
    <span className="wear">
      {tyres.map(([name, v]) => (
        <span key={name} className="wear-bar" title={`${name}: ${fmtPct(v)}`}>
          <i className={wearClass(v)} style={{ height: `${Math.round(Math.min(1, Math.max(0, v ?? 0)) * 100)}%` }} />
        </span>
      ))}
      <span className="mono wear-min">{fmtPct(Math.min(...tyres.map(([, v]) => v ?? 1)))}</span>
    </span>
  )
}

const wearClass = (v: number | null) => (v === null ? '' : v >= 0.7 ? 'w-ok' : v >= 0.45 ? 'w-mid' : 'w-low')
