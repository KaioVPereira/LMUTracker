import { useMemo, useRef, useState, type MouseEvent } from 'react'
import type { RankPoint } from '../api/types'
import { fmtDateTime, rankClass } from '../lib/format'

// Gráficos do perfil, em SVG/HTML próprios (sem biblioteca). Regras: uma série por gráfico (sem legenda — o título
// nomeia), linha de 2px, marcadores >= 8px, grade recessiva, tooltip ao passar o mouse/tocar, cor da série = acento.

const TITLES = ['Bronze', 'Silver', 'Gold', 'Platinum', 'Diamond']

/** 230 -> "Bronze 3 · 30%". */
function scoreLabel(score: number): string {
  const i = Math.min(TITLES.length - 1, Math.floor(score / 300))
  const tier = Math.min(3, Math.floor((score - i * 300) / 100) + 1)
  return `${TITLES[i]} ${tier}`
}

type Metric = 'dr' | 'sr'

/**
 * Evolução do Driver Rank ou do Safety Rank, corrida a corrida. O eixo é a escala contínua do servidor
 * (título × 300 + tier × 100 + progresso); as linhas da grade marcam o início de cada tier.
 */
export function RankChart({ points }: { points: RankPoint[] }) {
  const [metric, setMetric] = useState<Metric>('dr')
  const [hover, setHover] = useState<number | null>(null)
  const box = useRef<HTMLDivElement>(null)

  const series = useMemo(() => points
    .map((p, i) => ({
      i, p,
      score: metric === 'dr' ? p.driverScore : p.safetyScore,
      adj: metric === 'dr' ? p.driverRankAdjustment : p.safetyRankAdjustment,
      title: metric === 'dr' ? p.driverRankTitle : p.safetyRankTitle,
      tier: metric === 'dr' ? p.driverRankTier : p.safetyRankTier,
      progress: metric === 'dr' ? p.driverRankProgress : p.safetyRankProgress,
    }))
    .filter((x) => x.score !== null) as { i: number; p: RankPoint; score: number; adj: number | null; title: string | null; tier: number | null; progress: number | null }[],
  [points, metric])

  const W = 720, H = 240, PL = 78, PR = 16, PT = 14, PB = 28
  const tabs = (
    <div className="chips" role="tablist" aria-label="Métrica">
      <button type="button" role="tab" className="chip" aria-selected={metric === 'dr'} onClick={() => setMetric('dr')}>Driver Rank</button>
      <button type="button" role="tab" className="chip" aria-selected={metric === 'sr'} onClick={() => setMetric('sr')}>Safety Rank</button>
    </div>
  )
  if (series.length < 2) {
    return <div className="chart-card">{tabs}<p className="note">Com 2 corridas ou mais a evolução aparece aqui.</p></div>
  }

  const min = Math.floor(Math.min(...series.map((s) => s.score)) / 100) * 100
  const max = Math.max(min + 100, Math.ceil((Math.max(...series.map((s) => s.score)) + 1) / 100) * 100)
  const x = (k: number) => PL + (k / Math.max(1, series.length - 1)) * (W - PL - PR)
  const y = (v: number) => PT + (1 - (v - min) / (max - min)) * (H - PT - PB)
  const ticks: number[] = []
  for (let v = min; v <= max; v += 100) ticks.push(v)
  const path = series.map((s, k) => `${k ? 'L' : 'M'}${x(k).toFixed(1)} ${y(s.score).toFixed(1)}`).join(' ')
  const area = `${path} L${x(series.length - 1).toFixed(1)} ${H - PB} L${x(0).toFixed(1)} ${H - PB} Z`

  function onMove(e: MouseEvent<SVGRectElement>) {
    const rect = (e.currentTarget.ownerSVGElement as SVGSVGElement).getBoundingClientRect()
    const px = ((e.clientX - rect.left) / rect.width) * W
    const k = Math.round(((px - PL) / (W - PL - PR)) * (series.length - 1))
    setHover(Math.max(0, Math.min(series.length - 1, k)))
  }

  const h = hover !== null ? series[hover] : null
  const first = series[0], last = series[series.length - 1]
  const delta = last.score - first.score

  return (
    <div className="chart-card">
      <div className="chart-head">
        {tabs}
        <span className="rel">
          {series.length} corridas · {delta >= 0 ? 'subiu' : 'caiu'} <b className={delta >= 0 ? 'up' : 'down'}>{Math.abs(delta)} pts</b> desde {fmtDateTime(first.p.at)}
        </span>
      </div>
      <div className="chart-box" ref={box}>
        <svg viewBox={`0 0 ${W} ${H}`} role="img"
          aria-label={`${metric === 'dr' ? 'Driver Rank' : 'Safety Rank'}: de ${scoreLabel(first.score)} para ${scoreLabel(last.score)} em ${series.length} corridas`}>
          {ticks.map((t) => (
            <g key={t}>
              <line x1={PL} x2={W - PR} y1={y(t)} y2={y(t)} className={`grid${t % 300 === 0 ? ' grid-strong' : ''}`} />
              {t < max && <text x={PL - 8} y={y(t + 50) + 4} textAnchor="end" className={`axis ${rankClass(scoreLabel(t).split(' ')[0])}`}>{scoreLabel(t)}</text>}
            </g>
          ))}
          <path d={area} className="area" />
          <path d={path} className="line" />
          {series.map((s, k) => (
            <circle key={s.i} cx={x(k)} cy={y(s.score)} r={k === hover ? 6 : 4}
              className={`dot ${s.adj !== null && s.adj > 0 ? 'is-up' : s.adj !== null && s.adj < 0 ? 'is-down' : ''}`} />
          ))}
          {h && <line x1={x(hover!)} x2={x(hover!)} y1={PT} y2={H - PB} className="cross" />}
          <text x={PL} y={H - 8} className="axis">{fmtDateTime(first.p.at)}</text>
          <text x={W - PR} y={H - 8} textAnchor="end" className="axis">{fmtDateTime(last.p.at)}</text>
          <rect x={PL} y={PT} width={W - PL - PR} height={H - PT - PB} fill="transparent"
            onMouseMove={onMove} onMouseLeave={() => setHover(null)} onTouchStart={() => setHover(series.length - 1)} />
        </svg>
        {h && (
          <div className="chart-tip" style={{ left: `${(x(hover!) / W) * 100}%`, top: `${(y(h.score) / H) * 100}%` }} role="status">
            <b className={rankClass(h.title)}>{h.title} {h.tier} · {h.progress ?? 0}%</b>
            <span>{h.p.title ?? 'Corrida'} · {h.p.track ?? '—'}</span>
            <span>{fmtDateTime(h.p.at)}{h.adj !== null && <> · seta <b className={h.adj > 0 ? 'up' : 'down'}>{h.adj > 0 ? `+${h.adj}` : h.adj}</b></>}</span>
          </div>
        )}
      </div>
      <p className="note">Cada ponto é o rank com que ele largou naquela corrida (progresso arredondado de 10 em 10, como o RaceOS mostra).</p>
    </div>
  )
}

/**
 * Heatmap de atividade estilo GitHub: uma coluna por semana (seg → dom), cor pela quantidade de corridas no dia.
 * Uma cor só, do apagado ao aceso (no escuro, mais atividade = mais claro).
 */
export function ActivityHeatmap({ dates, weeks = 26 }: { dates: string[]; weeks?: number }) {
  const [hover, setHover] = useState<{ day: string; n: number; left: number; top: number } | null>(null)
  const [now] = useState(() => Date.now()) // "hoje" fixado na montagem

  const { cols, max, total, months } = useMemo(() => {
    const counts = new Map<string, number>()
    const key = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
    for (const iso of dates) {
      const k = key(new Date(iso))
      counts.set(k, (counts.get(k) ?? 0) + 1)
    }
    const today = new Date(now)
    today.setHours(0, 0, 0, 0)
    const dow = (today.getDay() + 6) % 7 // segunda = 0
    const start = new Date(today)
    start.setDate(today.getDate() - dow - (weeks - 1) * 7)
    const cols: { day: string; n: number; future: boolean; date: Date }[][] = []
    const months: { col: number; label: string }[] = []
    let lastMonth = -1
    for (let w = 0; w < weeks; w++) {
      const col = []
      for (let d = 0; d < 7; d++) {
        const date = new Date(start)
        date.setDate(start.getDate() + w * 7 + d)
        const k = key(date)
        col.push({ day: k, n: counts.get(k) ?? 0, future: date > today, date })
        if (d === 0 && date.getMonth() !== lastMonth) {
          months.push({ col: w, label: date.toLocaleDateString('pt-BR', { month: 'short' }).replace('.', '') })
          lastMonth = date.getMonth()
        }
      }
      cols.push(col)
    }
    return { cols, max: Math.max(1, ...counts.values()), total: dates.length, months }
  }, [dates, weeks, now])

  const level = (n: number) => (n === 0 ? 0 : n >= max * 0.75 ? 4 : n >= max * 0.5 ? 3 : n >= max * 0.25 ? 2 : 1)
  const label = (c: { date: Date; n: number }) =>
    `${c.n} corrida${c.n === 1 ? '' : 's'} em ${c.date.toLocaleDateString('pt-BR', { weekday: 'short', day: '2-digit', month: '2-digit' })}`

  return (
    <div className="chart-card">
      <div className="chart-head"><span className="rel">{total} corrida{total === 1 ? '' : 's'} nas últimas {weeks} semanas</span></div>
      <div className="heat-scroll">
        <div className="heat" style={{ gridTemplateColumns: `22px repeat(${cols.length}, 13px)` }} role="img"
          aria-label={`Atividade: ${total} corridas nas últimas ${weeks} semanas`} onMouseLeave={() => setHover(null)}>
          <span />
          {cols.map((_, w) => <span key={w} className="heat-month">{months.find((m) => m.col === w)?.label ?? ''}</span>)}
          {[0, 1, 2, 3, 4, 5, 6].map((d) => (
            <Row key={d} d={d} cols={cols} level={level} label={label} onHover={setHover} />
          ))}
          {hover && <div className="chart-tip heat-tip" style={{ left: hover.left, top: hover.top }}>{hover.day}</div>}
        </div>
      </div>
      <div className="heat-legend" aria-hidden="true">
        menos {[0, 1, 2, 3, 4].map((l) => <i key={l} className={`hc l${l}`} />)} mais
      </div>
    </div>
  )
}

function Row({ d, cols, level, label, onHover }: {
  d: number
  cols: { day: string; n: number; future: boolean; date: Date }[][]
  level: (n: number) => number
  label: (c: { date: Date; n: number }) => string
  onHover: (h: { day: string; n: number; left: number; top: number } | null) => void
}) {
  const names = ['seg', '', 'qua', '', 'sex', '', '']
  return (
    <>
      <span className="heat-dow">{names[d]}</span>
      {cols.map((col, w) => {
        const c = col[d]
        if (c.future) return <i key={w} className="hc is-future" aria-hidden="true" />
        return (
          <i key={w} className={`hc l${level(c.n)}`} title={label(c)}
            onMouseEnter={(e) => {
              const el = e.currentTarget
              onHover({ day: label(c), n: c.n, left: el.offsetLeft + 6, top: el.offsetTop })
            }} />
        )
      })}
    </>
  )
}
