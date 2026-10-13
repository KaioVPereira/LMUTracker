import { useCallback, useEffect, useRef, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import type { ActiveDriver, ClassShare, CloseFinish, LapRecord, RankClimber, TopSplitWinner } from '../api/types'
import { classKey, classShort } from '../lib/classes'
import { fmtInt, fmtLap, fmtRelative, rankClass } from '../lib/format'
import { ClassChip, EmptyState, Skeleton } from './Bits'

/** Nome do piloto com link para o perfil (quando o conhecemos pelo id interno). */
function DriverLink({ id, name }: { id: string | null; name: string }) {
  return id ? <Link to={`/pilotos/${id}`} className="plain">{name}</Link> : <>{name}</>
}

const reduceMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches

/**
 * Faixa horizontal com rolagem por arrasto/deslize e, onde há mouse, botões de anterior/próximo. Cada card encaixa
 * no início ao rolar (scroll-snap). Com `autoPlayMs`, avança um card sozinho e volta ao começo no fim — pausa com o
 * mouse em cima, com foco dentro, depois de um toque, com a aba escondida e com "reduzir movimento" do sistema.
 */
export function Carousel({ id, title, sub, children, loading, autoPlayMs, narrow }: {
  id: string
  title: string
  sub?: string
  children: ReactNode
  loading?: boolean
  autoPlayMs?: number
  narrow?: boolean
}) {
  const track = useRef<HTMLDivElement>(null)
  const paused = useRef(false)
  const touchTimer = useRef(0)

  const go = useCallback((dir: 1 | -1) => {
    const el = track.current
    if (!el) return
    const card = el.firstElementChild as HTMLElement | null
    const stepPx = card ? card.offsetWidth + 12 : el.clientWidth * 0.85
    const atEnd = el.scrollLeft + el.clientWidth >= el.scrollWidth - 4
    const atStart = el.scrollLeft <= 4
    if (dir === 1 && atEnd) el.scrollTo({ left: 0, behavior: 'smooth' })
    else if (dir === -1 && atStart) el.scrollTo({ left: el.scrollWidth, behavior: 'smooth' })
    else el.scrollBy({ left: dir * stepPx, behavior: 'smooth' })
  }, [])

  useEffect(() => {
    if (!autoPlayMs || loading || reduceMotion()) return
    const id = window.setInterval(() => {
      const el = track.current
      if (!el || paused.current || document.hidden || el.scrollWidth <= el.clientWidth + 4) return
      go(1)
    }, autoPlayMs)
    return () => window.clearInterval(id)
  }, [autoPlayMs, loading, go])

  useEffect(() => () => window.clearTimeout(touchTimer.current), [])

  const pause = () => { paused.current = true }
  const resume = () => { paused.current = false }
  const pauseAfterTouch = () => {
    paused.current = true
    window.clearTimeout(touchTimer.current)
    touchTimer.current = window.setTimeout(resume, 8000)
  }

  return (
    <section aria-labelledby={id} className="carousel" onPointerEnter={(e) => { if (e.pointerType === 'mouse') pause() }}
      onPointerLeave={(e) => { if (e.pointerType === 'mouse') resume() }} onFocus={pause} onBlur={resume} onTouchStart={pauseAfterTouch}>
      <div className="sec-h">
        <h2 id={id}>{title}</h2>
        {sub && <span className="rel">{sub}</span>}
        <span className="car-nav">
          <button type="button" className="car-btn" onClick={() => go(-1)} aria-label={`${title}: anteriores`}>←</button>
          <button type="button" className="car-btn" onClick={() => go(1)} aria-label={`${title}: próximos`}>→</button>
        </span>
      </div>
      <div className={`car-track${narrow ? ' is-narrow' : ''}`} ref={track} tabIndex={0} aria-label={title}>
        {loading
          ? [0, 1, 2, 3].map((i) => (
            <div key={i} className="car-card sk-stack" aria-hidden="true">
              <Skeleton w="50%" h={18} /><Skeleton w="70%" h={32} r={10} /><Skeleton w="60%" /><Skeleton w="40%" />
            </div>
          ))
          : children}
      </div>
    </section>
  )
}

export function RecordCard({ r, now }: { r: LapRecord; now: number }) {
  return (
    <Link to={`/corridas/${r.eventId}?split=${r.splitNo}`} className={`car-card lift rec-card cls-${classKey(r.carClass)}`}>
      <span className="rec-top">
        <span className="rec-track">{r.track}</span>
        <ClassChip cls={r.carClass} />
      </span>
      <span className="rec-time">{fmtLap(r.lapTime)}</span>
      <span className="rec-meta">
        <span className={`setup ${r.fixedSetup ? 'is-fixed' : ''}`}>{r.fixedSetup ? 'Setup fixo' : 'Setup livre'}</span>
        <span>{r.car ?? '—'}</span>
      </span>
      <span className="rec-driver">
        <b>{r.driverName}</b>
        {r.nationality && <span className="pill">{r.nationality}</span>}
        <span className="rel" style={{ marginLeft: 'auto' }}>{fmtRelative(r.setAt, now)}</span>
      </span>
    </Link>
  )
}

export function WinnerCard({ w, now }: { w: TopSplitWinner; now: number }) {
  return (
    <Link to={`/corridas/${w.eventId}?split=1`} className="car-card lift win-card">
      <span className="win-top">
        <span className="p1" aria-label="Primeiro lugar">P1</span>
        <span className="win-who">
          <b>{w.driverName}</b>
          <span>{w.car ?? '—'}</span>
        </span>
        <ClassChip cls={w.carClass} />
      </span>
      <span className="win-race">{w.title ?? 'Corrida'} · {w.track ?? '—'}</span>
      <span className="win-nums">
        <span><b>{w.laps}</b> voltas</span>
        <span><b>{w.splitDrivers}</b> no split</span>
        {w.gapToSecond !== null && <span><b>+{w.gapToSecond.toFixed(3)}</b> s p/ P2</span>}
        <span style={{ marginLeft: 'auto' }}>{fmtRelative(w.startsAt, now)}</span>
      </span>
    </Link>
  )
}

const rankText = (title: string | null, tier: number | null, progress: number | null) =>
  title ? `${title}${tier !== null ? ` ${tier}` : ''}${progress !== null ? ` · ${progress}%` : ''}` : '—'

/** Blocos de destaques: saldo de DR, chegadas apertadas, mais ativos e largadas por classe. */
export function Highlights({ climbers, close, active, share }: {
  climbers: RankClimber[]
  close: CloseFinish[]
  active: ActiveDriver[]
  share: ClassShare[]
}) {
  const totalStarts = share.reduce((a, s) => a + s.starts, 0)
  return (
    <section aria-labelledby="sec-dest">
      <div className="sec-h"><h2 id="sec-dest">Destaques</h2><span className="rel">semana e últimas 24 h</span></div>
      <div className="hl-grid">
        <article className="hl">
          <h3>Maior saldo de DR <small>7 dias</small></h3>
          {climbers.length ? (
            <ol className="hl-list">
              {climbers.slice(0, 4).map((c, i) => (
                <li key={(c.driverId ?? c.name) + i}>
                  <span className="hl-rank">{i + 1}</span>
                  <span className="hl-main">
                    <b><DriverLink id={c.driverId} name={c.name} /></b>
                    <span>
                      <span className={rankClass(c.fromTitle)}>{rankText(c.fromTitle, c.fromTier, c.fromProgress)}</span>
                      {' → '}
                      <span className={rankClass(c.toTitle)}>{rankText(c.toTitle, c.toTier, c.toProgress)}</span>
                    </span>
                  </span>
                  <span className="hl-val up">+{c.driverRankGain}<small>{c.races} corr.</small></span>
                </li>
              ))}
            </ol>
          ) : <p className="note">Ainda sem pilotos com 3 corridas na semana.</p>}
          <p className="hl-foot">Soma das setas de DR de cada corrida (o RaceOS não mostra os pontos de terceiros).</p>
        </article>

        <article className="hl">
          <h3>Chegadas mais apertadas <small>24 h · split 1</small></h3>
          {close.length ? (
            <ol className="hl-list">
              {close.map((c) => (
                <li key={c.eventId + c.carClass}>
                  <span className={`hl-dot cls-${classKey(c.carClass)}`} aria-hidden="true" />
                  <span className="hl-main">
                    <Link to={`/corridas/${c.eventId}?split=1`} className="plain"><b>{c.title ?? 'Corrida'}</b></Link>
                    <span>{c.winnerName} × {c.secondName} · {classShort(c.carClass)}</span>
                  </span>
                  <span className="hl-val mono">{c.gap.toFixed(3)}s</span>
                </li>
              ))}
            </ol>
          ) : <p className="note">Nenhuma corrida terminou nas últimas 24 h.</p>}
        </article>

        <article className="hl">
          <h3>Mais ativos <small>24 h</small></h3>
          {active.length ? (
            <ol className="hl-list">
              {active.map((a, i) => (
                <li key={(a.driverId ?? a.name) + i}>
                  <span className="hl-rank">{i + 1}</span>
                  <span className="hl-main">
                    <b><DriverLink id={a.driverId} name={a.name} /></b>
                    {a.nationality && <span>{a.nationality}</span>}
                  </span>
                  <span className="hl-val">{a.races}<small>corridas</small></span>
                </li>
              ))}
            </ol>
          ) : <p className="note">Ninguém correu nas últimas 24 h.</p>}
        </article>

        <article className="hl">
          <h3>Largadas por classe <small>24 h</small></h3>
          {totalStarts ? (
            <>
              <div className="share-bar" role="img"
                aria-label={share.map((s) => `${classShort(s.carClass)} ${Math.round((s.starts / totalStarts) * 100)}%`).join(', ')}>
                {share.map((s) => (
                  <span key={s.carClass} className={`cls-${classKey(s.carClass)}`} style={{ width: `${(s.starts / totalStarts) * 100}%` }} />
                ))}
              </div>
              <ul className="share-legend">
                {share.map((s) => (
                  <li key={s.carClass}>
                    <span className={`cls-dot cls-${classKey(s.carClass)}`} aria-hidden="true" />
                    <b>{classShort(s.carClass)}</b>
                    <span className="mono">{Math.round((s.starts / totalStarts) * 100)}%</span>
                    <span className="rel">{fmtInt(s.starts)}</span>
                  </li>
                ))}
              </ul>
            </>
          ) : <p className="note">Sem largadas nas últimas 24 h.</p>}
        </article>
      </div>
    </section>
  )
}

export function HomeEmpty({ what }: { what: string }) {
  return <EmptyState title={`Ainda sem ${what}`}>Assim que as primeiras corridas da semana terminarem, isto se preenche sozinho.</EmptyState>
}
