import { useMemo, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import type { EventInfo, RaceDetail, ResultRow, SplitEvents, SplitLaps, SplitResults } from '../api/types'
import { ClassBarCell, ClassChip, ClassChips, Delta, EmptyState, ErrorBox, Loading, Rank, TierChip } from '../components/Bits'
import { PaceChart, PositionChart, StrategyChart } from '../components/RaceCharts'
import { TopBar } from '../components/TopBar'
import { TrackMap } from '../components/TrackMap'
import { classKey, classShort } from '../lib/classes'
import { carLabel, crewMembers } from '../lib/crew'
import { fmtGameTime, skyLabel } from '../lib/eventPhase'
import { MAX_SERIES, SERIES } from '../lib/series'
import { finishLabel, fmtDateTime, fmtLap, fmtRaceTime, typeLabel } from '../lib/format'
import { useFollows } from '../lib/follows'
import { useApi } from '../lib/useApi'
import { Flag } from '../components/Flag'

const STATUS_NOTE: Record<string, string> = {
  Pending: 'Esta corrida ainda não terminou. Os resultados aparecem aqui alguns minutos depois do fim.',
  Ready: 'A corrida terminou e o resultado está sendo baixado. Volte em alguns minutos.',
  Failed: 'O resultado desta corrida não ficou disponível no RaceOS.',
}

type Tab = 'resultado' | 'posicoes' | 'ritmo' | 'estrategia' | 'penalidades'
const TABS: { key: Tab; label: string }[] = [
  { key: 'resultado', label: 'Resultado' },
  { key: 'posicoes', label: 'Posições' },
  { key: 'ritmo', label: 'Ritmo' },
  { key: 'estrategia', label: 'Estratégia' },
  { key: 'penalidades', label: 'Penalidades' },
]

export function RacePage() {
  const { eventId = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const race = useApi<RaceDetail>(`/lmu/races/${encodeURIComponent(eventId)}`)
  const splits = race.data?.splits ?? []
  const wanted = Number(params.get('split') ?? splits[0]?.splitNo ?? 1)
  const split = splits.some((s) => s.splitNo === wanted) ? wanted : (splits[0]?.splitNo ?? 1)
  const cls = params.get('classe') ?? ''
  const tab = (TABS.some((t) => t.key === params.get('aba')) ? params.get('aba') : 'resultado') as Tab
  const base = `/lmu/races/${encodeURIComponent(eventId)}/splits/${split}`
  const results = useApi<SplitResults>(splits.length ? base : null)
  // Volta a volta e penalidades só são pedidos quando a aba precisa deles.
  const laps = useApi<SplitLaps>(splits.length && tab !== 'resultado' && tab !== 'penalidades' ? `${base}/laps` : null)
  const events = useApi<SplitEvents>(splits.length && tab === 'penalidades' ? `${base}/events` : null)
  const [picked, setPicked] = useState<{ key: string; ids: string[] } | null>(null)
  const { isFollowing } = useFollows()

  const set = (next: Record<string, string | null>) => {
    const p = new URLSearchParams(params)
    for (const [k, v] of Object.entries(next)) { if (v) p.set(k, v); else p.delete(k) }
    setParams(p, { replace: true })
  }

  const rows = useMemo(() => results.data?.rows ?? [], [results.data])
  const classes = [...new Set(rows.map((x) => classKey(x.carClass)))].filter((k) => k !== 'other')
  const inClass = (c: string | null) => !cls || classKey(c) === cls
  const shownRows = rows.filter((x) => inClass(x.carClass))
  const lapDrivers = (laps.data?.drivers ?? []).filter((d) => inClass(d.carClass)).map((d) => ({ ...d, name: carLabel(d) }))
    .sort((a, b) => (cls ? (a.classPosition ?? 999) - (b.classPosition ?? 999) : (a.position ?? 999) - (b.position ?? 999)))

  // Destacados nos gráficos: escolha da pessoa para este split+classe, senão os 5 primeiros.
  const selKey = `${split}|${cls}`
  const selected = picked?.key === selKey ? picked.ids : lapDrivers.slice(0, 5).map((d) => d.resultId)
  const toggle = (id: string) => {
    const cur = selected.includes(id) ? selected.filter((x) => x !== id) : [...selected, id].slice(-MAX_SERIES)
    setPicked({ key: selKey, ids: cur })
  }

  if (race.loading) return <><TopBar /><div className="wrap"><Loading /></div></>
  if (race.notFound || !race.data) {
    return <><TopBar /><div className="wrap"><ErrorBox message={race.error ?? 'Corrida não encontrada.'} /></div></>
  }

  const r = race.data
  const fastest = Math.min(...shownRows.map((x) => x.bestLapTime ?? Infinity))
  const totalDrivers = splits.reduce((a, s) => a + s.drivers, 0)

  return (
    <>
      <TopBar />
      <div className="wrap">
        <nav className="crumbs" aria-label="Caminho"><Link to="/corridas">Corridas</Link><span>/</span><span>{r.title ?? 'Corrida'}</span></nav>

        <header className="race-head race-head-map">
          <div className="race-head-main">
            <span className="eyebrow">{typeLabel(r.eventType)}</span>
            <h1>{r.title ?? 'Corrida'}</h1>
            <div className="meta-line">
              {r.track ? <Link to={`/pistas/${encodeURIComponent(r.track)}`} className="plain">{r.track}</Link> : <span>—</span>}
              <TierChip tier={r.tier} /><ClassChips classes={r.classes} solid />
              <span className="mono">{fmtDateTime(r.startsAt)}</span>
            </div>
            <div className="race-head-nums">
              <div><span className="n">{splits.length}</span><span className="l">Splits</span></div>
              <div><span className="n">{totalDrivers}</span><span className="l">Pilotos</span></div>
              {splits[0]?.mostLapsCompleted ? <div><span className="n">{splits[0].mostLapsCompleted}</span><span className="l">Voltas (split 1)</span></div> : null}
            </div>
          </div>
          <TrackMap track={r.track} className="race-map" />
        </header>

        {r.info && <Conditions info={r.info} />}

        {splits.length === 0 && <div className="card pad note">{STATUS_NOTE[r.status] ?? 'Sem resultado ainda.'}</div>}

        {splits.length > 0 && (
          <section aria-labelledby="sec-split">
            <div className="sec-h"><h2 id="sec-split">Split {split}</h2><span className="rel">{splits.find((s) => s.splitNo === split)?.drivers ?? 0} pilotos</span></div>
            <div className="chips" role="tablist" aria-label="Splits">
              {splits.map((s) => (
                <button key={s.splitNo} type="button" role="tab" aria-selected={s.splitNo === split} className="chip"
                  onClick={() => set({ split: String(s.splitNo) })}>
                  Split {s.splitNo} <span className="chip-sub">{s.drivers}</span>
                </button>
              ))}
            </div>
            {classes.length > 1 && (
              <div className="chips" role="tablist" aria-label="Classe">
                <button type="button" role="tab" className="chip" aria-selected={!cls} onClick={() => set({ classe: null })}>Todas as classes</button>
                {classes.map((k) => (
                  <button key={k} type="button" role="tab" className={`chip chip-cls cls-${k}`} aria-selected={cls === k} onClick={() => set({ classe: k })}>
                    <span className="cls-dot" aria-hidden="true" />{classShort(k)}
                  </button>
                ))}
              </div>
            )}

            <div className="tabs-line" role="tablist" aria-label="Visão">
              {TABS.map((t) => (
                <button key={t.key} type="button" role="tab" aria-selected={tab === t.key} onClick={() => set({ aba: t.key === 'resultado' ? null : t.key })}>{t.label}</button>
              ))}
            </div>

            {results.error && <ErrorBox message={results.error} onRetry={results.reload} />}

            {tab === 'resultado' && (
              <>
                <div className="tbl-wrap">
                  <table className="results">
                    <thead>
                      <tr>
                        <th aria-hidden="true"></th><th className="num">Pos</th><th className="num">Classe</th><th>Piloto</th><th>Carro</th>
                        <th className="num">Larg.</th><th className="num" title="Posições ganhas na classe">+/−</th><th className="num">Voltas</th>
                        <th className="num">Melhor volta</th><th className="num">Tempo</th><th className="num">Pits</th>
                        <th className="num" title="Contatos relatados pelo jogo">Contatos</th><th className="num">Pen.</th>
                        <th>DR</th><th>SR</th><th className="num">ΔDR</th><th className="num">ΔSR</th>
                      </tr>
                    </thead>
                    <tbody>
                      {shownRows.length ? shownRows.map((x) => {
                        const gained = x.finishStatus === 'Finished Normally' && x.classGridPos && x.classPosition ? x.classGridPos - x.classPosition : null
                        return (
                          <tr key={x.resultId} className={`row-link${isFollowing(x.driverId) ? ' is-followed' : ''}`} onClick={() => navigate(`/resultados/${x.resultId}`)}>
                            <ClassBarCell cls={x.carClass} />
                            <td className="num pos-cell">{x.position ?? '—'}</td>
                            <td className="num"><span className="mono">{x.classPosition ? `P${x.classPosition}` : '—'}</span> <ClassChip cls={x.carClass} /></td>
                            <td>
                              {x.driverId
                                ? <Link to={`/pilotos/${x.driverId}`} onClick={(e) => e.stopPropagation()}>{x.name}</Link>
                                : x.name}
                              <Flag code={x.nationality} />
                              <CoDrivers row={x} />
                            </td>
                            <td className="car">{x.vehName ?? '—'}</td>
                            <td className="num">{x.classGridPos ?? x.gridPos ?? '—'}</td>
                            <td className={`num ${gained === null ? 'flat' : gained > 0 ? 'up' : gained < 0 ? 'down' : 'flat'}`}>{gained === null ? '—' : gained > 0 ? `▲${gained}` : gained < 0 ? `▼${-gained}` : '='}</td>
                            <td className="num">{x.laps}</td>
                            <td className={`num ${x.bestLapTime === fastest ? 'best' : ''}`}>{fmtLap(x.bestLapTime)}</td>
                            <td className="num">{x.finishStatus === 'Finished Normally' ? fmtRaceTime(x.finishTime) : finishLabel(x.finishStatus)}</td>
                            <td className="num">{x.pitStops}</td>
                            <td className="num">{x.incidents ?? '—'}</td>
                            <td className={`num ${x.penalties ? 'warn-t' : ''}`}>{x.penalties ?? '—'}</td>
                            <td><Rank title={x.driverRankTitle} tier={x.driverRankTier} /></td>
                            <td><Rank title={x.safetyRankTitle} tier={x.safetyRankTier} /></td>
                            <td className="num"><Delta value={x.driverRankAdjustment} /></td>
                            <td className="num"><Delta value={x.safetyRankAdjustment} /></td>
                          </tr>
                        )
                      }) : (
                        <tr><td colSpan={17} className="empty">{results.loading ? 'Carregando…' : 'Sem pilotos neste filtro.'}</td></tr>
                      )}
                    </tbody>
                  </table>
                </div>
                <p className="note">Toque num piloto para ver o volta a volta dele. Roxo = volta mais rápida {cls ? 'da classe' : 'do split'}; +/− = posições ganhas na classe da largada à chegada.</p>
              </>
            )}

            {(tab === 'posicoes' || tab === 'ritmo') && (
              laps.loading ? <Loading what="Carregando o volta a volta…" rows={3} /> : lapDrivers.length ? (
                <div className="chart-card">
                  <div className="pick" role="group" aria-label={`Pilotos destacados (até ${MAX_SERIES})`}>
                    {lapDrivers.slice(0, 16).map((d) => {
                      const i = selected.indexOf(d.resultId)
                      return (
                        <button key={d.resultId} type="button" className={`pick-chip${i >= 0 ? ' is-on' : ''}`} aria-pressed={i >= 0} onClick={() => toggle(d.resultId)}>
                          <i className="sw" style={{ background: i >= 0 ? SERIES[i] : 'transparent' }} />
                          <b>{cls ? `P${d.classPosition ?? '—'}` : `P${d.position ?? '—'}`}</b> {d.name}
                        </button>
                      )
                    })}
                  </div>
                  {tab === 'posicoes'
                    ? <PositionChart drivers={lapDrivers} selected={selected} byClass={!!cls} />
                    : <PaceChart drivers={lapDrivers} selected={selected} />}
                  <p className="note">
                    {tab === 'posicoes'
                      ? `Posição ${cls ? 'na classe' : 'geral'} na largada (grid) e ao fim de cada volta. Em cinza, os outros pilotos ${cls ? 'da classe' : 'do split'}. Escolha até ${MAX_SERIES} para destacar.`
                      : 'Tempo de cada volta, sem a 1ª, sem as de pit e sem as acima de 107% da melhor volta do piloto (rodadas, bandeira amarela).'}
                  </p>
                </div>
              ) : <EmptyState title="Sem volta a volta">Este split não tem o volta a volta guardado.</EmptyState>
            )}

            {tab === 'estrategia' && (
              laps.loading ? <Loading what="Carregando o volta a volta…" rows={3} /> : lapDrivers.length ? (
                <div className="chart-card">
                  <StrategyChart drivers={lapDrivers} />
                  <p className="note">Cada barra é a corrida do piloto, dividida nos stints (cor = composto do pneu traseiro). Traço escuro = parada no box.</p>
                </div>
              ) : <EmptyState title="Sem volta a volta">Este split não tem o volta a volta guardado.</EmptyState>
            )}

            {tab === 'penalidades' && (
              events.loading ? <Loading what="Carregando…" rows={3} /> : !events.data?.available ? (
                <EmptyState title="Sem registro de penalidades">
                  Esta corrida foi guardada antes de o Pitwall registrar as penalidades e as decisões dos comissários.
                </EmptyState>
              ) : events.data.events.length === 0 ? (
                <EmptyState title="Corrida sem penalidades">Nenhuma penalidade nem decisão dos comissários neste split.</EmptyState>
              ) : (
                <div className="rows">
                  {events.data.events.map((e, i) => (
                    <div key={i} className="stat-row">
                      <span className={`ev-kind ev-${e.kind}`}>{e.kind === 'penalty' ? 'Penalidade' : e.kind === 'served' ? 'Cumprida' : 'Comissários'}</span>
                      <span className="stat-main">
                        <b>{e.driver ?? '—'}{e.opponent ? <span className="rel"> × {e.opponent}</span> : null}</b>
                        <span>{e.detail ?? ''}</span>
                      </span>
                      <span className="mono rel">{e.lap ? `volta ${e.lap}` : e.at !== null ? fmtClockSec(e.at) : ''}</span>
                    </div>
                  ))}
                </div>
              )
            )}
          </section>
        )}
      </div>
    </>
  )
}

/** Carro dividido: os outros pilotos do carro, abaixo de quem terminou. */
function CoDrivers({ row }: { row: ResultRow }) {
  const others = crewMembers(row.crew).filter((m) => m.name.toLowerCase() !== row.name.toLowerCase())
  if (!others.length) return null
  return (
    <span className="crew-line">
      com {others.map((m, i) => (
        <span key={m.name}>{i > 0 && ', '}{m.driverId ? <Link to={`/pilotos/${m.driverId}`} onClick={(e) => e.stopPropagation()}>{m.name}</Link> : m.name}</span>
      ))}
    </span>
  )
}

/** 1148.9 s -> "19:08" (tempo da sessão). */
const fmtClockSec = (s: number) => `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, '0')}`

const LIMITS: Record<string, string> = { off: 'Desligados', default: 'Padrão', strict: 'Rígidos', relaxed: 'Relaxados' }
const KIND: Record<string, string> = { practice: 'Treino', qualifying: 'Classificação', warmup: 'Warm-up', race: 'Corrida' }

/** Condições e regras do evento: as sessões (duração, horário no jogo, clima) e o que a configuração define. */
function Conditions({ info }: { info: EventInfo }) {
  const facts: [string, string][] = []
  if (info.fixedSetup !== null) facts.push(['Setup', info.fixedSetup ? 'Fixo' : 'Livre'])
  if (info.tyreWear) facts.push(['Desgaste de pneu', `${info.tyreWear}×`])
  if (info.fuelUse) facts.push(['Consumo', `${info.fuelUse}×`])
  if (info.damage !== null && info.damage !== undefined) facts.push(['Dano', `${info.damage}%`])
  if (info.tireSets) facts.push(['Jogos de pneu', String(info.tireSets)])
  if (info.trackLimits) facts.push(['Limites de pista', LIMITS[info.trackLimits] ?? info.trackLimits])
  if (info.tyreWarmers !== null && info.tyreWarmers !== undefined) facts.push(['Aquecedores', info.tyreWarmers ? 'Sim' : 'Não'])
  if (info.maxPlayers) facts.push(['Grid por split', `até ${info.maxPlayers}`])
  return (
    <section aria-labelledby="sec-cond" className="cond">
      <div className="sec-h"><h2 id="sec-cond">Condições</h2></div>
      <div className="cond-grid">
        {info.sessions.map((s, i) => (
          <div key={i} className={`cond-sess${s.kind === 'race' ? ' is-race' : ''}`}>
            <span className="cond-kind">{KIND[s.kind] ?? s.kind}<small>{Math.round(s.minutes)} min</small></span>
            <span className="cond-line"><b className="mono">{fmtGameTime(s.timeOfDay)}</b> no jogo</span>
            <span className="cond-line">{s.temperatureC !== null ? `${s.temperatureC} °C · ` : ''}{skyLabel(s.sky) ?? '—'}{s.rainChance ? ` · chuva ${s.rainChance}%` : ''}</span>
          </div>
        ))}
        {facts.length > 0 && (
          <dl className="cond-facts">
            {facts.map(([k, v]) => <div key={k}><dt>{k}</dt><dd>{v}</dd></div>)}
          </dl>
        )}
      </div>
    </section>
  )
}
