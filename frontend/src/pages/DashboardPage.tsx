import { useCallback, useEffect, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import type { FollowedDriver, Home, Overview } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { ClassChips, CountUp, EmptyState, ErrorBox, RowsSkeleton, Skeleton, TierChip } from '../components/Bits'
import { Carousel, Highlights, HomeEmpty, RecordCard, WinnerCard } from '../components/HomeSections'
import { Hero, LiveCard } from '../components/LiveEvent'
import { TopBar } from '../components/TopBar'
import { sessionsSummary } from '../lib/eventPhase'
import { fmtDateTime, fmtInt, fmtRelative, fmtTime, parseUtc } from '../lib/format'
import { conditions, isLive, liveHref, useNow } from '../lib/live'

// "Ao vivo" = a agenda e o estado das corridas, relidos a cada 30 s (o scanner atualiza a cada 2 min). Os destaques
// vêm de um cache de 60 s no servidor, então relemos a cada minuto. Da corrida em si o RaceOS não publica a
// cronometragem de terceiros: o que mostramos ao vivo é a fase (treino, classificação, corrida) e as condições.
const REFRESH_MS = 30_000
const HOME_REFRESH_MS = 60_000
const UPCOMING_SHOWN = 8
const AUTOPLAY_MS = 4_500

export function DashboardPage() {
  const { user } = useAuth()
  const location = useLocation()
  const navigate = useNavigate()
  const [flash, setFlash] = useState<string | null>((location.state as { flash?: string } | null)?.flash ?? null)

  const [overview, setOverview] = useState<Overview | null>(null)
  const [overviewError, setOverviewError] = useState<string | null>(null)
  const [home, setHome] = useState<Home | null>(null)
  const [homeError, setHomeError] = useState(false)
  const [follows, setFollows] = useState<FollowedDriver[] | null>(null)
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

  // Pilotos seguidos (só logado): recarrega junto com a agenda.
  const uid = user?.id
  const loadFollows = useCallback(() => {
    if (!uid) return
    api<FollowedDriver[]>('/lmu/follows').then(setFollows).catch(() => setFollows(null))
  }, [uid])
  useEffect(() => {
    loadFollows()
    const id = window.setInterval(loadFollows, REFRESH_MS * 2)
    return () => window.clearInterval(id)
  }, [loadFollows])

  useEffect(() => {
    loadHome()
    const id = window.setInterval(loadHome, HOME_REFRESH_MS)
    return () => window.clearInterval(id)
  }, [loadHome])

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
          ? <Hero race={heroRace} live={live.includes(heroRace)} liveCount={live.length} to={`/ao-vivo/${heroRace.eventId}`} />
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

        {user && follows && follows.length > 0 && <FollowingSection list={follows} now={now} />}

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

/**
 * Pilotos seguidos, com a corrida mais recente de cada um. "Acabou de correr" = resultado de até 3 h atrás — o RaceOS
 * não diz quem está inscrito antes, então o destaque chega uns 10 min depois da bandeira.
 */
function FollowingSection({ list, now }: { list: FollowedDriver[]; now: number }) {
  const RECENT = 3 * 3600_000
  return (
    <section aria-labelledby="sec-seg">
      <div className="sec-h"><h2 id="sec-seg">Seguindo</h2><span className="rel">{list.length} piloto{list.length === 1 ? '' : 's'}</span></div>
      <div className="grid-cards">
        {list.slice(0, 8).map((f) => {
          const l = f.lastRace
          const fresh = l && now - parseUtc(l.startsAt).getTime() < RECENT
          const live = f.liveNow
          return (
            <Link key={f.driverId} to={live ? liveHref(live, f.name) : l ? `/resultados/${l.resultId}` : `/pilotos/${f.driverId}`}
              className={`race-card lift follow-card${fresh || live ? ' is-fresh' : ''}`}>
              <span className="top">
                <b className="follow-name">{f.name}</b>
                {live ? <span className="live">Correndo agora</span>
                  : fresh ? <span className="live-soft">acabou de correr</span> : <span className="rel">{l ? fmtRelative(l.startsAt, now) : 'sem corridas'}</span>}
              </span>
              {live ? (
                <span className="meta">{live.title ?? 'Corrida'} · {live.track ?? '—'}{live.team ? ` · equipe ${live.team}` : ''}</span>
              ) : l ? (
                <>
                  <span className="follow-pos">{l.finishStatus === 'Finished Normally' && l.classPosition ? `P${l.classPosition}` : (l.finishStatus === 'DNF' ? 'Abandonou' : '—')}<ClassChips classes={l.carClass} /></span>
                  <span className="meta">{l.title ?? 'Corrida'} · {l.track ?? '—'} · split {l.splitNo}</span>
                </>
              ) : <span className="meta">Ainda sem corrida registrada.</span>}
              <span className="foot-nums"><span><b>{f.racesLast7Days}</b> corridas na semana</span></span>
            </Link>
          )
        })}
      </div>
    </section>
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
