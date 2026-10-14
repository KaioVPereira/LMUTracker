import { Link, useParams } from 'react-router-dom'
import type { HeadToHead, HeadToHeadSide } from '../api/types'
import { ClassChip, EmptyState, ErrorBox, Loading, Rank } from '../components/Bits'
import { TopBar } from '../components/TopBar'
import { finishLabel, fmtDateTime, fmtLap } from '../lib/format'
import { useApi } from '../lib/useApi'
import { Flag } from '../components/Flag'

const pos = (status: string | null, classPos: number | null) =>
  status === 'Finished Normally' && classPos ? `P${classPos}` : finishLabel(status)

/** Comparação direta: as corridas que os dois disputaram no mesmo split e quem terminou na frente. */
export function HeadToHeadPage() {
  const { driverId = '', otherId = '' } = useParams()
  const { data, loading, error, notFound } = useApi<HeadToHead>(`/lmu/drivers/${encodeURIComponent(driverId)}/vs/${encodeURIComponent(otherId)}`)

  if (loading) return <><TopBar /><div className="wrap"><Loading what="Comparando…" /></div></>
  if (!data) return <><TopBar /><div className="wrap"><ErrorBox message={notFound ? 'Piloto não encontrado.' : (error ?? 'Não deu para comparar.')} /></div></>

  const h = data
  const total = h.meAhead + h.themAhead
  const mePct = total ? Math.round((h.meAhead / total) * 100) : 50

  return (
    <>
      <TopBar />
      <div className="wrap">
        <nav className="crumbs" aria-label="Caminho">
          <Link to={`/pilotos/${h.me.driverId}`}>{h.me.name}</Link><span>/</span><span>Comparação</span>
        </nav>

        <header className="h2h-head">
          <Side s={h.me} align="left" />
          <div className="h2h-score" aria-label={`${h.me.name} ${h.meAhead} × ${h.themAhead} ${h.them.name}`}>
            <span className="eyebrow">Na frente no mesmo split</span>
            <div className="h2h-big"><b className={h.meAhead >= h.themAhead ? 'up' : ''}>{h.meAhead}</b><span>×</span><b className={h.themAhead > h.meAhead ? 'up' : ''}>{h.themAhead}</b></div>
            <span className="h2h-bar big"><i style={{ width: `${mePct}%` }} /></span>
            <span className="rel">{h.shared} corrida{h.shared === 1 ? '' : 's'} juntos</span>
            {h.avgBestLapGap !== null && (
              <span className="rel">
                melhor volta, mesma classe ({h.sameClassRaces}): {h.avgBestLapGap === 0 ? 'empate' : `${h.avgBestLapGap < 0 ? h.me.name : h.them.name} ${Math.abs(h.avgBestLapGap).toFixed(3)} s mais rápido em média`}
              </span>
            )}
          </div>
          <Side s={h.them} align="right" />
        </header>

        <section aria-labelledby="sec-juntos">
          <div className="sec-h"><h2 id="sec-juntos">Corridas juntos</h2><span className="rel">mais recentes primeiro</span></div>
          {h.races.length ? (
            <div className="tbl-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Data</th><th>Corrida</th><th className="num">Split</th>
                    <th>{h.me.name}</th><th className="num">Melhor volta</th>
                    <th>{h.them.name}</th><th className="num">Melhor volta</th>
                  </tr>
                </thead>
                <tbody>
                  {h.races.map((r) => {
                    const meWon = r.myPosition !== null && (r.theirPosition === null || r.myPosition < r.theirPosition)
                    const themWon = r.theirPosition !== null && (r.myPosition === null || r.theirPosition < r.myPosition)
                    return (
                      <tr key={`${r.eventId}-${r.splitNo}`}>
                        <td className="mono">{fmtDateTime(r.startsAt)}</td>
                        <td><Link to={`/corridas/${r.eventId}?split=${r.splitNo}`}>{r.title ?? 'Corrida'}</Link> <span className="rel">{r.track}</span></td>
                        <td className="num">{r.splitNo}</td>
                        <td className={meWon ? 'h2h-win' : ''}><ClassChip cls={r.myClass} /> <b className="mono">{pos(r.myStatus, r.myClassPosition)}</b> <span className="rel">geral {r.myPosition ?? '—'}</span></td>
                        <td className="num">{fmtLap(r.myBestLap)}</td>
                        <td className={themWon ? 'h2h-win' : ''}><ClassChip cls={r.theirClass} /> <b className="mono">{pos(r.theirStatus, r.theirClassPosition)}</b> <span className="rel">geral {r.theirPosition ?? '—'}</span></td>
                        <td className="num">{fmtLap(r.theirBestLap)}</td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          ) : (
            <EmptyState title="Ainda não correram juntos">
              Nenhuma corrida no mesmo split entre os dois desde que o Pitwall começou a registrar.
            </EmptyState>
          )}
          <p className="note">"Na frente" usa a posição geral no split; destaque verde = quem chegou na frente naquela corrida.</p>
        </section>
      </div>
    </>
  )
}

function Side({ s, align }: { s: HeadToHeadSide; align: 'left' | 'right' }) {
  return (
    <div className={`h2h-side is-${align}`}>
      <Link to={`/pilotos/${s.driverId}`} className="plain"><h2>{s.name}</h2></Link>
      <span className="driver-sub"><Flag code={s.nationality} withName /></span>
      <span className="h2h-ranks"><Rank title={s.driverRankTitle} tier={s.driverRankTier} /> · <Rank title={s.safetyRankTitle} tier={s.safetyRankTier} /></span>
      <span className="rel">{s.races} corridas · {s.wins} vitórias · {s.podiums} pódios</span>
    </div>
  )
}
