import type { CrewStint, Lap } from '../api/types'
import { fmtLap } from '../lib/format'
import { ZoomBox } from './ZoomBox'

/**
 * Tempo por volta. Voltas de pit e a primeira (largada) ficam fora da escala — senão um pit de +25s achata o
 * resto do gráfico — e aparecem como marcas na base. A melhor volta é destacada.
 */
export function LapChart({ laps, crew }: { laps: Lap[]; crew?: CrewStint[] | null }) {
  const pace = laps.filter((l) => l.lapTime && !l.pit && l.lap > 1)
  if (pace.length < 2) return null

  const W = 720, H = 200, PL = 66, PR = 12, PT = 14, PB = 26
  const times = pace.map((l) => l.lapTime!)
  const best = Math.min(...times)
  let min = best
  let max = Math.max(...times)
  const pad = Math.max(0.2, (max - min) * 0.12)
  min -= pad
  max += pad
  const first = laps[0].lap
  const last = laps[laps.length - 1].lap
  const x = (lap: number) => PL + ((lap - first) / Math.max(1, last - first)) * (W - PL - PR)
  const y = (t: number) => PT + (1 - (t - min) / (max - min)) * (H - PT - PB)

  const path = pace.map((l, i) => `${i ? 'L' : 'M'}${x(l.lap).toFixed(1)} ${y(l.lapTime!).toFixed(1)}`).join(' ')
  const ticks = [max - pad, (min + max) / 2, min + pad]
  const bestLap = pace.find((l) => l.lapTime === best)!

  return (
    <figure className="chart">
      <ZoomBox width={W}>
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label="Tempo por volta">
        {ticks.map((t) => (
          <g key={t}>
            <line x1={PL} x2={W - PR} y1={y(t)} y2={y(t)} className="grid" />
            <text x={PL - 6} y={y(t) + 4} textAnchor="end" className="axis">{fmtLap(t)}</text>
          </g>
        ))}
        <text x={PL} y={H - 6} className="axis">volta {first}</text>
        <text x={W - PR} y={H - 6} textAnchor="end" className="axis">volta {last}</text>
        <path d={path} className="line" />
        {pace.map((l) => (
          <circle key={l.lap} cx={x(l.lap)} cy={y(l.lapTime!)} r={l === bestLap ? 4.5 : 2.2}
            className={l === bestLap ? 'dot best-dot' : 'dot'} />
        ))}
        {laps.filter((l) => l.pit).map((l) => {
          const flip = x(l.lap) > W - PR - 70 // perto da borda direita o rótulo passa para a esquerda da linha
          return (
            <g key={`pit-${l.lap}`}>
              <line x1={x(l.lap)} x2={x(l.lap)} y1={PT} y2={H - PB} className="pitline" />
              <text x={x(l.lap) + (flip ? -3 : 3)} y={PT + 10} textAnchor={flip ? 'end' : 'start'} className="axis pit-label">
                PIT · volta {l.lap}
              </text>
            </g>
          )
        })}
        {/* Carro dividido: troca de piloto no começo de cada turno (embaixo, para não brigar com o rótulo do pit). */}
        {(crew ?? []).filter((s) => s.startLap > first).map((s) => (
          <g key={`swap-${s.startLap}`}>
            <line x1={x(s.startLap - 0.5)} x2={x(s.startLap - 0.5)} y1={PT} y2={H - PB} className="swapline" />
            <text x={x(s.startLap - 0.5) + 3} y={H - PB - 4} className="axis swap-label">{s.name}</text>
          </g>
        ))}
      </svg>
      </ZoomBox>
      <figcaption className="note">
        Melhor volta: <b className="mono">{fmtLap(best)}</b> (volta {bestLap.lap}). Pit e 1ª volta fora da escala.
      </figcaption>
    </figure>
  )
}
