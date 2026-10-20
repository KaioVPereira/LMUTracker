import { useMemo, useState, type PointerEvent } from 'react'
import type { SplitDriverLaps } from '../api/types'
import { compoundClass, fmtLap } from '../lib/format'
import { SERIES } from '../lib/series'
import { ZoomBox } from './ZoomBox'

// Gráficos da corrida, em SVG próprio. Até 6 pilotos destacados por vez (lib/series); os demais aparecem em cinza
// ao fundo, como contexto. Cada destacado tem legenda e rótulo direto no fim da linha — a cor nunca é a única pista.

const W = 760, PL = 44, PR = 112, PT = 12, PB = 26

/** Volta sob o cursor, de `minLap` (0 = largada, no gráfico de posições) até `maxLap`. */
function useHoverLap(maxLap: number, minLap = 1) {
  const [lap, setLap] = useState<number | null>(null)
  const onMove = (e: PointerEvent<SVGRectElement>) => {
    const svg = e.currentTarget.ownerSVGElement as SVGSVGElement
    const rect = svg.getBoundingClientRect()
    const px = ((e.clientX - rect.left) / rect.width) * W
    const l = Math.round(minLap + ((px - PL) / (W - PL - PR)) * (maxLap - minLap))
    setLap(Math.max(minLap, Math.min(maxLap, l)))
  }
  return { lap, onMove, clear: () => setLap(null) }
}

const shortName = (n: string) => (n.length > 14 ? `${n.slice(0, 13)}…` : n)

/**
 * Posição volta a volta, começando na largada (volta 0 = posição no grid) — assim quem ganha ou perde muitos lugares
 * na primeira volta aparece. `byClass` = posição dentro da classe (quando a lista está filtrada por classe).
 */
export function PositionChart({ drivers, selected, byClass }: { drivers: SplitDriverLaps[]; selected: string[]; byClass: boolean }) {
  const maxLap = Math.max(1, ...drivers.flatMap((d) => d.laps.map((l) => l.lap)))
  const maxPos = Math.max(1, drivers.length, ...drivers.map((d) => (byClass ? d.classGridPos : d.gridPos) ?? 0))
  const H = Math.min(420, 120 + maxPos * 9)
  const x = (lap: number) => PL + (lap / maxLap) * (W - PL - PR)
  const y = (p: number) => PT + ((p - 1) / Math.max(1, maxPos - 1)) * (H - PT - PB)
  const hover = useHoverLap(maxLap, 0)

  /** Pontos do piloto: o grid como volta 0 e depois a posição ao fim de cada volta. */
  const points = (d: SplitDriverLaps) => {
    const grid = byClass ? d.classGridPos : d.gridPos
    const laps = d.laps.map((l) => ({ lap: l.lap, p: byClass ? l.classPosition : l.position }))
    return [...(grid ? [{ lap: 0, p: grid }] : []), ...laps].filter((q): q is { lap: number; p: number } => q.p !== null)
  }
  const line = (d: SplitDriverLaps) => points(d).map((q, i) => `${i ? 'L' : 'M'}${x(q.lap).toFixed(1)} ${y(q.p).toFixed(1)}`).join(' ')

  const sel = selected.map((id) => drivers.find((d) => d.resultId === id)).filter(Boolean) as SplitDriverLaps[]
  const ticks = [1, ...[5, 10, 15, 20, 25, 30, 35, 40].filter((t) => t < maxPos), maxPos]
  const tip = hover.lap !== null ? sel
    .map((d) => {
      const pts = points(d)
      const at = pts.find((q) => q.lap === hover.lap)
      const grid = pts.find((q) => q.lap === 0)
      return { d, p: at?.p ?? null, gained: at && grid && hover.lap! > 0 ? grid.p - at.p : null }
    })
    .filter((t) => t.p !== null).sort((a, b) => a.p! - b.p!) : []

  return (
    <ZoomBox width={W}>
    <div className="chart-box is-wide">
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={`Posição ${byClass ? 'na classe' : 'geral'} volta a volta`}>
        {ticks.map((t) => (
          <g key={t}>
            <line x1={PL} x2={W - PR} y1={y(t)} y2={y(t)} className="grid" />
            <text x={PL - 8} y={y(t) + 4} textAnchor="end" className="axis">P{t}</text>
          </g>
        ))}
        {drivers.filter((d) => !selected.includes(d.resultId)).map((d) => (
          <path key={d.resultId} d={line(d)} className="ctx-line" />
        ))}
        {sel.map((d) => {
          const c = SERIES[selected.indexOf(d.resultId)]
          const pts = points(d)
          const last = pts[pts.length - 1]
          const start = pts[0]?.lap === 0 ? pts[0] : null
          return (
            <g key={d.resultId}>
              <path d={line(d)} fill="none" stroke={c} strokeWidth={2.5} strokeLinejoin="round" strokeLinecap="round" />
              {start && <circle cx={x(0)} cy={y(start.p)} r={3} fill={c} className="ring" />}
              {last && (
                <>
                  <circle cx={x(last.lap)} cy={y(last.p)} r={4} fill={c} className="ring" />
                  <text x={x(last.lap) + 8} y={y(last.p) + 4} className="series-label">{shortName(d.name)}</text>
                </>
              )}
            </g>
          )
        })}
        {hover.lap !== null && <line x1={x(hover.lap)} x2={x(hover.lap)} y1={PT} y2={H - PB} className="cross" />}
        <line x1={x(0)} x2={x(0)} y1={PT} y2={H - PB} className="grid" />
        <text x={PL} y={H - 6} className="axis">largada</text>
        {maxLap > 2 && x(1) - x(0) > 48 && <text x={x(1)} y={H - 6} textAnchor="middle" className="axis">1</text>}
        <text x={W - PR} y={H - 6} textAnchor="end" className="axis">volta {maxLap}</text>
        <rect x={PL} y={PT} width={W - PL - PR} height={H - PT - PB} fill="transparent" onPointerMove={hover.onMove} onPointerDown={hover.onMove} onPointerLeave={(e) => { if (e.pointerType === 'mouse') hover.clear() }} />
      </svg>
      {hover.lap !== null && tip.length > 0 && (
        <div className="chart-tip" style={{ left: `${(x(hover.lap) / W) * 100}%`, top: '8%', transform: `translate(${hover.lap === 0 ? '0' : '-50%'}, 0)` }}>
          <b>{hover.lap === 0 ? 'Largada (grid)' : `Volta ${hover.lap}`}</b>
          {tip.map((t) => (
            <span key={t.d.resultId}>
              <i className="sw" style={{ background: SERIES[selected.indexOf(t.d.resultId)] }} />P{t.p} · {t.d.name}
              {t.gained !== null && t.gained !== 0 && <em className={t.gained > 0 ? 'up' : 'down'}> {t.gained > 0 ? `+${t.gained}` : t.gained} desde a largada</em>}
            </span>
          ))}
        </div>
      )}
    </div>
    </ZoomBox>
  )
}

/** Tempo por volta dos destacados (sem 1ª volta, sem pit e sem voltas acima de 107% da melhor do piloto). */
export function PaceChart({ drivers, selected }: { drivers: SplitDriverLaps[]; selected: string[] }) {
  const sel = selected.map((id) => drivers.find((d) => d.resultId === id)).filter(Boolean) as SplitDriverLaps[]
  const clean = useMemo(() => sel.map((d) => {
    const times = d.laps.filter((l) => l.lap > 1 && !l.pit && l.lapTime).map((l) => l.lapTime!)
    const best = times.length ? Math.min(...times) : 0
    return { d, laps: d.laps.filter((l) => l.lap > 1 && !l.pit && l.lapTime && l.lapTime <= best * 1.07) }
  }), [sel])
  const all = clean.flatMap((c) => c.laps.map((l) => l.lapTime!))
  const maxLap = Math.max(2, ...drivers.flatMap((d) => d.laps.map((l) => l.lap)))
  const hover = useHoverLap(maxLap)
  if (!all.length) return <p className="note">Escolha pilotos com volta a volta para comparar o ritmo.</p>

  const H = 300
  const min = Math.min(...all), max = Math.max(...all)
  const pad = Math.max(0.15, (max - min) * 0.08)
  const lo = min - pad, hi = max + pad
  const x = (lap: number) => PL + 14 + ((lap - 1) / Math.max(1, maxLap - 1)) * (W - PL - PR - 14)
  const y = (t: number) => PT + (1 - (t - lo) / (hi - lo)) * (H - PT - PB)
  const ticks = [lo + pad, (lo + hi) / 2, hi - pad]
  const tip = hover.lap !== null ? clean
    .map((c) => ({ c, l: c.laps.find((l) => l.lap === hover.lap) }))
    .filter((t) => t.l).sort((a, b) => a.l!.lapTime! - b.l!.lapTime!) : []

  return (
    <ZoomBox width={W}>
    <div className="chart-box is-wide">
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label="Tempo por volta dos pilotos destacados">
        {ticks.map((t) => (
          <g key={t}>
            <line x1={PL + 14} x2={W - PR} y1={y(t)} y2={y(t)} className="grid" />
            <text x={PL + 6} y={y(t) + 4} textAnchor="end" className="axis">{fmtLap(t)}</text>
          </g>
        ))}
        {clean.map(({ d, laps }) => {
          const c = SERIES[selected.indexOf(d.resultId)]
          // Quebra a linha onde faltam voltas (pit, volta lenta): só liga voltas consecutivas.
          const path = laps.map((l, i) => `${i && laps[i - 1].lap === l.lap - 1 ? 'L' : 'M'}${x(l.lap).toFixed(1)} ${y(l.lapTime!).toFixed(1)}`).join(' ')
          const last = laps[laps.length - 1]
          return (
            <g key={d.resultId}>
              <path d={path} fill="none" stroke={c} strokeWidth={2} strokeLinejoin="round" />
              {laps.map((l) => <circle key={l.lap} cx={x(l.lap)} cy={y(l.lapTime!)} r={hover.lap === l.lap ? 4.5 : 2.5} fill={c} />)}
              {last && <text x={x(last.lap) + 8} y={y(last.lapTime!) + 4} className="series-label">{shortName(d.name)}</text>}
            </g>
          )
        })}
        {hover.lap !== null && <line x1={x(hover.lap)} x2={x(hover.lap)} y1={PT} y2={H - PB} className="cross" />}
        <text x={PL + 14} y={H - 6} className="axis">volta 1</text>
        <text x={W - PR} y={H - 6} textAnchor="end" className="axis">volta {maxLap}</text>
        <rect x={PL} y={PT} width={W - PL - PR} height={H - PT - PB} fill="transparent" onPointerMove={hover.onMove} onPointerDown={hover.onMove} onPointerLeave={(e) => { if (e.pointerType === 'mouse') hover.clear() }} />
      </svg>
      {hover.lap !== null && tip.length > 0 && (
        <div className="chart-tip" style={{ left: `${(x(hover.lap) / W) * 100}%`, top: '8%', transform: 'translate(-50%, 0)' }}>
          <b>Volta {hover.lap}</b>
          {tip.map((t) => (
            <span key={t.c.d.resultId}><i className="sw" style={{ background: SERIES[selected.indexOf(t.c.d.resultId)] }} />{fmtLap(t.l!.lapTime)} · {t.c.d.name}</span>
          ))}
        </div>
      )}
    </div>
    </ZoomBox>
  )
}

/** Estratégia: uma linha por piloto, a corrida em stints coloridos pelo composto, com as paradas marcadas. */
export function StrategyChart({ drivers, highlight }: { drivers: SplitDriverLaps[]; highlight?: string }) {
  const maxLap = Math.max(1, ...drivers.flatMap((d) => d.laps.map((l) => l.lap)))
  const compounds = new Set<string>()
  return (
    <ZoomBox width={720}>
    <div className="strategy">
      {drivers.map((d) => {
        // Stints: sequências de voltas com o mesmo composto, cortadas nos pits.
        const stints: { from: number; to: number; compound: string | null }[] = []
        for (const l of d.laps) {
          const cur = stints[stints.length - 1]
          if (!cur || l.pit || cur.compound !== l.compound) stints.push({ from: l.lap, to: l.lap, compound: l.compound })
          else cur.to = l.lap
          if (l.compound) compounds.add(l.compound)
        }
        const pits = d.laps.filter((l) => l.pit).map((l) => l.lap)
        return (
          <div key={d.resultId} className={`strat-row${d.resultId === highlight ? ' is-me' : ''}`}>
            <span className="strat-name"><b>{d.classPosition ? `P${d.classPosition}` : '—'}</b> {d.name}</span>
            <span className="strat-bar" aria-label={`${d.name}: ${pits.length} parada${pits.length === 1 ? '' : 's'}${pits.length ? ` (voltas ${pits.join(', ')})` : ''}`}>
              {stints.map((s, i) => (
                <i key={i} className={`stint ${compoundClass(s.compound)}`} title={`${s.compound ?? 'pneu ?'} · voltas ${s.from}–${s.to}`}
                  style={{ left: `${((s.from - 1) / maxLap) * 100}%`, width: `${((s.to - s.from + 1) / maxLap) * 100}%` }} />
              ))}
              {pits.map((p) => <b key={p} className="strat-pit" style={{ left: `${((p - 0.5) / maxLap) * 100}%` }} />)}
            </span>
            <span className="strat-n mono">{pits.length}</span>
          </div>
        )
      })}
      <div className="strat-legend" aria-hidden="true">
        {[...compounds].map((c) => <span key={c}><i className={`stint ${compoundClass(c)}`} />{c}</span>)}
        <span><b className="strat-pit legend" /> parada</span>
      </div>
    </div>
    </ZoomBox>
  )
}
