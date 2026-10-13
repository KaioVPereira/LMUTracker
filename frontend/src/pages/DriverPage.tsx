import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import type { Achievement, CarStat, DriverProfile, DriverProfileStats, Rival, SearchResults, TrackStat } from '../api/types'
import { ClassBarCell, ClassChip, Delta, EmptyState, ErrorBox, Loading, Progress, Rank, Skeleton, TierChip } from '../components/Bits'
import { Pager } from '../components/Pager'
import { ActivityHeatmap, RankChart } from '../components/ProfileCharts'
import { UnlinkSteamButton } from '../components/SteamLink'
import { TopBar } from '../components/TopBar'
import { badgeLabel, finishLabel, fmtDateTime, fmtDay, fmtInt, fmtLap, fmtRelative, rankClass } from '../lib/format'
import { useApi } from '../lib/useApi'

const pct = (n: number, d: number) => (d > 0 ? Math.round((n / d) * 100) : 0)
const fmt1 = (v: number | null | undefined) => (v === null || v === undefined ? '—' : v.toLocaleString('pt-BR', { maximumFractionDigits: 1 }))

export function DriverPage() {
  const { driverId = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const page = Math.max(1, Number(params.get('pagina') ?? 1))
  const { data, loading, error } = useApi<DriverProfile>(`/lmu/drivers/${encodeURIComponent(driverId)}?page=${page}`)
  const stats = useApi<DriverProfileStats>(`/lmu/drivers/${encodeURIComponent(driverId)}/stats`)

  if (loading && !data) return <><TopBar /><div className="wrap"><Loading /></div></>
  if (!data) return <><TopBar /><div className="wrap"><ErrorBox message={error ?? 'Piloto não encontrado.'} /></div></>

  const d = data
  const badge = badgeLabel(d.badge)
  const s = stats.data?.summary

  return (
    <>
      <TopBar />
      <div className="wrap">
        <nav className="crumbs" aria-label="Caminho"><Link to="/corridas">Corridas</Link><span>/</span><span>Pilotos</span><span>/</span><span>{d.name}</span></nav>

        <header className="profile-head">
          <div className="profile-id">
            <span className="eyebrow">{d.isMe ? 'Seu perfil' : 'Piloto'}</span>
            <h1>{d.name}</h1>
            <span className="driver-sub">
              {d.isMe && <span className="pill me">Você</span>}
              {d.nationality && <span className="pill">{d.nationality}</span>}
              {badge && <span className="pill">{badge}</span>}
              <span>visto {fmtRelative(d.lastSeenAt)}</span>
            </span>
            {d.isMe && <span><UnlinkSteamButton onDone={() => navigate('/perfil')} /></span>}
          </div>
          <RankCard label="Driver Rank" title={d.driverRankTitle} tier={d.driverRankTier} progress={d.driverRankProgress} />
          <RankCard label="Safety Rank" title={d.safetyRankTitle} tier={d.safetyRankTier} progress={d.safetyRankProgress} />
        </header>

        <section aria-label="Resumo" className="tiles tiles-4">
          {s ? (
            <>
              <Tile n={fmtInt(s.races)} l="corridas" sub={`${s.topSplitRaces} no split 1`} />
              <Tile n={fmtInt(s.wins)} l="vitórias na classe" sub={`${pct(s.wins, s.races)}% das corridas`} />
              <Tile n={fmtInt(s.podiums)} l="pódios" sub={`${pct(s.podiums, s.races)}% · ${s.poles} pole${s.poles === 1 ? '' : 's'}`} />
              <Tile n={`${pct(s.dnfs, s.races)}%`} l="abandono" sub={`${s.dnfs} de ${s.races} corridas`} />
              <Tile n={s.avgPositionsGained === null ? '—' : `${s.avgPositionsGained > 0 ? '+' : ''}${fmt1(s.avgPositionsGained)}`}
                l="posições por corrida" sub="da largada à chegada, na classe" tone={s.avgPositionsGained === null ? undefined : s.avgPositionsGained >= 0 ? 'up' : 'down'} />
              <Tile n={fmt1(s.incidentsPerRace)} l="contatos por corrida" sub={s.racesWithIncidents ? `${s.cleanRaces} corrida${s.cleanRaces === 1 ? '' : 's'} limpa${s.cleanRaces === 1 ? '' : 's'} · ${s.penalties} penalidade${s.penalties === 1 ? '' : 's'}` : 'sem dados ainda'} />
              <Tile n={s.paceConsistency === null ? '—' : `${fmt1(s.paceConsistency)}%`} l="variação de ritmo" sub="voltas limpas; menor é mais constante" />
              <Tile n={fmt1(s.avgClassFinish)} l="chegada média" sub={`largada média ${fmt1(s.avgQualiClassPosition)}`} />
            </>
          ) : stats.error ? <ErrorBox message="Não deu para carregar as estatísticas." onRetry={stats.reload} /> : (
            [0, 1, 2, 3, 4, 5, 6, 7].map((i) => <div key={i} className="tile"><Skeleton w={60} h={30} /><Skeleton w="70%" /></div>)
          )}
        </section>

        {stats.data && (
          <>
            <div className="profile-grid">
              <section aria-labelledby="sec-evo">
                <div className="sec-h"><h2 id="sec-evo">Evolução do rank</h2></div>
                <RankChart points={stats.data.rankHistory} />
              </section>
              <section aria-labelledby="sec-ativ">
                <div className="sec-h"><h2 id="sec-ativ">Atividade</h2><span className="rel">desde que o Pitwall começou a registrar</span></div>
                <ActivityHeatmap dates={stats.data.rankHistory.map((p) => p.at)} weeks={16} />
              </section>
            </div>

            <div className="profile-grid">
              <TracksSection tracks={stats.data.tracks} />
              <CarsSection cars={stats.data.cars} />
            </div>

            <div className="profile-grid">
              <RivalsSection rivals={stats.data.rivals} meId={d.driverId} />
              <AchievementsSection list={stats.data.achievements} />
            </div>
          </>
        )}

        <section aria-labelledby="sec-corridas">
          <div className="sec-h">
            <h2 id="sec-corridas">Histórico</h2>
            <span className="rel">{d.races.total} corrida{d.races.total === 1 ? '' : 's'} desde que o scanner começou a guardar</span>
          </div>
          <div className="tbl-wrap">
            <table>
              <thead>
                <tr>
                  <th aria-hidden="true"></th>
                  <th>Data</th><th>Evento</th><th>Pista</th><th>Nível</th><th className="num">Split</th><th>Classe</th>
                  <th className="num">Larg.</th><th className="num">Chegada</th><th className="num">Melhor volta</th>
                  <th>DR</th><th>SR</th><th className="num">ΔDR</th><th className="num">ΔSR</th>
                </tr>
              </thead>
              <tbody>
                {d.races.items.length ? d.races.items.map((r) => (
                  <tr key={r.resultId} className="row-link" onClick={() => navigate(`/resultados/${r.resultId}`)}>
                    <ClassBarCell cls={r.carClass} />
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
                  <tr><td colSpan={14} className="empty">Nenhuma corrida guardada para este piloto.</td></tr>
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

function Tile({ n, l, sub, tone }: { n: string; l: string; sub?: string; tone?: 'up' | 'down' }) {
  return (
    <div className="tile">
      <span className={`n${tone ? ` ${tone}` : ''}`}>{n}</span>
      <span className="l">{l}</span>
      {sub && <span className="tile-sub">{sub}</span>}
    </div>
  )
}

function RankCard({ label, title, tier, progress }: { label: string; title: string | null; tier: number | null; progress: number | null }) {
  return (
    <div className="profile-rank">
      <span className="rank-lab">{label}</span>
      <span className={`rank-big ${rankClass(title)}`}>{title ?? '—'}{tier !== null && title ? ` ${tier}` : ''}</span>
      <Progress value={progress} title={title} />
      {progress !== null && <span className="rel">{progress}% para o próximo tier</span>}
    </div>
  )
}

/** Pistas por classe, da melhor chegada média para a pior. Com 2+ pistas, marca a melhor e a pior. */
function TracksSection({ tracks }: { tracks: TrackStat[] }) {
  const ranked = tracks.filter((t) => t.avgClassFinish !== null)
  const best = ranked.length >= 2 ? ranked[0] : null
  const worst = ranked.length >= 2 ? ranked[ranked.length - 1] : null
  return (
    <section aria-labelledby="sec-pistas">
      <div className="sec-h"><h2 id="sec-pistas">Pistas</h2><span className="rel">pela chegada média na classe</span></div>
      {tracks.length ? (
        <div className="rows">
          {tracks.map((t) => (
            <div key={`${t.track}-${t.carClass}`} className="stat-row">
              <span className="stat-main">
                <b>{t.track} <ClassChip cls={t.carClass} />
                  {t === best && <span className="tag tag-up">Melhor</span>}
                  {t === worst && <span className="tag tag-down">Pior</span>}
                </b>
                <span>{t.races} corrida{t.races === 1 ? '' : 's'} · {t.wins} vitória{t.wins === 1 ? '' : 's'} · {t.podiums} pódio{t.podiums === 1 ? '' : 's'}{t.incidentsPerRace !== null ? ` · ${fmt1(t.incidentsPerRace)} contatos/corrida` : ''}</span>
              </span>
              <span className="stat-nums">
                <span><small>média</small><b>{t.avgClassFinish === null ? '—' : `P${fmt1(t.avgClassFinish)}`}</b></span>
                <span><small>melhor</small><b>{t.bestClassFinish ? `P${t.bestClassFinish}` : '—'}</b></span>
                <span><small>volta</small><b className="mono">{fmtLap(t.bestLap)}</b></span>
              </span>
            </div>
          ))}
        </div>
      ) : <EmptyState title="Sem pistas ainda" />}
    </section>
  )
}

/** Carros mais usados, com taxa de vitória e de pódio em barras (uma cor só: é magnitude). */
function CarsSection({ cars }: { cars: CarStat[] }) {
  return (
    <section aria-labelledby="sec-carros">
      <div className="sec-h"><h2 id="sec-carros">Carros</h2><span className="rel">mais usados</span></div>
      {cars.length ? (
        <div className="rows">
          {cars.slice(0, 8).map((c) => (
            <div key={`${c.car}-${c.carClass}`} className="stat-row">
              <span className="stat-main">
                <b>{c.car} <ClassChip cls={c.carClass} /></b>
                <span>{c.races} corrida{c.races === 1 ? '' : 's'} · melhor {c.bestClassFinish ? `P${c.bestClassFinish}` : '—'} · {c.dnfs} abandono{c.dnfs === 1 ? '' : 's'}</span>
              </span>
              <span className="rate-bars">
                <RateBar label="vitórias" n={c.wins} of={c.races} />
                <RateBar label="pódios" n={c.podiums} of={c.races} />
              </span>
            </div>
          ))}
        </div>
      ) : <EmptyState title="Sem carros ainda" />}
    </section>
  )
}

function RateBar({ label, n, of }: { label: string; n: number; of: number }) {
  const p = pct(n, of)
  return (
    <span className="rate" title={`${n} de ${of}`}>
      <small>{label}</small>
      <span className="rate-track" aria-hidden="true"><i style={{ width: `${p}%` }} /></span>
      <b className="mono">{p}%</b>
    </span>
  )
}

/** Quem mais correu no mesmo split que ele, com o placar de quem chegou na frente; e comparação com qualquer piloto. */
function RivalsSection({ rivals, meId }: { rivals: Rival[]; meId: string }) {
  return (
    <section aria-labelledby="sec-rivais">
      <div className="sec-h"><h2 id="sec-rivais">Rivais frequentes</h2><span className="rel">mesmo split, 2+ corridas</span></div>
      <ComparePicker meId={meId} />
      {rivals.length ? (
        <div className="rows">
          {rivals.map((r) => (
            <div key={(r.driverId ?? r.name) + r.sharedRaces} className="stat-row">
              <span className="stat-main">
                <b>{r.driverId ? <Link to={`/pilotos/${r.driverId}`} className="plain">{r.name}</Link> : r.name}{r.nationality && <span className="pill">{r.nationality}</span>}</b>
                <span>{r.sharedRaces} corridas juntos · última {fmtRelative(r.lastRaced)}</span>
              </span>
              <span className="h2h-mini" aria-label={`${r.aheadOfThem} vezes na frente, ${r.behindThem} atrás`}>
                <b className={r.aheadOfThem >= r.behindThem ? 'up' : ''}>{r.aheadOfThem}</b>
                <span className="h2h-bar"><i style={{ width: `${pct(r.aheadOfThem, r.aheadOfThem + r.behindThem)}%` }} /></span>
                <b className={r.behindThem > r.aheadOfThem ? 'down' : ''}>{r.behindThem}</b>
              </span>
              {r.driverId && <Link to={`/pilotos/${meId}/vs/${r.driverId}`} className="btn btn-sm">Comparar</Link>}
            </div>
          ))}
        </div>
      ) : <p className="note">Ainda sem rival com 2 corridas no mesmo split.</p>}
    </section>
  )
}

/** Campo para comparar com qualquer piloto (usa a busca global, só pilotos). */
function ComparePicker({ meId }: { meId: string }) {
  const navigate = useNavigate()
  const [q, setQ] = useState('')
  const [list, setList] = useState<{ id: string; name: string; sub: string }[]>([])
  const term = q.trim()
  useEffect(() => {
    if (term.length < 2) return
    let alive = true
    const id = window.setTimeout(() => {
      api<SearchResults>(`/lmu/search?q=${encodeURIComponent(term)}`)
        .then((r) => { if (alive) setList(r.drivers.filter((x) => x.driverId !== meId).map((x) => ({ id: x.driverId, name: x.name, sub: `${x.nationality ?? ''} · ${x.races} corridas` }))) })
        .catch(() => { if (alive) setList([]) })
    }, 180)
    return () => { alive = false; window.clearTimeout(id) }
  }, [term, meId])
  const shown = term.length >= 2 ? list : []
  return (
    <div className="compare">
      <label className="sr-only" htmlFor="cmp-q">Comparar com outro piloto</label>
      <input id="cmp-q" className="compare-input" placeholder="Comparar com outro piloto…" value={q}
        onChange={(e) => setQ(e.target.value)} autoComplete="off" />
      {shown.length > 0 && (
        <ul className="compare-list">
          {shown.slice(0, 6).map((x) => (
            <li key={x.id}><button type="button" onClick={() => navigate(`/pilotos/${meId}/vs/${x.id}`)}><b>{x.name}</b><span>{x.sub}</span></button></li>
          ))}
        </ul>
      )}
    </div>
  )
}

function AchievementsSection({ list }: { list: Achievement[] }) {
  const done = list.filter((a) => a.achieved).length
  return (
    <section aria-labelledby="sec-conq">
      <div className="sec-h"><h2 id="sec-conq">Conquistas</h2><span className="rel">{done} de {list.length}</span></div>
      <div className="ach-grid">
        {[...list].sort((a, b) => Number(b.achieved) - Number(a.achieved)).map((a) => (
          <div key={a.key} className={`ach${a.achieved ? ' is-on' : ''}`} title={a.description}>
            <span className="ach-ico" aria-hidden="true">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                {a.achieved ? <path d="M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0zM17 5h3v2a3 3 0 0 1-3 3M7 5H4v2a3 3 0 0 0 3 3" /> : <><rect x="5" y="11" width="14" height="10" rx="2" /><path d="M8 11V7a4 4 0 0 1 8 0v4" /></>}
              </svg>
            </span>
            <span className="ach-text">
              <b>{a.title}</b>
              <span>{a.description}</span>
              {a.achieved && a.at && <small>{a.eventId ? <Link to={`/corridas/${a.eventId}`} className="plain">em {fmtDay(a.at)}</Link> : `em ${fmtDay(a.at)}`}</small>}
              {!a.achieved && a.goal && (
                <span className="ach-prog"><span className="rate-track"><i style={{ width: `${pct(a.progress ?? 0, a.goal)}%` }} /></span><small className="mono">{a.progress ?? 0}/{a.goal}</small></span>
              )}
            </span>
          </div>
        ))}
      </div>
      <p className="note">Contam a partir de quando o Pitwall começou a registrar as corridas.</p>
    </section>
  )
}
