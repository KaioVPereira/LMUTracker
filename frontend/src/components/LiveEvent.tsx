import { Link } from 'react-router-dom'
import type { LiveSpot, LiveStatus, UpcomingRace } from '../api/types'
import { classKey, classShort } from '../lib/classes'
import { type Phase, currentPhase, fmtGameTime, phasesOf, raceSession, skyLabel } from '../lib/eventPhase'
import { fmtClock, fmtInt, fmtRelative, fmtTime, parseUtc, typeLabel } from '../lib/format'
import { conditions, liveHref, useNow } from '../lib/live'
import { TierChip } from './Bits'

// Corrida ao vivo / próxima largada: o destaque da home e o topo da página do evento. "Ao vivo" aqui é o que o RaceOS
// diz do evento (fase pelos horários, inscritos, splits) — a cronometragem de terceiros ele não publica.

/** Linha do tempo das sessões, com a atual destacada e o progresso dela. */
export function Phases({ race, now }: { race: UpcomingRace; now: number }) {
  const phases = phasesOf(race)
  if (!phases.length) return null
  const cur = currentPhase(phases, now)
  const total = phases[phases.length - 1].end - phases[0].start
  return (
    <ol className="phases" aria-label="Sessões do evento">
      {phases.map((p, i) => {
        const done = i < cur
        const on = i === cur
        const pct = on ? Math.min(100, ((now - p.start) / (p.end - p.start)) * 100) : done ? 100 : 0
        return (
          <li key={p.kind + i} className={`ph${on ? ' is-on' : ''}${done ? ' is-done' : ''}`}
            style={{ flexGrow: Math.max(1, (p.end - p.start) / total * 10) }} aria-current={on ? 'step' : undefined}>
            <i><b style={{ width: `${pct}%` }} /></i>
            <span>{p.label}<small>{Math.round(p.minutes)} min</small></span>
          </li>
        )
      })}
    </ol>
  )
}

/**
 * Corrida em destaque. Ao vivo: a fase atual e quanto falta para a próxima; antes: contagem até a largada.
 * Com `to`, um botão leva à página do evento (splits e pilotos); posições ao vivo o RaceOS não publica.
 */
export function Hero({ race, live, liveCount, to }: { race: UpcomingRace; live: boolean; liveCount?: number; to?: string }) {
  const now = useNow(1000)
  const phases = phasesOf(race)
  const cur = currentPhase(phases, now)
  const phase = cur >= 0 && cur < phases.length ? phases[cur] : null
  const nextPhase = phases[cur + 1]
  const s = raceSession(race.info)
  const hour = live && phase ? raceHour(phase, now) : null

  let clock: { value: number; label: string }
  if (!live) clock = { value: parseUtc(race.startsAt).getTime() - now, label: 'para a largada' }
  else if (phase && phase.kind !== 'race' && nextPhase) clock = { value: nextPhase.start - now, label: `para ${nextPhase.kind === 'race' ? 'a largada da corrida' : nextPhase.label.toLowerCase()}` }
  else clock = { value: parseUtc(race.endsAt).getTime() - now, label: 'para a bandeira' }

  return (
    <article className={`hero${live ? '' : ' is-next'}`} aria-labelledby="hero-title">
      <div className="hero-main">
        {live
          ? <span className="live">Ao vivo{phase ? ` · ${phase.label}` : ''}{hour ? ` · hora ${hour}` : ''}{liveCount && liveCount > 1 ? <span className="live-extra"> · {liveCount} corridas</span> : null}</span>
          : <span className="next-tag">Próxima largada</span>}
        <h1 id="hero-title" className="hero-title">{race.title ?? 'Corrida diária'}</h1>
        <p className="hero-sub">{race.track ?? '—'} · {typeLabel(race.eventType)} · começa {fmtTime(race.startsAt)}</p>
        <div className="hero-clock" aria-live="off"><b>{fmtClock(clock.value)}</b>{clock.label}</div>
        <Phases race={race} now={now} />
        {to && (
          <div className="hero-actions">
            <Link to={to} className="btn btn-primary">{live ? 'Acompanhar o evento' : 'Ver inscritos e detalhes'} →</Link>
          </div>
        )}
      </div>
      <div className="hero-side">
      <dl className="hero-facts">
        <div><dt>Nível</dt><dd><TierChip tier={race.tier} /></dd></div>
        {s && <div><dt>Hora no jogo</dt><dd className="mono">{fmtGameTime(s.timeOfDay)}</dd></div>}
        {s?.temperatureC !== null && s?.temperatureC !== undefined && <div><dt>Temperatura</dt><dd>{s.temperatureC} °C</dd></div>}
        {s && <div><dt>Céu</dt><dd>{skyLabel(s.sky) ?? '—'}{s.rainChance ? ` · chuva ${s.rainChance}%` : ''}</dd></div>}
        {race.info?.fixedSetup !== null && race.info?.fixedSetup !== undefined && <div><dt>Setup</dt><dd>{race.info.fixedSetup ? 'Fixo' : 'Livre'}</dd></div>}
        {race.info?.maxPlayers && <div><dt>Grid por split</dt><dd className="mono">até {race.info.maxPlayers}</dd></div>}
        <div className="fact-end"><dt>Termina</dt><dd className="mono">~{fmtTime(race.endsAt)}</dd></div>
      </dl>
      </div>
      {race.live && <HeroLive l={race.live} live={live} maxPlayers={race.info?.maxPlayers ?? null} now={now} />}
    </article>
  )
}

/** Card de uma corrida acontecendo agora; leva à página do evento (splits e pilotos). */
export function LiveCard({ race, now }: { race: UpcomingRace; now: number }) {
  const phases = phasesOf(race)
  const cur = currentPhase(phases, now)
  const phase = cur >= 0 && cur < phases.length ? phases[cur] : null
  return (
    <Link to={`/ao-vivo/${race.eventId}`} className="car-card live-card lift">
      <span className="live">Ao vivo{phase ? ` · ${phase.label}` : ''}</span>
      <b>{race.title ?? 'Corrida diária'}</b>
      <span className="meta">{race.track ?? '—'} · até {fmtTime(race.endsAt)}</span>
      {race.live?.registered != null && (
        <span className="live-nums"><b>{fmtInt(race.live.registered)}</b> inscritos{race.live.splits ? <> · <b>{race.live.splits}</b> split{race.live.splits === 1 ? '' : 's'}</> : null}</span>
      )}
      <span className="chips"><TierChip tier={race.tier} />{conditions(race.info) && <span className="rel">{conditions(race.info)}</span>}</span>
    </Link>
  )
}

/**
 * "Correndo agora" no perfil: a corrida em andamento em que o piloto está inscrito, com link para ela. Vem da lista
 * de inscritos do RaceOS (inscrito ≠ certeza de estar na pista, mas é o que o RaceOS publica durante o evento).
 */
export function LiveNowCard({ spot, name }: { spot: LiveSpot; name: string }) {
  return (
    <Link to={liveHref(spot, name)} className="live-now lift">
      <span className="live">Correndo agora</span>
      <span className="live-now-main">
        <b>{spot.title ?? 'Corrida'}</b>
        <span>{[spot.track, spot.team && `equipe ${spot.team}`, `até ${fmtTime(spot.endsAt)}`].filter(Boolean).join(' · ')}</span>
      </span>
      <span className="live-now-go">Ver ao vivo →</span>
    </Link>
  )
}

/** "3 de 24" — hora da corrida em andamento, só em corridas longas (3 h ou mais). */
function raceHour(p: Phase, now: number): string | null {
  if (p.kind !== 'race' || p.minutes < 180) return null
  return `${Math.min(Math.floor((now - p.start) / 3600_000) + 1, Math.ceil(p.minutes / 60))} de ${Math.round(p.minutes / 60)}`
}

const STATUS: Record<string, string> = {
  running: 'em andamento', serversrunning: 'servidores no ar', waiting: 'aguardando a largada', finished: 'encerrado',
}

/**
 * O que o RaceOS diz do evento agora (event/overview, relido pelo scanner a cada ~10 min): inscritos por classe, splits
 * abertos e o tamanho de cada um. Não há posições: a cronometragem só existe nos servidores da corrida.
 */
export function HeroLive({ l, live, maxPlayers, now }: { l: LiveStatus; live: boolean; maxPlayers: number | null; now: number }) {
  const classTotal = l.byClass.reduce((a, c) => a + c.count, 0)
  const biggest = Math.max(maxPlayers ?? 0, ...l.splitSizes)
  const full = maxPlayers ? l.splitSizes.filter((n) => n >= maxPlayers).length : 0
  const status = l.status ? STATUS[l.status.toLowerCase()] ?? l.status : null
  return (
    <section className="hero-live" aria-label="Estado do evento no RaceOS">
      <div className="hl-nums">
        <div><b>{l.registered != null ? fmtInt(l.registered) : '—'}</b><span>{live ? 'inscritos' : 'inscritos até agora'}</span></div>
        <div><b>{l.splits ?? '—'}</b><span>split{l.splits === 1 ? '' : 's'}</span></div>
        {l.servers != null && l.serversRunning != null && (
          <div><b>{l.serversRunning}<small>/{l.servers}</small></b><span>servidores no ar</span></div>
        )}
      </div>
      {classTotal > 0 && (
        <div className="hl-block">
          <span className="hl-lab">Inscritos por classe</span>
          <div className="hl-classes" role="img" aria-label={l.byClass.map((c) => `${classShort(c.class)} ${c.count}`).join(', ')}>
            {l.byClass.map((c) => <i key={c.class} className={`cls-${classKey(c.class)}`} style={{ flexGrow: c.count }} />)}
          </div>
          <div className="hl-legend">
            {l.byClass.map((c) => (
              <span key={c.class} className={`cls-${classKey(c.class)}`}>{classShort(c.class)} <b>{fmtInt(c.count)}</b>{' '}{Math.round((c.count / classTotal) * 100)}%</span>
            ))}
          </div>
        </div>
      )}
      {l.splitSizes.length > 1 && (
        <div className="hl-block">
          <span className="hl-lab">Pilotos por split{full ? ` · ${full} cheio${full === 1 ? '' : 's'}` : ''}</span>
          <div className="hl-splits" role="img" aria-label={l.splitSizes.map((n, i) => `split ${i + 1}: ${n}`).join(', ')}>
            {l.splitSizes.map((n, i) => <i key={i} style={{ height: `${Math.max(6, (n / biggest) * 100)}%` }} title={`Split ${i + 1}: ${n} pilotos`} />)}
          </div>
        </div>
      )}
      <div className="hl-foot">
        {status && <span className="ok">RaceOS: {status}</span>}
        <span>atualizado {fmtRelative(l.checkedAt, now)}</span>
        <span>posições só no resultado, depois da bandeira</span>
      </div>
    </section>
  )
}

