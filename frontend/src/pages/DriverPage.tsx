import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import type { DriverProfile } from '../api/types'
import { ClassChip, Delta, ErrorBox, Loading, Progress, Rank, TierChip } from '../components/Bits'
import { Pager } from '../components/Pager'
import { TopBar } from '../components/TopBar'
import { badgeLabel, finishLabel, fmtDateTime, fmtInt, fmtLap, fmtRelative, rankClass } from '../lib/format'
import { useApi } from '../lib/useApi'

export function DriverPage() {
  const { driverId = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const page = Math.max(1, Number(params.get('pagina') ?? 1))
  const { data, loading, error } = useApi<DriverProfile>(`/lmu/drivers/${encodeURIComponent(driverId)}?page=${page}`)

  if (loading) return <><TopBar /><div className="wrap"><Loading /></div></>
  if (!data) return <><TopBar /><div className="wrap"><ErrorBox message={error ?? 'Piloto não encontrado.'} /></div></>

  const d = data
  const s = d.stats
  const badge = badgeLabel(d.badge)

  return (
    <>
      <TopBar />
      <div className="wrap">
        <nav className="crumbs" aria-label="Caminho"><Link to="/corridas">Corridas</Link><span>/</span><span>Pilotos</span><span>/</span><span>{d.name}</span></nav>

        <div className="driver">
          <div>
            <span className="eyebrow">Piloto</span>
            <span className="driver-name">{d.name}</span>
            <span className="driver-sub">
              {d.isMe && <span className="pill me">Você</span>}
              {d.nationality && <span className="pill">{d.nationality}</span>}
              {badge && <span className="pill">{badge}</span>}
              <span>visto {fmtRelative(d.lastSeenAt)}</span>
            </span>
          </div>
          <RankCard label="Driver Rank (DR)" title={d.driverRankTitle} tier={d.driverRankTier} progress={d.driverRankProgress} />
          <RankCard label="Safety Rank (SR)" title={d.safetyRankTitle} tier={d.safetyRankTier} progress={d.safetyRankProgress} />
        </div>

        <div className="tiles">
          <Tile n={fmtInt(s.races)} l="Corridas" />
          <Tile n={fmtInt(s.wins)} l="Vitórias na classe" />
          <Tile n={fmtInt(s.podiums)} l="Pódios" />
          <Tile n={fmtInt(s.top5)} l="Top 5" />
          <Tile n={fmtInt(s.poles)} l="Poles" />
          <Tile n={s.bestFinish ? `P${s.bestFinish}` : '—'} l="Melhor chegada" />
          <Tile n={fmtInt(s.dnfs)} l="Não terminou" />
          <Tile n={fmtInt(s.lapsCompleted)} l="Voltas completadas" />
        </div>

        <section aria-labelledby="sec-corridas">
          <div className="sec-h">
            <span className="eyebrow">Histórico</span>
            <h2 id="sec-corridas">Corridas</h2>
            <span className="rel">desde que o scanner começou a guardar</span>
          </div>
          <div className="tbl-wrap">
            <table>
              <thead>
                <tr>
                  <th>Data</th><th>Evento</th><th>Pista</th><th>Nível</th><th className="num">Split</th><th>Classe</th>
                  <th className="num">Larg.</th><th className="num">Chegada</th><th className="num">Melhor volta</th>
                  <th>DR</th><th>SR</th><th className="num">ΔDR</th><th className="num">ΔSR</th>
                </tr>
              </thead>
              <tbody>
                {d.races.items.length ? d.races.items.map((r) => (
                  <tr key={r.resultId} className="row-link" onClick={() => navigate(`/resultados/${r.resultId}`)}>
                    <td className="mono">{fmtDateTime(r.startsAt)}</td>
                    <td><Link to={`/resultados/${r.resultId}`} onClick={(e) => e.stopPropagation()}>{r.title ?? 'Corrida'}</Link></td>
                    <td>{r.track ?? '—'}</td>
                    <td><TierChip tier={r.tier} /></td>
                    <td className="num">{r.splitNo}</td>
                    <td><ClassChip cls={r.carClass} /></td>
                    <td className="num">{r.classGridPos ? `P${r.classGridPos}` : '—'}</td>
                    <td className="num">
                      {r.finishStatus === 'Finished Normally' && r.classPosition ? `P${r.classPosition}` : finishLabel(r.finishStatus)}
                    </td>
                    <td className="num">{fmtLap(r.bestLapTime)}</td>
                    <td><Rank title={r.driverRankTitle} tier={r.driverRankTier} /></td>
                    <td><Rank title={r.safetyRankTitle} tier={r.safetyRankTier} /></td>
                    <td className="num"><Delta value={r.driverRankAdjustment} /></td>
                    <td className="num"><Delta value={r.safetyRankAdjustment} /></td>
                  </tr>
                )) : (
                  <tr><td colSpan={13} className="empty">Nenhuma corrida guardada para este piloto.</td></tr>
                )}
              </tbody>
            </table>
          </div>
          <Pager page={d.races.page} pageSize={d.races.pageSize} total={d.races.total}
            onPage={(p) => setParams(p > 1 ? { pagina: String(p) } : {})} />
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

function RankCard({ label, title, tier, progress }: { label: string; title: string | null; tier: number | null; progress: number | null }) {
  return (
    <div>
      <span className="rank-lab">{label}</span>
      <span className={`rank-big ${rankClass(title)}`}>{title ?? '—'}{tier !== null && title ? ` ${tier}` : ''}</span>
      <Progress value={progress} title={title} />
      {progress !== null && <span className="mono note">{progress}% de progresso no tier</span>}
    </div>
  )
}
