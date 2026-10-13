import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import type { RaceDetail, SplitResults } from '../api/types'
import { ClassBarCell, ClassChip, ClassChips, Delta, ErrorBox, Loading, Rank, TierChip } from '../components/Bits'
import { TopBar } from '../components/TopBar'
import { finishLabel, fmtDateTime, fmtLap, fmtRaceTime, typeLabel } from '../lib/format'
import { useApi } from '../lib/useApi'

const STATUS_NOTE: Record<string, string> = {
  Pending: 'Esta corrida ainda não terminou. Os resultados aparecem aqui alguns minutos depois do fim.',
  Ready: 'A corrida terminou e o resultado está sendo baixado. Volte em alguns minutos.',
  Failed: 'O resultado desta corrida não ficou disponível no RaceOS.',
}

export function RacePage() {
  const { eventId = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const race = useApi<RaceDetail>(`/lmu/races/${encodeURIComponent(eventId)}`)
  const splits = race.data?.splits ?? []
  const wanted = Number(params.get('split') ?? splits[0]?.splitNo ?? 1)
  const split = splits.some((s) => s.splitNo === wanted) ? wanted : (splits[0]?.splitNo ?? 1)
  const results = useApi<SplitResults>(splits.length ? `/lmu/races/${encodeURIComponent(eventId)}/splits/${split}` : null)

  if (race.loading) return <><TopBar /><div className="wrap"><Loading /></div></>
  if (race.notFound || !race.data) {
    return <><TopBar /><div className="wrap"><ErrorBox message={race.error ?? 'Corrida não encontrada.'} /></div></>
  }

  const r = race.data
  const rows = results.data?.rows ?? []
  const fastest = Math.min(...rows.map((x) => x.bestLapTime ?? Infinity))
  const totalDrivers = splits.reduce((a, s) => a + s.drivers, 0)

  return (
    <>
      <TopBar />
      <div className="wrap">
        <nav className="crumbs" aria-label="Caminho"><Link to="/corridas">Corridas</Link><span>/</span><span>{r.title ?? 'Corrida'}</span></nav>

        <header className="race-head card">
          <div className="race-head-main">
            <span className="eyebrow">{typeLabel(r.eventType)}</span>
            <h1>{r.title ?? 'Corrida'}</h1>
            <div className="meta-line">
              <span>{r.track ?? '—'}</span><TierChip tier={r.tier} /><ClassChips classes={r.classes} solid />
              <span className="mono">{fmtDateTime(r.startsAt)}</span>
            </div>
          </div>
          <div className="race-head-nums">
            <div><span className="n">{splits.length}</span><span className="l">Splits</span></div>
            <div><span className="n">{totalDrivers}</span><span className="l">Pilotos</span></div>
          </div>
        </header>

        {splits.length === 0 && <div className="card pad note">{STATUS_NOTE[r.status] ?? 'Sem resultado ainda.'}</div>}

        {splits.length > 0 && (
          <section aria-labelledby="sec-split">
            <div className="sec-h"><span className="eyebrow">Classificação</span><h2 id="sec-split">Split {split}</h2></div>
            <div className="chips" role="tablist" aria-label="Splits">
              {splits.map((s) => (
                <button key={s.splitNo} type="button" role="tab" aria-selected={s.splitNo === split} className="chip"
                  onClick={() => setParams({ split: String(s.splitNo) })}>
                  Split {s.splitNo} <span className="chip-sub">{s.drivers} pilotos</span>
                </button>
              ))}
            </div>

            {results.error && <ErrorBox message={results.error} />}
            <div className="tbl-wrap">
              <table className="results">
                <thead>
                  <tr>
                    <th aria-hidden="true"></th><th className="num">Pos</th><th className="num">Classe</th><th>Piloto</th><th>Carro</th>
                    <th className="num">Larg.</th><th className="num">Voltas</th><th className="num">Melhor volta</th>
                    <th className="num">Tempo</th><th>DR</th><th>SR</th><th className="num">ΔDR</th><th className="num">ΔSR</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.length ? rows.map((x) => (
                    <tr key={x.resultId} className="row-link" onClick={() => navigate(`/resultados/${x.resultId}`)}>
                      <ClassBarCell cls={x.carClass} />
                      <td className="num pos-cell">{x.position ?? '—'}</td>
                      <td className="num"><span className="mono">{x.classPosition ? `P${x.classPosition}` : '—'}</span> <ClassChip cls={x.carClass} /></td>
                      <td>
                        {x.driverId
                          ? <Link to={`/pilotos/${x.driverId}`} onClick={(e) => e.stopPropagation()}>{x.name}</Link>
                          : x.name}
                        {x.nationality && <span className="pill nat">{x.nationality}</span>}
                      </td>
                      <td className="car">{x.vehName ?? '—'}</td>
                      <td className="num">{x.classGridPos ?? x.gridPos ?? '—'}</td>
                      <td className="num">{x.laps}</td>
                      <td className={`num ${x.bestLapTime === fastest ? 'best' : ''}`}>{fmtLap(x.bestLapTime)}</td>
                      <td className="num">{x.finishStatus === 'Finished Normally' ? fmtRaceTime(x.finishTime) : finishLabel(x.finishStatus)}</td>
                      <td><Rank title={x.driverRankTitle} tier={x.driverRankTier} /></td>
                      <td><Rank title={x.safetyRankTitle} tier={x.safetyRankTier} /></td>
                      <td className="num"><Delta value={x.driverRankAdjustment} /></td>
                      <td className="num"><Delta value={x.safetyRankAdjustment} /></td>
                    </tr>
                  )) : (
                    <tr><td colSpan={13} className="empty">{results.loading ? 'Carregando…' : 'Sem pilotos neste split.'}</td></tr>
                  )}
                </tbody>
              </table>
            </div>
            <p className="note">Clique num piloto para ver o volta a volta dele nesta corrida. A volta mais rápida do split está em roxo; a faixa à esquerda é a cor da classe.</p>
          </section>
        )}
      </div>
    </>
  )
}
