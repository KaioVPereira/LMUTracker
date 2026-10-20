import { useMemo, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import type { Lap, ResultDetail, ResultRow, SplitDriverLaps, SplitEvents, SplitLaps, SplitResults } from '../api/types'
import { ClassChip, Delta, EmptyState, ErrorBox, Loading, Rank, TierChip } from '../components/Bits'
import { Flag } from '../components/Flag'
import { LapChart } from '../components/LapChart'
import { PaceChart, PositionChart, StrategyChart } from '../components/RaceCharts'
import { TopBar } from '../components/TopBar'
import { classKey } from '../lib/classes'
import { carLabel, crewMembers, driverOfLap } from '../lib/crew'
import {
  compoundClass, finishLabel, fmtDateTime, fmtGap, fmtLap, fmtPct, fmtRaceTime, typeLabel,
} from '../lib/format'
import { cleanPace, fmtDiff } from '../lib/pace'
import { MAX_SERIES, SERIES } from '../lib/series'
import { useApi } from '../lib/useApi'

type Tab = 'posicoes' | 'ritmo' | 'estrategia' | 'adversarios' | 'penalidades'
const TABS: { key: Tab; label: string }[] = [
  { key: 'posicoes', label: 'Posições' },
  { key: 'ritmo', label: 'Ritmo' },
  { key: 'estrategia', label: 'Estratégia' },
  { key: 'adversarios', label: 'Adversários' },
  { key: 'penalidades', label: 'Penalidades' },
]

const FINISHED = 'Finished Normally'

/**
 * O piloto numa corrida: o resultado dele, como foi a corrida (posições, ritmo e estratégia comparados com quem estava
 * perto na classe, adversários diretos e penalidades) e o volta a volta. Tudo o que a página da corrida tem, visto
 * a partir dele — dá para mostrar a corrida de alguém sem ficar trocando de tela.
 */
export function ResultPage() {
  const { resultId = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const { data, loading, error } = useApi<ResultDetail>(`/lmu/results/${encodeURIComponent(resultId)}`)
  const base = data ? `/lmu/races/${encodeURIComponent(data.eventId)}/splits/${data.splitNo}` : null
  const tab = (TABS.some((t) => t.key === params.get('aba')) ? params.get('aba') : 'posicoes') as Tab
  const split = useApi<SplitResults>(base)
  const lapsOfSplit = useApi<SplitLaps>(base ? `${base}/laps` : null)
  const events = useApi<SplitEvents>(base && tab === 'penalidades' ? `${base}/events` : null)
  const [picked, setPicked] = useState<{ key: string; ids: string[] } | null>(null)

  const r = data?.row
  const myClass = classKey(r?.carClass)
  // Os carros da classe dele no split, com o nome dos pilotos do carro quando dividido.
  const classDrivers = useMemo(() => (lapsOfSplit.data?.drivers ?? [])
    .filter((d) => classKey(d.carClass) === myClass)
    .map((d) => ({ ...d, name: carLabel(d) }))
    .sort((a, b) => (a.classPosition ?? 999) - (b.classPosition ?? 999)), [lapsOfSplit.data, myClass])
  const me = classDrivers.find((d) => d.resultId === resultId) ?? null

  // Destacados: ele, o vencedor da classe e quem chegou logo à frente e logo atrás.
  const defaults = useMemo(() => {
    if (!me) return []
    const at = (p: number | null) => classDrivers.find((d) => d.classPosition === p)?.resultId
    const ids = [me.resultId, at(1), me.classPosition ? at(me.classPosition - 1) : undefined, me.classPosition ? at(me.classPosition + 1) : undefined]
    return [...new Set(ids.filter(Boolean) as string[])]
  }, [me, classDrivers])
  const selected = picked?.key === resultId ? picked.ids : defaults
  const toggle = (id: string) => {
    if (id === resultId) return // o piloto da página fica sempre
    setPicked({ key: resultId, ids: selected.includes(id) ? selected.filter((x) => x !== id) : [...selected, id].slice(-MAX_SERIES) })
  }
  const setTab = (t: Tab) => {
    const p = new URLSearchParams(params)
    if (t === 'posicoes') p.delete('aba'); else p.set('aba', t)
    setParams(p, { replace: true })
  }

  if (loading) return <><TopBar /><div className="wrap"><Loading /></div></>
  if (!data || !r) return <><TopBar /><div className="wrap"><ErrorBox message={error ?? 'Resultado não encontrado.'} /></div></>

  const laps = data.laps
  const bestSector = (k: 's1' | 's2' | 's3') => Math.min(...laps.map((l) => l[k] ?? Infinity))
  const best = { s1: bestSector('s1'), s2: bestSector('s2'), s3: bestSector('s3') }
  const bestLap = Math.min(...laps.map((l) => (l.pit ? Infinity : (l.lapTime ?? Infinity))))
  const finished = r.finishStatus === FINISHED
  const crew = crewMembers(r.crew)
  const raceLink = `/corridas/${data.eventId}?split=${data.splitNo}${myClass !== 'other' ? `&classe=${myClass}` : ''}`

  return (
    <>
      <TopBar />
      <div className="wrap">
        <nav className="crumbs" aria-label="Caminho">
          <Link to="/corridas">Corridas</Link><span>/</span>
          <Link to={`/corridas/${data.eventId}?split=${data.splitNo}`}>{data.title ?? 'Corrida'} · Split {data.splitNo}</Link><span>/</span>
          <span>{crew.length > 1 ? crew.map((c) => c.name).join(' / ') : r.name}</span>
        </nav>

        <header className="race-head card">
          <div className="race-head-main">
            <span className="eyebrow">{typeLabel(data.eventType)} · Split {data.splitNo}</span>
            <h1>
              {crew.length > 1
                ? crew.map((c, i) => <span key={c.name}>{i > 0 && <span className="crew-sep"> / </span>}{c.driverId ? <Link to={`/pilotos/${c.driverId}`} className="plain">{c.name}</Link> : c.name}</span>)
                : r.driverId ? <Link to={`/pilotos/${r.driverId}`} className="plain">{r.name}</Link> : r.name}
            </h1>
            <div className="meta-line">
              <Link to={raceLink} className="plain">{data.title ?? 'Corrida'}</Link>
              {data.track ? <Link to={`/pistas/${encodeURIComponent(data.track)}`} className="plain">{data.track}</Link> : <span>—</span>}
              <TierChip tier={data.tier} />
              <span className="mono">{fmtDateTime(data.startsAt)}</span>
            </div>
            <div className="meta-line">
              <ClassChip cls={r.carClass} />
              <span>{r.vehName ?? '—'}</span>
              {r.carNumber && <span className="mono">#{r.carNumber}</span>}
              {r.teamName && <span className="note">{r.teamName}</span>}
              {crew.length > 1 && <span className="pill">Carro dividido · {crew.length} pilotos</span>}
            </div>
          </div>
          <div className="race-head-nums">
            <div>
              <span className="n">{finished && r.classPosition ? `P${r.classPosition}` : finishLabel(r.finishStatus)}</span>
              <span className="l">Na classe</span>
            </div>
            <div><span className="n">{r.position ? `P${r.position}` : '—'}</span><span className="l">Geral</span></div>
            <Link to={raceLink} className="btn btn-sm">Corrida completa</Link>
          </div>
        </header>

        <div className="tiles tiles-4">
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

        {crew.length > 1 && <CrewSection detail={data} />}

        <section aria-labelledby="sec-corrida" className="res-race">
          <div className="sec-h">
            <span className="eyebrow">Na corrida</span>
            <h2 id="sec-corrida">Como foi</h2>
            <Link to={raceLink} className="sec-link">Ver a corrida inteira →</Link>
          </div>

          {lapsOfSplit.loading ? <Loading what="Carregando a corrida…" rows={3} />
            : !me ? <EmptyState title="Sem volta a volta">Esta corrida não tem o volta a volta guardado — só o resultado acima.</EmptyState>
            : (
              <>
                <Insights me={me} classDrivers={classDrivers} row={r} />
                <div className="tabs-line" role="tablist" aria-label="Visão da corrida">
                  {TABS.map((t) => (
                    <button key={t.key} type="button" role="tab" aria-selected={tab === t.key} onClick={() => setTab(t.key)}>{t.label}</button>
                  ))}
                </div>

                {(tab === 'posicoes' || tab === 'ritmo') && (
                  <div className="chart-card">
                    <div className="pick" role="group" aria-label={`Pilotos destacados (até ${MAX_SERIES})`}>
                      {classDrivers.slice(0, 20).map((d) => {
                        const i = selected.indexOf(d.resultId)
                        return (
                          <button key={d.resultId} type="button" className={`pick-chip${i >= 0 ? ' is-on' : ''}${d.resultId === resultId ? ' is-me' : ''}`}
                            aria-pressed={i >= 0} onClick={() => toggle(d.resultId)}>
                            <i className="sw" style={{ background: i >= 0 ? SERIES[i] : 'transparent' }} />
                            <b>P{d.classPosition ?? '—'}</b> {d.name}
                          </button>
                        )
                      })}
                    </div>
                    {tab === 'posicoes'
                      ? <PositionChart drivers={classDrivers} selected={selected} byClass />
                      : <PaceChart drivers={classDrivers} selected={selected} />}
                    <p className="note">
                      {tab === 'posicoes'
                        ? 'Posição na classe na largada (grid) e ao fim de cada volta. Em cinza, os outros carros da classe. Destacados: o piloto, o vencedor da classe e quem chegou logo à frente e logo atrás — toque para trocar.'
                        : 'Tempo de cada volta, sem a 1ª, sem as de pit e sem as acima de 107% da melhor volta do piloto (rodadas, bandeira amarela).'}
                    </p>
                  </div>
                )}

                {tab === 'estrategia' && (
                  <div className="chart-card">
                    <StrategyChart drivers={neighbours(classDrivers, me, 4)} highlight={me.resultId} />
                    <p className="note">O piloto (em destaque) e os carros perto na classe. Cada barra é a corrida dividida nos stints (cor = composto do pneu traseiro); traço escuro = parada no box.</p>
                  </div>
                )}

                {tab === 'adversarios' && <Rivals me={me} classDrivers={classDrivers} rows={split.data?.rows ?? []} />}

                {tab === 'penalidades' && <Penalties events={events} names={[r.name, ...crew.map((c) => c.name)]} />}
              </>
            )}
        </section>

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
              <div className="card pad"><LapChart laps={laps} crew={crew.length > 1 ? r.crew : null} /></div>
              <div className="tbl-wrap">
                <table className="laps">
                  <thead>
                    <tr>
                      <th className="num">Volta</th>{crew.length > 1 && <th>Piloto</th>}<th className="num">Pos</th><th className="num">Tempo</th>
                      <th className="num">S1</th><th className="num">S2</th><th className="num">S3</th>
                      <th>Pneus</th><th>Desgaste (DE · DD · TE · TD)</th><th className="num">Comb.</th><th></th>
                    </tr>
                  </thead>
                  <tbody>
                    {laps.map((l) => (
                      <tr key={l.lap} className={l.pit ? 'pit-row' : undefined}>
                        <td className="num">{l.lap}</td>
                        {crew.length > 1 && <td className="crew-cell">{driverOfLap(r.crew, l.lap) ?? '—'}</td>}
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
                Roxo = melhor da corrida {crew.length > 1 ? 'do carro' : 'do piloto'} (volta e setores). Desgaste = borracha restante em cada pneu no fim
                da volta. Combustível em % do tanque.
              </p>
            </>
          )}
        </section>
      </div>
    </>
  )
}

/** Os carros a até `n` posições dele na classe (ele incluído), na ordem de chegada. */
function neighbours(classDrivers: SplitDriverLaps[], me: SplitDriverLaps, n: number) {
  const i = classDrivers.indexOf(me)
  const from = Math.max(0, i - n)
  return classDrivers.slice(from, Math.max(i + n + 1, from + 2 * n + 1))
}

/** Quatro leituras rápidas da corrida dele: largada→chegada, 1ª volta, ritmo na classe e melhor posição. */
function Insights({ me, classDrivers, row }: { me: SplitDriverLaps; classDrivers: SplitDriverLaps[]; row: ResultRow }) {
  const grid = me.classGridPos
  const lap1 = me.laps.find((l) => l.lap === 1)?.classPosition ?? null
  const finished = row.finishStatus === FINISHED
  const gained = finished && grid && row.classPosition ? grid - row.classPosition : null

  const paces = classDrivers.map((d) => ({ id: d.resultId, p: cleanPace(d.laps) })).filter((x) => x.p !== null) as { id: string; p: number }[]
  paces.sort((a, b) => a.p - b.p)
  const myPace = paces.find((x) => x.id === me.resultId)?.p ?? null
  const paceRank = myPace === null ? null : paces.findIndex((x) => x.id === me.resultId) + 1
  const bestLaps = classDrivers.map((d) => d.bestLap).filter((b): b is number => b !== null).sort((a, b) => a - b)
  const lapRank = me.bestLap === null ? null : bestLaps.indexOf(me.bestLap) + 1

  const positions = me.laps.map((l) => l.classPosition).filter((p): p is number => p !== null)
  const top = positions.length ? Math.min(...positions) : null
  const topLap = top === null ? null : me.laps.find((l) => l.classPosition === top)?.lap
  const led = me.laps.filter((l) => l.classPosition === 1).length

  return (
    <div className="tiles insight-tiles">
      <div className="tile">
        <span className="l">Largada → chegada</span>
        <span className="n">{grid ? `P${grid}` : '—'} → {finished && row.classPosition ? `P${row.classPosition}` : finishLabel(row.finishStatus)}</span>
        <span className={`tile-sub ${gained === null ? '' : gained > 0 ? 'up' : gained < 0 ? 'down' : ''}`}>
          {gained === null ? 'na classe' : gained > 0 ? `ganhou ${gained} posiç${gained === 1 ? 'ão' : 'ões'}` : gained < 0 ? `perdeu ${-gained} posiç${gained === -1 ? 'ão' : 'ões'}` : 'manteve a posição'}
        </span>
      </div>
      <div className="tile">
        <span className="l">1ª volta</span>
        <span className="n">{lap1 ? `P${lap1}` : '—'}</span>
        <span className={`tile-sub ${grid && lap1 ? (grid - lap1 > 0 ? 'up' : grid - lap1 < 0 ? 'down' : '') : ''}`}>
          {grid && lap1 ? (grid - lap1 > 0 ? `+${grid - lap1} na largada` : grid - lap1 < 0 ? `${grid - lap1} na largada` : 'sem mudar na largada') : 'na classe'}
        </span>
      </div>
      <div className="tile">
        <span className="l">Ritmo de corrida</span>
        <span className="n">{paceRank ? `${paceRank}º` : '—'}<small className="n-of"> de {paces.length}</small></span>
        <span className="tile-sub">
          {myPace === null ? 'poucas voltas limpas' : paceRank === 1 ? `o melhor da classe · ${fmtLap(myPace)}` : `${fmtDiff(myPace - paces[0].p)} s/volta do melhor · ${fmtLap(myPace)}`}
        </span>
      </div>
      <div className="tile">
        <span className="l">Melhor volta na classe</span>
        <span className="n">{lapRank ? `${lapRank}ª` : '—'}<small className="n-of"> de {bestLaps.length}</small></span>
        <span className="tile-sub">{top ? `melhor posição P${top}${topLap ? ` (volta ${topLap})` : ''}${led ? ` · ${led} volta${led === 1 ? '' : 's'} na liderança` : ''}` : '—'}</span>
      </div>
    </div>
  )
}

/** Adversários diretos: os carros perto dele na classe, com a diferença de volta, ritmo e na chegada. */
function Rivals({ me, classDrivers, rows }: { me: SplitDriverLaps; classDrivers: SplitDriverLaps[]; rows: ResultRow[] }) {
  const navigate = useNavigate()
  const near = neighbours(classDrivers, me, 3)
  const byId = new Map(rows.map((x) => [x.resultId, x]))
  const mine = byId.get(me.resultId)
  const myPace = cleanPace(me.laps)
  const gap = (o: ResultRow | undefined) => {
    if (!o || !mine) return '—'
    if (o.resultId === mine.resultId) return ''
    if (o.laps !== mine.laps) return `${o.laps > mine.laps ? '−' : '+'}${Math.abs(o.laps - mine.laps)} v`
    if (o.finishStatus !== FINISHED || mine.finishStatus !== FINISHED || !o.finishTime || !mine.finishTime) return '—'
    return `${fmtDiff(o.finishTime - mine.finishTime, 1)} s`
  }
  return (
    <>
      <div className="tbl-wrap">
        <table>
          <thead>
            <tr>
              <th className="num">Classe</th><th>Piloto</th><th>Carro</th><th className="num">Larg.</th>
              <th className="num">Melhor volta</th><th className="num">Δ volta</th><th className="num">Ritmo</th><th className="num">Δ ritmo</th>
              <th className="num">Pits</th><th className="num" title="Contatos relatados pelo jogo">Contatos</th><th className="num" title="Diferença na chegada para o piloto desta página">Chegada</th>
            </tr>
          </thead>
          <tbody>
            {near.map((d) => {
              const o = byId.get(d.resultId)
              const pace = cleanPace(d.laps)
              const isMe = d.resultId === me.resultId
              return (
                <tr key={d.resultId} className={`row-link${isMe ? ' is-me' : ''}`} onClick={() => navigate(`/resultados/${d.resultId}`)}>
                  <td className="num pos-cell">{d.finishStatus === FINISHED && d.classPosition ? `P${d.classPosition}` : finishLabel(d.finishStatus)}</td>
                  <td>
                    {d.driverId && !isMe ? <Link to={`/pilotos/${d.driverId}`} onClick={(e) => e.stopPropagation()}>{d.name}</Link> : <b>{d.name}</b>}
                    <Flag code={o?.nationality ?? null} />
                  </td>
                  <td className="car">{o?.vehName ?? d.car ?? '—'}</td>
                  <td className="num">{d.classGridPos ?? '—'}</td>
                  <td className="num">{fmtLap(d.bestLap)}</td>
                  <td className={`num ${deltaCls(d.bestLap, me.bestLap, isMe)}`}>{isMe || d.bestLap === null || me.bestLap === null ? '' : fmtDiff(d.bestLap - me.bestLap)}</td>
                  <td className="num">{fmtLap(pace)}</td>
                  <td className={`num ${deltaCls(pace, myPace, isMe)}`}>{isMe || pace === null || myPace === null ? '' : fmtDiff(pace - myPace)}</td>
                  <td className="num">{o?.pitStops ?? '—'}</td>
                  <td className="num">{o?.incidents ?? '—'}</td>
                  <td className="num">{gap(o)}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
      <p className="note">Até 3 carros à frente e atrás na classe. Δ = diferença para o piloto desta página (verde: o piloto foi mais rápido). Ritmo = mediana das voltas limpas.</p>
    </>
  )
}

/** Verde quando ele foi mais rápido que o outro (o outro tem tempo maior), vermelho quando o outro foi. */
const deltaCls = (other: number | null, mine: number | null, isMe: boolean) =>
  isMe || other === null || mine === null ? '' : other > mine ? 'up' : other < mine ? 'down' : 'flat'

function Penalties({ events, names }: { events: { data: SplitEvents | null; loading: boolean }; names: string[] }) {
  if (events.loading || !events.data) return <Loading what="Carregando…" rows={2} />
  if (!events.data.available) {
    return <EmptyState title="Sem registro de penalidades">Esta corrida foi guardada antes de o Pitwall registrar as penalidades e as decisões dos comissários.</EmptyState>
  }
  const mine = new Set(names.map((n) => n.toLowerCase()))
  const list = events.data.events.filter((e) => mine.has((e.driver ?? '').toLowerCase()) || mine.has((e.opponent ?? '').toLowerCase()))
  if (!list.length) return <EmptyState title="Corrida limpa de penalidades">Nenhuma penalidade nem decisão dos comissários envolvendo este carro.</EmptyState>
  return (
    <div className="rows">
      {list.map((e, i) => (
        <div key={i} className="stat-row">
          <span className={`ev-kind ev-${e.kind}`}>{e.kind === 'penalty' ? 'Penalidade' : e.kind === 'served' ? 'Cumprida' : 'Comissários'}</span>
          <span className="stat-main">
            <b>{e.driver ?? '—'}{e.opponent ? <span className="rel"> × {e.opponent}</span> : null}</b>
            <span>{e.detail ?? ''}</span>
          </span>
          <span className="mono rel">{e.lap ? `volta ${e.lap}` : e.at !== null ? `${Math.floor(e.at / 60)}:${String(Math.floor(e.at % 60)).padStart(2, '0')}` : ''}</span>
        </div>
      ))}
    </div>
  )
}

/** Carro dividido: os turnos de cada piloto, com voltas, melhor volta e ritmo de cada um. */
function CrewSection({ detail }: { detail: ResultDetail }) {
  const r = detail.row
  const members = crewMembers(r.crew).map((m) => {
    const stints = (r.crew ?? []).filter((s) => s.name.toLowerCase() === m.name.toLowerCase())
    const laps = detail.laps.filter((l) => stints.some((s) => l.lap >= s.startLap && l.lap <= s.endLap))
    const valid = laps.filter((l) => !l.pit && l.lapTime).map((l) => l.lapTime!)
    const pace = cleanPace(laps.map((l) => ({ lap: l.lap, position: l.position, classPosition: null, lapTime: l.lapTime, elapsed: l.elapsed, pit: l.pit, compound: null, fuel: l.fuel })))
    return { ...m, stints, laps: laps.length, best: valid.length ? Math.min(...valid) : null, pace }
  })
  const total = Math.max(1, ...detail.laps.map((l) => l.lap), ...(r.crew ?? []).map((s) => s.endLap))
  const color = (name: string) => SERIES[members.findIndex((m) => m.name === name) % SERIES.length]
  return (
    <section aria-labelledby="sec-crew">
      <div className="sec-h"><span className="eyebrow">Equipe</span><h2 id="sec-crew">Pilotos do carro</h2><span className="rel">quem guiou cada trecho</span></div>
      <div className="chart-card">
        <div className="crew-bar" aria-hidden="true">
          {(r.crew ?? []).map((s) => (
            <i key={s.startLap} title={`${s.name} · voltas ${s.startLap}–${s.endLap}`}
              style={{ left: `${((s.startLap - 1) / total) * 100}%`, width: `${((s.endLap - s.startLap + 1) / total) * 100}%`, background: color(s.name) }} />
          ))}
        </div>
        <div className="rows crew-rows">
          {members.map((m) => (
            <div key={m.name} className="stat-row">
              <span className="stat-main">
                <b><i className="sw" style={{ background: color(m.name), borderColor: 'transparent' }} />{m.driverId ? <Link to={`/pilotos/${m.driverId}`} className="plain">{m.name}</Link> : m.name}{m.name === r.name && <span className="tag tag-up">terminou</span>}</b>
                <span>{m.stints.map((s) => `voltas ${s.startLap}–${s.endLap}`).join(' · ')}</span>
              </span>
              <span className="stat-nums">
                <span><small>voltas</small><b>{m.laps}</b></span>
                <span><small>melhor</small><b className="mono">{fmtLap(m.best)}</b></span>
                <span><small>ritmo</small><b className="mono">{fmtLap(m.pace)}</b></span>
              </span>
            </div>
          ))}
        </div>
        <p className="note">O resultado é do carro: posição, tempo e DR/SR do topo são de quem terminou ({r.name}). Cada piloto vê esta corrida no próprio perfil, com a melhor volta dos turnos dele.</p>
      </div>
    </section>
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
