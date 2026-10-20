import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import type {
  Compare, CompareRace, CompareSide, CompareSuggestion, CompareSuggestions, DriverProfile, DriverProfileStats, SearchResults,
} from '../api/types'
import { EmptyState, ErrorBox, Loading, Rank } from '../components/Bits'
import { Flag } from '../components/Flag'
import { TopBar } from '../components/TopBar'
import { classKey, classShort } from '../lib/classes'
import { countryName, finishLabel, fmtDay, fmtDayShort, fmtLap } from '../lib/format'
import { SERIES } from '../lib/series'
import { useApi } from '../lib/useApi'

const FINISHED = 'Finished Normally'
const fmt1 = (v: number | null | undefined) => (v === null || v === undefined ? '—' : v.toLocaleString('pt-BR', { maximumFractionDigits: 1 }))
const fmtSec = (v: number | null | undefined) => (v ? v.toFixed(3) : '—')

type Combo = { track: string; cls: string; mine: number; theirs: number }

/**
 * Comparativo entre dois pilotos numa pista e classe: melhor volta setor a setor (onde cada um ganha ou perde tempo),
 * volta ideal, ritmo, constância, chegadas e as corridas de cada um ali. O adversário vem da busca, dos rivais ou de
 * atalhos ("quem mais venceu aqui", "dono da volta mais rápida"). Tudo na URL: ?com=&pista=&classe=.
 */
export function ComparePage() {
  const { driverId = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const other = params.get('com') ?? ''
  const me = useApi<DriverProfile>(`/lmu/drivers/${encodeURIComponent(driverId)}`)
  const myStats = useApi<DriverProfileStats>(`/lmu/drivers/${encodeURIComponent(driverId)}/stats`)
  const them = useApi<DriverProfile>(other ? `/lmu/drivers/${encodeURIComponent(other)}` : null)
  const theirStats = useApi<DriverProfileStats>(other ? `/lmu/drivers/${encodeURIComponent(other)}/stats` : null)

  const set = (next: Record<string, string | null>) => {
    const p = new URLSearchParams(params)
    for (const [k, v] of Object.entries(next)) { if (v) p.set(k, v); else p.delete(k) }
    setParams(p, { replace: true })
  }

  // Pistas e classes dele (variantes da classe juntas), as em comum com o adversário primeiro.
  const combos = useMemo<Combo[]>(() => {
    const map = new Map<string, Combo>()
    const add = (list: DriverProfileStats['tracks'] | undefined, side: 'mine' | 'theirs') => {
      for (const t of list ?? []) {
        const cls = classKey(t.carClass)
        if (cls === 'other') continue
        const k = `${t.track}|${cls}`
        const c = map.get(k) ?? { track: t.track, cls, mine: 0, theirs: 0 }
        c[side] += t.races
        map.set(k, c)
      }
    }
    add(myStats.data?.tracks, 'mine')
    add(theirStats.data?.tracks, 'theirs')
    return [...map.values()].filter((c) => c.mine > 0)
      .sort((a, b) => Number(b.theirs > 0) - Number(a.theirs > 0) || (b.mine + b.theirs) - (a.mine + a.theirs))
  }, [myStats.data, theirStats.data])

  const wanted = params.get('pista') && params.get('classe') ? { track: params.get('pista')!, cls: params.get('classe')! } : null
  const combo = wanted ?? (combos[0] ? { track: combos[0].track, cls: combos[0].cls } : null)
  const cmp = useApi<Compare>(other && combo
    ? `/lmu/drivers/${encodeURIComponent(driverId)}/compare/${encodeURIComponent(other)}?track=${encodeURIComponent(combo.track)}&class=${combo.cls}`
    : null)

  if (me.loading) return <><TopBar /><div className="wrap"><Loading /></div></>
  if (!me.data) return <><TopBar /><div className="wrap"><ErrorBox message={me.error ?? 'Piloto não encontrado.'} /></div></>
  const a = me.data

  return (
    <>
      <TopBar />
      <div className="wrap">
        <nav className="crumbs" aria-label="Caminho">
          <Link to={`/pilotos/${a.driverId}`}>{a.name}</Link><span>/</span><span>Comparar</span>
        </nav>
        <div className="page-head">
          <span className="eyebrow">Comparativo</span>
          <h1>{a.name}{them.data ? <> <span className="vs">vs</span> {them.data.name}</> : null}</h1>
          <p className="note">Escolha a pista, a classe e com quem comparar. O comparativo usa as corridas oficiais que o Pitwall guardou.</p>
        </div>

        <section className="cmp-pick card pad" aria-label="Escolhas">
          <div className="cmp-step">
            <span className="cmp-step-n">1</span>
            <div className="cmp-step-body">
              <b>Pista e classe</b>
              {myStats.loading ? <Loading rows={1} /> : combos.length ? (
                <div className="chips">
                  {combos.slice(0, 18).map((c) => {
                    const on = combo?.track === c.track && combo.cls === c.cls
                    return (
                      <button key={`${c.track}|${c.cls}`} type="button" className={`chip chip-cls cls-${c.cls}`} aria-selected={on}
                        onClick={() => set({ pista: c.track, classe: c.cls })}>
                        <span className="cls-dot" aria-hidden="true" />{c.track} · {classShort(c.cls)}
                        <span className="chip-sub">{c.mine}{other ? ` × ${c.theirs}` : ''}</span>
                      </button>
                    )
                  })}
                </div>
              ) : <p className="note">{a.name} ainda não tem corrida guardada.</p>}
              {other && <p className="note">Números = corridas de cada um ali ({a.name} × {them.data?.name ?? '…'}). As pistas em comum vêm primeiro.</p>}
            </div>
          </div>

          <div className="cmp-step">
            <span className="cmp-step-n">2</span>
            <div className="cmp-step-body">
              <b>Adversário</b>
              {other ? (
                <div className="cmp-chosen">
                  {them.data ? <span><Link to={`/pilotos/${other}`}>{them.data.name}</Link><Flag code={them.data.nationality} /></span> : <span className="rel">carregando…</span>}
                  <button type="button" className="btn btn-sm btn-ghost" onClick={() => set({ com: null })}>Trocar adversário</button>
                  <button type="button" className="btn btn-sm btn-ghost" title="Trocar os lados"
                    onClick={() => navigate(`/pilotos/${other}/comparar?com=${driverId}${combo ? `&pista=${encodeURIComponent(combo.track)}&classe=${combo.cls}` : ''}`)}>⇄ Inverter</button>
                </div>
              ) : (
                <>
                  <OpponentSearch exclude={driverId} onPick={(id) => set({ com: id })} />
                  {combo && <Suggestions track={combo.track} cls={combo.cls} exclude={driverId} onPick={(id) => set({ com: id })} />}
                  {!!myStats.data?.rivals.length && (
                    <div className="sugg-col">
                      <span className="sugg-h">Rivais frequentes</span>
                      <div className="chips">
                        {myStats.data.rivals.filter((r) => r.driverId).slice(0, 6).map((r) => (
                          <button key={r.driverId} type="button" className="chip" onClick={() => set({ com: r.driverId! })}>
                            {r.name}<span className="chip-sub">{r.sharedRaces} juntos</span>
                          </button>
                        ))}
                      </div>
                    </div>
                  )}
                </>
              )}
            </div>
          </div>
        </section>

        {other && combo && (
          cmp.loading ? <Loading what="Comparando…" rows={4} />
            : cmp.error || !cmp.data ? <ErrorBox message={cmp.error ?? 'Não deu para comparar.'} onRetry={cmp.reload} />
            : <CompareView c={cmp.data} />
        )}
      </div>
    </>
  )
}

// ---------------------------------------------------------------- resultado

function CompareView({ c }: { c: Compare }) {
  const { me, them } = c
  const where = `${c.track} · ${classShort(c.carClass)}`
  if (!me.starts && !them.starts) return <EmptyState title="Nenhum dos dois correu aqui">Escolha outra pista ou classe.</EmptyState>
  return (
    <>
      <header className="h2h-head cmp-head">
        <Side s={me} align="left" color={SERIES[0]} />
        <div className="h2h-score">
          <span className="eyebrow">Melhor volta · {where}</span>
          <div className="cmp-best">
            <b className={faster(me.bestLap?.lapTime, them.bestLap?.lapTime)}>{fmtLap(me.bestLap?.lapTime)}</b>
            <span>×</span>
            <b className={faster(them.bestLap?.lapTime, me.bestLap?.lapTime)}>{fmtLap(them.bestLap?.lapTime)}</b>
          </div>
          {me.bestLap && them.bestLap && (
            <span className="rel">
              {me.bestLap.lapTime === them.bestLap.lapTime ? 'empate'
                : `${me.bestLap.lapTime < them.bestLap.lapTime ? me.name : them.name} ${Math.abs(me.bestLap.lapTime - them.bestLap.lapTime).toFixed(3)} s mais rápido`}
            </span>
          )}
        </div>
        <Side s={them} align="right" color={SERIES[1]} />
      </header>

      <div className="profile-grid">
        <Sectors me={me} them={them} />
        <Duel me={me} them={them} />
      </div>

      <section aria-labelledby="sec-evo-volta">
        <div className="sec-h"><h2 id="sec-evo-volta">Melhor volta em cada corrida</h2><span className="rel">{where}</span></div>
        <div className="chart-card"><LapTrend me={me} them={them} /></div>
      </section>

      <div className="profile-grid">
        <RaceList s={me} color={SERIES[0]} />
        <RaceList s={them} color={SERIES[1]} />
      </div>

      {c.shared.length > 0 && (
        <section aria-labelledby="sec-juntos">
          <div className="sec-h">
            <h2 id="sec-juntos">Corridas juntos aqui</h2><span className="rel">mesmo split</span>
            <Link to={`/pilotos/${me.driverId}/vs/${them.driverId}`} className="sec-link">Todas as corridas juntos →</Link>
          </div>
          <div className="tbl-wrap">
            <table>
              <thead>
                <tr><th>Data</th><th>Corrida</th><th className="num">Split</th><th>{me.name}</th><th className="num">Volta</th><th>{them.name}</th><th className="num">Volta</th></tr>
              </thead>
              <tbody>
                {c.shared.map((r) => {
                  const meWon = r.myPosition !== null && (r.theirPosition === null || r.myPosition < r.theirPosition)
                  const themWon = r.theirPosition !== null && (r.myPosition === null || r.theirPosition < r.myPosition)
                  return (
                    <tr key={`${r.eventId}-${r.splitNo}`}>
                      <td className="mono">{fmtDay(r.startsAt)}</td>
                      <td><Link to={`/corridas/${r.eventId}?split=${r.splitNo}`}>{r.title ?? 'Corrida'}</Link></td>
                      <td className="num">{r.splitNo}</td>
                      <td className={meWon ? 'h2h-win' : ''}><b className="mono">{pos(r.myStatus, r.myClassPosition)}</b></td>
                      <td className={`num ${faster(r.myBestLap, r.theirBestLap)}`}>{fmtLap(r.myBestLap)}</td>
                      <td className={themWon ? 'h2h-win' : ''}><b className="mono">{pos(r.theirStatus, r.theirClassPosition)}</b></td>
                      <td className={`num ${faster(r.theirBestLap, r.myBestLap)}`}>{fmtLap(r.theirBestLap)}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </section>
      )}

      <p className="note">
        Setores da melhor volta de cada um (podem ser de corridas e condições diferentes). Volta ideal = soma dos melhores
        setores de todas as voltas ali. Ritmo = mediana das voltas limpas de cada corrida (sem a 1ª, sem pit, até 107% da
        melhor), na média das corridas. Constância = variação típica das voltas limpas numa corrida (menor é melhor).
      </p>
    </>
  )
}

const pos = (status: string | null, classPos: number | null) => (status === FINISHED && classPos ? `P${classPos}` : finishLabel(status))

/** Classe "best" no lado mais rápido (menor tempo). */
const faster = (mine: number | null | undefined, other: number | null | undefined) =>
  mine && other && mine < other ? 'best' : ''

function Side({ s, align, color }: { s: CompareSide; align: 'left' | 'right'; color: string }) {
  return (
    <div className={`h2h-side is-${align}`}>
      <span className="cmp-swatch" style={{ background: color }} aria-hidden="true" />
      <Link to={`/pilotos/${s.driverId}`} className="plain"><h2>{s.name}</h2></Link>
      <span className="driver-sub"><Flag code={s.nationality} withName /></span>
      <span className="h2h-ranks"><Rank title={s.driverRankTitle} tier={s.driverRankTier} /> · <Rank title={s.safetyRankTitle} tier={s.safetyRankTier} /></span>
      <span className="rel">{s.starts} corrida{s.starts === 1 ? '' : 's'} aqui{s.mainCar ? ` · ${s.mainCar}` : ''}</span>
    </div>
  )
}

/** Setor a setor da melhor volta + volta ideal: quanto cada um ganha em cada trecho. */
function Sectors({ me, them }: { me: CompareSide; them: CompareSide }) {
  const rows: { label: string; a: number | null; b: number | null; total?: boolean }[] = [
    { label: 'Setor 1', a: me.bestLap?.s1 ?? null, b: them.bestLap?.s1 ?? null },
    { label: 'Setor 2', a: me.bestLap?.s2 ?? null, b: them.bestLap?.s2 ?? null },
    { label: 'Setor 3', a: me.bestLap?.s3 ?? null, b: them.bestLap?.s3 ?? null },
    { label: 'Melhor volta', a: me.bestLap?.lapTime ?? null, b: them.bestLap?.lapTime ?? null, total: true },
  ]
  const ideal = (s: CompareSide) => (s.bestS1 && s.bestS2 && s.bestS3 ? s.bestS1 + s.bestS2 + s.bestS3 : null)
  rows.push({ label: 'Volta ideal', a: ideal(me), b: ideal(them), total: true })
  const scale = Math.max(0.2, ...rows.filter((r) => !r.total && r.a && r.b).map((r) => Math.abs(r.a! - r.b!)))
  const sectorRows = rows.slice(0, 3).filter((r) => r.a && r.b)
  const worst = sectorRows.length ? sectorRows.reduce((w, r) => (r.a! - r.b! > w.a! - w.b! ? r : w)) : null
  const bestFor = sectorRows.length ? sectorRows.reduce((w, r) => (r.a! - r.b! < w.a! - w.b! ? r : w)) : null

  return (
    <section aria-labelledby="sec-setores">
      <div className="sec-h"><h2 id="sec-setores">Setor a setor</h2><span className="rel">onde cada um ganha tempo</span></div>
      <div className="rows sectors">
        <div className="sector-row sector-h" aria-hidden="true">
          <span /><span className="mono" style={{ color: SERIES[0] }}>{me.name}</span><span /><span className="mono" style={{ color: SERIES[1] }}>{them.name}</span>
        </div>
        {rows.map((r) => {
          const d = r.a && r.b ? r.a - r.b : null // negativo = ele mais rápido
          const w = d === null ? 0 : Math.min(50, (Math.abs(d) / scale) * 50)
          return (
            <div key={r.label} className={`sector-row${r.total ? ' is-total' : ''}`}>
              <span className="sector-l">{r.label}</span>
              <span className={`mono ${d !== null && d < 0 ? 'best' : ''}`}>{r.total ? fmtLap(r.a) : fmtSec(r.a)}</span>
              <span className="sector-bar" aria-label={d === null ? 'sem dados' : `diferença ${Math.abs(d).toFixed(3)} s`}>
                {d !== null && d !== 0 && !r.total && (
                  <i style={{ [d < 0 ? 'right' : 'left']: '50%', width: `${w}%`, background: SERIES[d < 0 ? 0 : 1] }} />
                )}
                <em className="mono">{d === null ? '—' : d === 0 ? '=' : Math.abs(d).toFixed(3)}</em>
              </span>
              <span className={`mono ${d !== null && d > 0 ? 'best' : ''}`}>{r.total ? fmtLap(r.b) : fmtSec(r.b)}</span>
            </div>
          )
        })}
      </div>
      {worst && bestFor && worst.a! - worst.b! > 0.05 && (
        <p className="note">
          {me.name} perde mais tempo no <b>{worst.label.toLowerCase()}</b> ({(worst.a! - worst.b!).toFixed(3)} s)
          {bestFor.a! - bestFor.b! < -0.05 ? <> e ganha no <b>{bestFor.label.toLowerCase()}</b> ({(bestFor.b! - bestFor.a!).toFixed(3)} s)</> : null}.
        </p>
      )}
      {(!me.bestLap?.s1 || !them.bestLap?.s1) && <p className="note">Sem setores para quem só tem corridas guardadas antes do volta a volta.</p>}
    </section>
  )
}

/** Os números lado a lado; o melhor de cada linha em destaque. */
function Duel({ me, them }: { me: CompareSide; them: CompareSide }) {
  type Row = { label: string; a: number | null; b: number | null; fmt: (v: number | null) => string; lower?: boolean; neutral?: boolean }
  const rows: Row[] = [
    { label: 'Corridas', a: me.starts, b: them.starts, fmt: (v) => fmt1(v), neutral: true },
    { label: 'Vitórias', a: me.wins, b: them.wins, fmt: (v) => fmt1(v) },
    { label: 'Pódios', a: me.podiums, b: them.podiums, fmt: (v) => fmt1(v) },
    { label: 'Chegada média', a: me.avgClassFinish, b: them.avgClassFinish, fmt: (v) => (v === null ? '—' : `P${fmt1(v)}`), lower: true },
    { label: 'Largada média', a: me.avgClassGrid, b: them.avgClassGrid, fmt: (v) => (v === null ? '—' : `P${fmt1(v)}`), lower: true },
    { label: 'Posições ganhas', a: me.avgPositionsGained, b: them.avgPositionsGained, fmt: (v) => (v === null ? '—' : `${v > 0 ? '+' : ''}${fmt1(v)}`) },
    { label: 'Ritmo de corrida', a: me.pace, b: them.pace, fmt: (v) => fmtLap(v), lower: true },
    { label: 'Constância', a: me.consistency, b: them.consistency, fmt: (v) => (v === null ? '—' : `${fmt1(v)}%`), lower: true },
    { label: 'Contatos por corrida', a: me.incidentsPerRace, b: them.incidentsPerRace, fmt: (v) => fmt1(v), lower: true },
    { label: 'Abandonos', a: me.dnfs, b: them.dnfs, fmt: (v) => fmt1(v), lower: true },
  ]
  const win = (x: number | null, y: number | null, r: Row) => !r.neutral && x !== null && y !== null && x !== y && (r.lower ? x < y : x > y)
  return (
    <section aria-labelledby="sec-duelo">
      <div className="sec-h"><h2 id="sec-duelo">Lado a lado</h2></div>
      <div className="rows duel">
        {rows.map((r) => (
          <div key={r.label} className="duel-row">
            <b className={`mono ${win(r.a, r.b, r) ? 'duel-win' : ''}`}>{r.fmt(r.a)}</b>
            <span>{r.label}</span>
            <b className={`mono ${win(r.b, r.a, r) ? 'duel-win' : ''}`}>{r.fmt(r.b)}</b>
          </div>
        ))}
        {me.pace && them.pace && (
          <p className="note duel-foot">
            No ritmo de corrida, {me.pace < them.pace ? me.name : them.name} anda {Math.abs(me.pace - them.pace).toFixed(3)} s por volta mais rápido.
          </p>
        )}
      </div>
    </section>
  )
}

/** Melhor volta de cada corrida ao longo do tempo, um ponto por corrida (mais rápido = mais alto). */
function LapTrend({ me, them }: { me: CompareSide; them: CompareSide }) {
  const series = [me, them].map((s, i) => ({
    s, color: SERIES[i],
    pts: s.races.filter((r) => r.bestLap).map((r) => ({ t: Date.parse(r.startsAt), v: r.bestLap!, r })).sort((x, y) => x.t - y.t),
  }))
  const all = series.flatMap((x) => x.pts)
  if (all.length < 2) return <p className="note">Poucas corridas para mostrar a evolução.</p>
  // Ignora voltas muito lentas (chuva, corrida abandonada cedo) para não achatar o gráfico.
  const best = Math.min(...all.map((p) => p.v))
  const shown = all.filter((p) => p.v <= best * 1.06)
  const W = 760, H = 240, PL = 64, PR = 16, PT = 12, PB = 26
  const t0 = Math.min(...all.map((p) => p.t)), t1 = Math.max(...all.map((p) => p.t))
  const lo = Math.min(...shown.map((p) => p.v)), hi = Math.max(...shown.map((p) => p.v))
  const pad = Math.max(0.15, (hi - lo) * 0.1)
  const x = (t: number) => PL + ((t - t0) / Math.max(1, t1 - t0)) * (W - PL - PR)
  const y = (v: number) => PT + ((Math.min(v, hi + pad) - (lo - pad)) / (hi - lo + 2 * pad)) * (H - PT - PB)
  const ticks = [lo, (lo + hi) / 2, hi]
  return (
    <>
      <div className="chart-box is-wide cmp-trend">
        <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label="Melhor volta de cada corrida dos dois pilotos">
          {ticks.map((t) => (
            <g key={t}>
              <line x1={PL} x2={W - PR} y1={y(t)} y2={y(t)} className="grid" />
              <text x={PL - 8} y={y(t) + 4} textAnchor="end" className="axis">{fmtLap(t)}</text>
            </g>
          ))}
          {series.map(({ s, color, pts }) => {
            const vis = pts.filter((p) => p.v <= best * 1.06)
            return (
              <g key={s.driverId}>
                <path d={vis.map((p, i) => `${i ? 'L' : 'M'}${x(p.t).toFixed(1)} ${y(p.v).toFixed(1)}`).join(' ')} fill="none" stroke={color} strokeWidth={2} strokeLinejoin="round" opacity={0.85} />
                {vis.map((p) => (
                  <circle key={p.r.resultId} cx={x(p.t)} cy={y(p.v)} r={3.5} fill={color} className="ring">
                    <title>{`${s.name} · ${fmtDay(p.r.startsAt)} · ${fmtLap(p.v)}`}</title>
                  </circle>
                ))}
              </g>
            )
          })}
          <text x={PL} y={H - 6} className="axis">{fmtDayShort(new Date(t0).toISOString())}</text>
          <text x={W - PR} y={H - 6} textAnchor="end" className="axis">{fmtDayShort(new Date(t1).toISOString())}</text>
        </svg>
      </div>
      <div className="strat-legend">
        {series.map(({ s, color }) => <span key={s.driverId}><i className="sw" style={{ background: color, borderColor: 'transparent' }} />{s.name}</span>)}
        <span>mais alto = mais rápido</span>
      </div>
    </>
  )
}

function RaceList({ s, color }: { s: CompareSide; color: string }) {
  const navigate = useNavigate()
  const [all, setAll] = useState(false)
  const list: CompareRace[] = all ? s.races : s.races.slice(0, 8)
  const best = Math.min(...s.races.map((r) => r.bestLap ?? Infinity))
  return (
    <section aria-label={`Corridas de ${s.name}`}>
      <div className="sec-h"><h2><span className="cmp-swatch inline" style={{ background: color }} aria-hidden="true" />{s.name}</h2><span className="rel">{s.starts} corrida{s.starts === 1 ? '' : 's'} aqui</span></div>
      {s.races.length ? (
        <>
          <div className="tbl-wrap">
            <table className="cmp-races">
              <thead><tr><th>Data</th><th className="num">Split</th><th className="num">Larg. → Cheg.</th><th className="num">Melhor volta</th><th className="num">Ritmo</th></tr></thead>
              <tbody>
                {list.map((r) => (
                  <tr key={r.resultId} className="row-link" onClick={() => navigate(`/resultados/${r.resultId}`)}>
                    <td className="mono"><Link to={`/resultados/${r.resultId}`} onClick={(e) => e.stopPropagation()}>{fmtDay(r.startsAt)}</Link></td>
                    <td className="num">{r.splitNo}</td>
                    <td className="num">{r.classGridPos ? `P${r.classGridPos}` : '—'} → {pos(r.finishStatus, r.classPosition)}</td>
                    <td className={`num ${r.bestLap === best ? 'best' : ''}`}>{fmtLap(r.bestLap)}</td>
                    <td className="num">{fmtLap(r.pace)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {s.races.length > 8 && <div className="more-row"><button type="button" className="btn btn-sm" onClick={() => setAll((v) => !v)}>{all ? 'Mostrar menos' : `Ver as ${s.races.length}`}</button></div>}
        </>
      ) : <p className="note">Sem corrida guardada nesta pista e classe.</p>}
    </section>
  )
}

// ---------------------------------------------------------------- escolha do adversário

function OpponentSearch({ exclude, onPick }: { exclude: string; onPick: (id: string) => void }) {
  const [q, setQ] = useState('')
  const [list, setList] = useState<{ id: string; name: string; sub: string }[]>([])
  const term = q.trim()
  useEffect(() => {
    if (term.length < 2) return
    let alive = true
    const t = window.setTimeout(() => {
      api<SearchResults>(`/lmu/search?q=${encodeURIComponent(term)}`)
        .then((r) => { if (alive) setList(r.drivers.filter((x) => x.driverId !== exclude).map((x) => ({ id: x.driverId, name: x.name, sub: `${x.nationality ? `${countryName(x.nationality)} · ` : ''}${x.races} corridas` }))) })
        .catch(() => { if (alive) setList([]) })
    }, 180)
    return () => { alive = false; window.clearTimeout(t) }
  }, [term, exclude])
  const shown = term.length >= 2 ? list : []
  return (
    <div className="compare">
      <label className="sr-only" htmlFor="cmp-search">Buscar piloto</label>
      <input id="cmp-search" className="compare-input" placeholder="Buscar piloto pelo nome…" value={q} onChange={(e) => setQ(e.target.value)} autoComplete="off" />
      {shown.length > 0 && (
        <ul className="compare-list">
          {shown.slice(0, 6).map((x) => (
            <li key={x.id}><button type="button" onClick={() => onPick(x.id)}><b>{x.name}</b><span>{x.sub}</span></button></li>
          ))}
        </ul>
      )}
    </div>
  )
}

/** Atalhos de adversário na pista e classe escolhidas: quem mais venceu, os mais rápidos e os mais ativos. */
function Suggestions({ track, cls, exclude, onPick }: { track: string; cls: string; exclude: string; onPick: (id: string) => void }) {
  const s = useApi<CompareSuggestions>(`/lmu/compare/suggestions?track=${encodeURIComponent(track)}&class=${cls}`)
  if (!s.data) return s.loading ? <Loading rows={1} /> : null
  const cols: { h: string; list: CompareSuggestion[]; v: (x: CompareSuggestion) => string }[] = [
    { h: 'Mais vitórias aqui', list: s.data.mostWins, v: (x) => `${x.value} vitória${x.value === 1 ? '' : 's'}` },
    { h: 'Voltas mais rápidas aqui', list: s.data.fastest, v: (x) => fmtLap(x.value) },
    { h: 'Mais corridas aqui', list: s.data.mostRaces, v: (x) => `${x.value} corridas` },
  ]
  return (
    <div className="sugg">
      <p className="note">Ou um atalho em <b>{track} · {classShort(cls)}</b>:</p>
      <div className="sugg-grid">
        {cols.map((c) => (
          <div key={c.h} className="sugg-col">
            <span className="sugg-h">{c.h}</span>
            {c.list.filter((x) => x.driverId !== exclude).slice(0, 5).map((x, i) => (
              <button key={x.driverId} type="button" className="sugg-item" onClick={() => onPick(x.driverId)}>
                <span className="hl-rank">{i + 1}</span>
                <span className="sugg-name">{x.name}<Flag code={x.nationality} /></span>
                <span className="mono rel">{c.v(x)}</span>
              </button>
            ))}
            {!c.list.length && <span className="rel">ninguém ainda</span>}
          </div>
        ))}
      </div>
    </div>
  )
}

