import { Link, useParams } from 'react-router-dom'
import type { CarDetail } from '../api/types'
import { ClassChip, CountUp, EmptyState, ErrorBox, Loading } from '../components/Bits'
import { TopBar } from '../components/TopBar'
import { classKey } from '../lib/classes'
import { fmtLap } from '../lib/format'
import { useApi } from '../lib/useApi'

const pct = (n: number, d: number) => (d > 0 ? Math.round((n / d) * 100) : 0)

/** Página do carro: números gerais, pistas (com a melhor volta e quem fez) e os pilotos que mais ganharam com ele. */
export function CarPage() {
  const { car = '' } = useParams()
  const c = useApi<CarDetail>(`/lmu/cars/${encodeURIComponent(car)}`)

  if (c.loading) return <><TopBar /><div className="wrap"><Loading /></div></>
  if (!c.data) return <><TopBar /><div className="wrap"><ErrorBox message={c.notFound ? 'Nenhuma largada registrada com este carro.' : (c.error ?? 'Carro não encontrado.')} /></div></>
  const d = c.data
  const k = classKey(d.carClass)

  return (
    <>
      <TopBar />
      <div className="wrap">
        <nav className="crumbs" aria-label="Caminho"><Link to="/rankings">Rankings</Link><span>/</span><span>Carros</span><span>/</span><span>{d.car}</span></nav>

        <header className={`race-head car-head cls-${k}`}>
          <div className="race-head-main">
            <span className="eyebrow">Carro</span>
            <h1>{d.car}</h1>
            <div className="meta-line"><ClassChip cls={d.carClass} solid /><span className="rel">{d.drivers.toLocaleString('pt-BR')} pilotos já correram com ele</span></div>
          </div>
          <div className="race-head-nums">
            <div><span className="n"><CountUp value={d.starts} /></span><span className="l">Largadas</span></div>
            <div><span className="n">{pct(d.wins, d.starts)}%</span><span className="l">Vitórias</span></div>
            <div><span className="n">{pct(d.podiums, d.starts)}%</span><span className="l">Pódios</span></div>
            <div><span className="n">{pct(d.dnfs, d.starts)}%</span><span className="l">Abandono</span></div>
          </div>
        </header>

        <div className="profile-grid">
          <section aria-labelledby="sec-pistas">
            <div className="sec-h"><h2 id="sec-pistas">Pistas</h2><span className="rel">melhor volta com este carro</span></div>
            {d.tracks.length ? (
              <div className="rows">
                {d.tracks.map((t) => (
                  <div key={t.track} className="stat-row">
                    <span className="stat-main">
                      <b><Link to={`/pistas/${encodeURIComponent(t.track)}`} className="plain">{t.track}</Link></b>
                      <span>{t.starts.toLocaleString('pt-BR')} largadas · {t.wins} vitórias{t.bestDriverName ? ' · ' : ''}{t.bestDriverName && (t.bestDriverId ? <Link to={`/pilotos/${t.bestDriverId}`} className="plain">{t.bestDriverName}</Link> : t.bestDriverName)}</span>
                    </span>
                    <span className="stat-nums"><span><small>melhor volta</small><b className="mono">{fmtLap(t.bestLap)}</b></span></span>
                    <Link to={`/rankings?pista=${encodeURIComponent(t.track)}&classe=${k}&carro=${encodeURIComponent(d.car)}`} className="btn btn-sm">Ranking</Link>
                  </div>
                ))}
              </div>
            ) : <EmptyState title="Sem pistas ainda" />}
          </section>
          <section aria-labelledby="sec-pil">
            <div className="sec-h"><h2 id="sec-pil">Quem mais ganhou</h2><span className="rel">com este carro</span></div>
            {d.topDrivers.length ? (
              <div className="rows">
                {d.topDrivers.map((p, i) => (
                  <div key={(p.driverId ?? p.name) + i} className="stat-row">
                    <span className="hl-rank">{i + 1}</span>
                    <span className="stat-main">
                      <b>{p.driverId ? <Link to={`/pilotos/${p.driverId}`} className="plain">{p.name}</Link> : p.name}{p.nationality && <span className="pill">{p.nationality}</span>}</b>
                      <span>{p.starts} largadas</span>
                    </span>
                    <span className="stat-nums"><span><small>vitórias</small><b>{p.wins}</b></span><span><small>pódios</small><b>{p.podiums}</b></span></span>
                  </div>
                ))}
              </div>
            ) : <EmptyState title="Sem pilotos ainda" />}
          </section>
        </div>
      </div>
    </>
  )
}
