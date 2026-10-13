import { useCallback, useEffect, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import type { EventInfo, Home, Overview, UpcomingRace } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { ClassChips, CountUp, EmptyState, ErrorBox, RowsSkeleton, Skeleton, TierChip } from '../components/Bits'
import { Carousel, Highlights, HomeEmpty, RecordCard, WinnerCard } from '../components/HomeSections'
import { TopBar } from '../components/TopBar'
import { currentPhase, fmtGameTime, isWet, phasesOf, raceSession, sessionsSummary, skyLabel } from '../lib/eventPhase'
import { fmtClock, fmtDateTime, fmtInt, fmtRelative, fmtTime, parseUtc, typeLabel } from '../lib/format'

// "Ao vivo" = a agenda e o estado das corridas, relidos a cada 30 s (o scanner atualiza a cada 2 min). Os destaques
// vêm de um cache de 60 s no servidor, então relemos a cada minuto. Da corrida em si o RaceOS não publica a
// cronometragem de terceiros: o que mostramos ao vivo é a fase (treino, classificação, corrida) e as condições.
const REFRESH_MS = 30_000
const HOME_REFRESH_MS = 60_000
const CLOCK_MS = 30_000
const UPCOMING_SHOWN = 8
const AUTOPLAY_MS = 4_500

/** Relógio de estado: "Ao vivo" e "em X min" mudam com ele, não a cada render. */
function useNow(every = CLOCK_MS): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), every)
    return () => window.clearInterval(id)
  }, [every])
  return now
}

const isLive = (r: UpcomingRace, now: number) =>
  parseUtc(r.startsAt).getTime() <= now && now < parseUtc(r.endsAt).getTime()

export function DashboardPage() {
  const { user } = useAuth()
  const location = useLocation()
  const navigate = useNavigate()
  const [flash, setFlash] = useState<string | null>((location.state as { flash?: string } | null)?.flash ?? null)

  const [overview, setOverview] = useState<Overview | null>(null)
  const [overviewError, setOverviewError] = useState<string | null>(null)
  const [home, setHome] = useState<Home | null>(null)
  const [homeError, setHomeError] = useState(false)
  const now = useNow()

  // Limpa o state da navegação para o aviso não voltar ao recarregar.
  useEffect(() => {
    if (location.state) navigate(location.pathname, { replace: true, state: null })
  }, [location.state, location.pathname, navigate])

  const loadOverview = useCallback(() => {
    api<Overview>('/lmu/overview')
      .then((o) => { setOverview(o); setOverviewError(null) })
      .catch((e) => setOverviewError(e instanceof ApiError ? e.message : 'Não foi possível carregar.'))
  }, [])

  const loadHome = useCallback(() => {
    api<Home>('/lmu/home').then((h) => { setHome(h); setHomeError(false) }).catch(() => setHomeError(true))
  }, [])

  useEffect(() => {
    loadOverview()
    const id = window.setInterval(loadOverview, REFRESH_MS)
    return () => window.clearInterval(id)
  }, [loadOverview])

  useEffect(() => {
    loadHome()
    const id = window.setInterval(loadHome, HOME_REFRESH_MS)
    return () => window.clearInterval(id)
  }, [loadHome])

  if (!user) return null

  const upcoming = overview?.upcoming ?? []
  const live = upcoming.filter((r) => isLive(r, now))
  const next = upcoming.filter((r) => parseUtc(r.startsAt).getTime() > now)
  // No destaque, prefere a corrida ao vivo que tem a linha do tempo das sessões (mais informação para mostrar).
  const heroRace = live.find((r) => r.info?.sessions.length) ?? live[0] ?? next[0] ?? null
  const otherLive = live.filter((r) => r !== heroRace)

  return (
    <>
      <TopBar />
      <div className="wrap">
        {flash && (
          <div className="flash" role="status">
            {flash}
            <button type="button" className="btn btn-ghost btn-sm" onClick={() => setFlash(null)}>Fechar</button>
          </div>
        )}

        {overviewError && <ErrorBox message={overviewError} onRetry={loadOverview} />}

        {!overview && !overviewError ? <HeroSkeleton /> : heroRace
          ? <Hero race={heroRace} live={live.includes(heroRace)} liveCount={live.length} />
          : (
            <EmptyState title="Nenhuma corrida na agenda agora">
              O scanner confere a agenda do RaceOS a cada 2 minutos — as próximas largadas aparecem aqui sozinhas.
            </EmptyState>
          )}

        {otherLive.length > 0 && (
          <Carousel id="sec-live" title="Também ao vivo" sub={`${otherLive.length} corrida${otherLive.length === 1 ? '' : 's'}`} autoPlayMs={AUTOPLAY_MS} narrow>
            {otherLive.map((r) => <LiveCard key={r.eventId} race={r} now={now} />)}
          </Carousel>
        )}

        {homeError && <ErrorBox message="Não deu para carregar os destaques agora." onRetry={loadHome} />}

        <Carousel id="sec-recordes" title="Voltas mais rápidas" sub="melhor volta da semana por pista e classe" loading={!home && !homeError}>
          {home?.records.length ? home.records.map((r) => <RecordCard key={`${r.track}-${r.carClass}-${r.fixedSetup}`} r={r} now={now} />) : <HomeEmpty what="recordes" />}
        </Carousel>

        <Carousel id="sec-vencedores" title="Vencedores do top split" sub="split 1, por classe" loading={!home && !homeError} autoPlayMs={AUTOPLAY_MS}>
          {home?.winners.length ? home.winners.map((w) => <WinnerCard key={`${w.eventId}-${w.carClass}`} w={w} now={now} />) : <HomeEmpty what="vencedores" />}
        </Carousel>

        {home && <Highlights climbers={home.climbers} close={home.closeFinishes} active={home.mostActive} share={home.classShare} />}

        <section aria-labelledby="sec-agenda">
          <div className="sec-h">
            <h2 id="sec-agenda">Próximas largadas</h2>
            <span className="rel">horário local</span>
            {overview?.lastActivityAt && (
              <span className="rel" style={{ marginLeft: 'auto' }}>scanner ativo {fmtRelative(overview.lastActivityAt, now)}</span>
            )}
          </div>
          {!overview ? <RowsSkeleton rows={6} /> : next.length ? (
            <div className="rows">
              {next.slice(0, UPCOMING_SHOWN).map((r) => (
                <div key={r.eventId} className="rowi">
                  <span className="rowi-time">
                    {fmtTime(r.startsAt)}
                    <small>{fmtRelative(r.startsAt, now)}</small>
                  </span>
                  <span className="rowi-main">
                    <b>{r.title ?? 'Corrida diária'}</b>
                    <span>{[r.track ?? '—', sessionsSummary(r.info), conditions(r.info)].filter(Boolean).join(' · ')}</span>
                  </span>
                  <span className="rowi-side">
                    {r.info?.fixedSetup !== null && r.info?.fixedSetup !== undefined && (
                      <span className={`setup ${r.info.fixedSetup ? 'is-fixed' : ''}`}>{r.info.fixedSetup ? 'Setup fixo' : 'Setup livre'}</span>
                    )}
                    <TierChip tier={r.tier} />
                  </span>
                  <span className="rowi-num">até {fmtTime(r.endsAt)}</span>
                </div>
              ))}
            </div>
          ) : (
            <EmptyState title="Agenda vazia">Nenhuma corrida futura na agenda neste momento.</EmptyState>
          )}
        </section>

        <section aria-labelledby="sec-recentes">
          <div className="sec-h">
            <h2 id="sec-recentes">Resultados recentes</h2>
            <Link to="/corridas" className="sec-link">Ver todas →</Link>
          </div>
          {!overview ? (
            <div className="grid-cards" role="status" aria-label="Carregando">
              {[0, 1, 2, 3].map((i) => (
                <div key={i} className="race-card"><Skeleton w={80} h={18} r={999} /><Skeleton w="75%" h={22} /><Skeleton w="55%" /><Skeleton w="40%" /></div>
              ))}
            </div>
          ) : overview.recent.length ? (
            <div className="grid-cards">
              {overview.recent.slice(0, 8).map((r) => (
                <Link key={r.eventId} to={`/corridas/${r.eventId}`} className="race-card lift">
                  <span className="top"><TierChip tier={r.tier} /><span className="rel">{fmtRelative(r.startsAt, now)}</span></span>
                  <h3>{r.title ?? 'Corrida diária'}</h3>
                  <span className="meta">{r.track ?? '—'} · {fmtDateTime(r.startsAt)}</span>
                  <span className="chips"><ClassChips classes={r.classes} /></span>
                  <span className="foot-nums"><span><b>{r.splits}</b> splits</span><span><b>{fmtInt(r.drivers)}</b> pilotos</span></span>
                </Link>
              ))}
            </div>
          ) : (
            <EmptyState title="Ainda sem resultados">
              Assim que uma corrida da agenda terminar, o resultado aparece aqui uns 10 minutos depois.
            </EmptyState>
          )}
        </section>

        <section aria-labelledby="sec-numeros">
          <div className="sec-h"><h2 id="sec-numeros">O Pitwall em números</h2></div>
          <div className="tiles">
            <Tile n={overview?.racesFetched} l="corridas com resultado" />
            <Tile n={overview?.resultsStored} l="resultados guardados" />
            <Tile n={overview?.driversIndexed} l="pilotos conhecidos" />
            <Tile n={overview?.eventsTracked} l="corridas acompanhadas" />
          </div>
        </section>

        <div className="foot">
          Pitwall não é afiliado à Motorsport Games nem ao Le Mans Ultimate. Horários no seu fuso local.
        </div>
      </div>
    </>
  )
}

/** "15:00 no jogo · 25 °C · céu limpo" — condições da largada da corrida. */
function conditions(info: EventInfo | null): string | null {
  const s = raceSession(info)
  if (!s) return null
  const parts = [`${fmtGameTime(s.timeOfDay)} no jogo`]
  if (s.temperatureC !== null) parts.push(`${s.temperatureC} °C`)
  if (s.rainChance) parts.push(`chuva ${s.rainChance}%`)
  else if (isWet(s.sky)) parts.push(skyLabel(s.sky)!.toLowerCase())
  return parts.join(' · ')
}

/** Linha do tempo das sessões, com a atual destacada e o progresso dela. */
function Phases({ race, now }: { race: UpcomingRace; now: number }) {
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
 * Sem botão de "acompanhar" — o RaceOS não publica a cronometragem durante a corrida.
 */
function Hero({ race, live, liveCount }: { race: UpcomingRace; live: boolean; liveCount: number }) {
  const now = useNow(1000)
  const phases = phasesOf(race)
  const cur = currentPhase(phases, now)
  const phase = cur >= 0 && cur < phases.length ? phases[cur] : null
  const nextPhase = phases[cur + 1]
  const s = raceSession(race.info)

  let clock: { value: number; label: string }
  if (!live) clock = { value: parseUtc(race.startsAt).getTime() - now, label: 'para a largada' }
  else if (phase && phase.kind !== 'race' && nextPhase) clock = { value: nextPhase.start - now, label: `para ${nextPhase.kind === 'race' ? 'a largada da corrida' : nextPhase.label.toLowerCase()}` }
  else clock = { value: parseUtc(race.endsAt).getTime() - now, label: 'para a bandeira' }

  return (
    <article className={`hero${live ? '' : ' is-next'}`} aria-labelledby="hero-title">
      <div className="hero-main">
        {live
          ? <span className="live">Ao vivo{phase ? ` · ${phase.label}` : ''}{liveCount > 1 ? ` · ${liveCount} corridas` : ''}</span>
          : <span className="next-tag">Próxima largada</span>}
        <h1 id="hero-title" className="hero-title">{race.title ?? 'Corrida diária'}</h1>
        <p className="hero-sub">{race.track ?? '—'} · {typeLabel(race.eventType)} · começa {fmtTime(race.startsAt)}</p>
        <div className="hero-clock" aria-live="off"><b>{fmtClock(clock.value)}</b>{clock.label}</div>
        <Phases race={race} now={now} />
      </div>
      <dl className="hero-facts">
        <div><dt>Nível</dt><dd><TierChip tier={race.tier} /></dd></div>
        {s && <div><dt>Hora no jogo</dt><dd className="mono">{fmtGameTime(s.timeOfDay)}</dd></div>}
        {s?.temperatureC !== null && s?.temperatureC !== undefined && <div><dt>Temperatura</dt><dd>{s.temperatureC} °C</dd></div>}
        {s && <div><dt>Céu</dt><dd>{skyLabel(s.sky) ?? '—'}{s.rainChance ? ` · chuva ${s.rainChance}%` : ''}</dd></div>}
        {race.info?.fixedSetup !== null && race.info?.fixedSetup !== undefined && <div><dt>Setup</dt><dd>{race.info.fixedSetup ? 'Fixo' : 'Livre'}</dd></div>}
        {race.info?.maxPlayers && <div><dt>Grid por split</dt><dd className="mono">até {race.info.maxPlayers}</dd></div>}
        <div className="fact-end"><dt>Termina</dt><dd className="mono">~{fmtTime(race.endsAt)}</dd></div>
      </dl>
    </article>
  )
}

/** Card de uma corrida acontecendo agora (sem link: ainda não há resultado para mostrar). */
function LiveCard({ race, now }: { race: UpcomingRace; now: number }) {
  const phases = phasesOf(race)
  const cur = currentPhase(phases, now)
  const phase = cur >= 0 && cur < phases.length ? phases[cur] : null
  return (
    <div className="car-card live-card">
      <span className="live">Ao vivo{phase ? ` · ${phase.label}` : ''}</span>
      <b>{race.title ?? 'Corrida diária'}</b>
      <span className="meta">{race.track ?? '—'} · até {fmtTime(race.endsAt)}</span>
      <span className="chips"><TierChip tier={race.tier} />{conditions(race.info) && <span className="rel">{conditions(race.info)}</span>}</span>
    </div>
  )
}

function HeroSkeleton() {
  return (
    <div className="hero" role="status" aria-label="Carregando">
      <div className="hero-main sk-stack">
        <Skeleton w={130} h={22} r={999} />
        <Skeleton w="70%" h={56} r={12} />
        <Skeleton w="45%" h={16} />
        <Skeleton w="60%" h={30} r={8} />
      </div>
    </div>
  )
}

function Tile({ n, l }: { n: number | undefined; l: string }) {
  return (
    <div className="tile">
      <span className="n">{n === undefined ? <Skeleton w={70} h={30} /> : <CountUp value={n} />}</span>
      <span className="l">{l}</span>
    </div>
  )
}
