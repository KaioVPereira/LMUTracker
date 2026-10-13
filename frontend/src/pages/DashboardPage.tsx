import { useCallback, useEffect, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import type { MyDriver, Overview, UpcomingRace, UserDto } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import {
  ClassChip, ClassChips, CountUp, EmptyState, ErrorBox, Loading, RowsSkeleton, Skeleton, TierChip, TrackArt,
} from '../components/Bits'
import { TopBar } from '../components/TopBar'
import {
  badgeLabel, fmtClock, fmtDateTime, fmtDelta, fmtInt, fmtLap, fmtRelative, fmtTime, parseUtc, rankClass, typeLabel,
} from '../lib/format'

const REFRESH_MS = 30_000
const CLOCK_MS = 30_000

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
  const [me, setMe] = useState<MyDriver | null>(null)
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

  const loadMe = useCallback(() => {
    api<MyDriver>('/lmu/me').then(setMe).catch(() => setMe(null))
  }, [])

  useEffect(() => {
    loadOverview()
    const id = window.setInterval(loadOverview, REFRESH_MS)
    return () => window.clearInterval(id)
  }, [loadOverview])

  useEffect(() => { loadMe() }, [loadMe, user?.steamId])

  if (!user) return null

  const upcoming = overview?.upcoming ?? []
  const live = upcoming.filter((r) => isLive(r, now))
  const heroRace = live[0] ?? upcoming.find((r) => parseUtc(r.startsAt).getTime() > now) ?? null

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

        {user.steamId ? <MyDriverPanel me={me} onUnlinked={loadMe} /> : <LinkBanner />}

        <section aria-label="Números do Pitwall" className="tiles">
          <Tile n={overview?.racesFetched} l="corridas com resultado" />
          <Tile n={overview?.resultsStored} l="resultados guardados" />
          <Tile n={overview?.driversIndexed} l="pilotos conhecidos" />
          <Tile n={overview?.eventsTracked} l="corridas acompanhadas" />
        </section>

        <section aria-labelledby="sec-agenda">
          <div className="sec-h">
            <h2 id="sec-agenda">Agenda</h2>
            <span className="rel">horário local</span>
            {overview?.lastActivityAt && (
              <span className="rel" style={{ marginLeft: 'auto' }}>scanner ativo {fmtRelative(overview.lastActivityAt, now)}</span>
            )}
          </div>
          {!overview ? <RowsSkeleton rows={6} /> : upcoming.length ? (
            <div className="rows">
              {upcoming.map((r) => {
                const on = isLive(r, now)
                return (
                  <Link key={r.eventId} to={`/corridas/${r.eventId}`} className={`rowi${on ? ' is-live' : ''}`}>
                    <span className="rowi-time">
                      {fmtTime(r.startsAt)}
                      <small>{on ? 'AO VIVO' : fmtRelative(r.startsAt, now)}</small>
                    </span>
                    <span className="rowi-main">
                      <b>{r.title ?? 'Corrida diária'}</b>
                      <span>{r.track ?? '—'} · {typeLabel(r.eventType)}</span>
                    </span>
                    <span className="rowi-side"><TierChip tier={r.tier} /></span>
                    <span className="rowi-num">até {fmtTime(r.endsAt)}</span>
                  </Link>
                )
              })}
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
              {overview.recent.map((r) => (
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

        <div className="foot">
          Pitwall não é afiliado à Motorsport Games nem ao Le Mans Ultimate. Horários no seu fuso local.
        </div>
      </div>
    </>
  )
}

/** Corrida em destaque: a que está acontecendo (contagem até a bandeira) ou a próxima (contagem até a largada). */
function Hero({ race, live, liveCount }: { race: UpcomingRace; live: boolean; liveCount: number }) {
  const now = useNow(1000)
  const target = parseUtc(live ? race.endsAt : race.startsAt).getTime()
  return (
    <article className={`hero${live ? '' : ' is-next'}`} aria-labelledby="hero-title">
      <TrackArt className="hero-art" />
      <div className="hero-main">
        {live ? <span className="live">Ao vivo{liveCount > 1 ? ` · ${liveCount} corridas` : ''}</span> : <span className="next-tag">Próxima largada</span>}
        <h1 id="hero-title" className="hero-title">{race.title ?? 'Corrida diária'}</h1>
        <p className="hero-sub">{race.track ?? '—'} · {typeLabel(race.eventType)} · largada {fmtTime(race.startsAt)}</p>
        <span className="chips"><TierChip tier={race.tier} /></span>
        <div className="hero-actions">
          <Link to={`/corridas/${race.eventId}`} className="btn btn-primary">{live ? 'Acompanhar' : 'Ver detalhes'}</Link>
          <span className="hero-clock" aria-live="off">
            <b>{fmtClock(target - now)}</b>{live ? 'para a bandeira' : 'para a largada'}
          </span>
        </div>
      </div>
    </article>
  )
}

function HeroSkeleton() {
  return (
    <div className="hero" role="status" aria-label="Carregando">
      <div className="hero-main sk-stack">
        <Skeleton w={130} h={22} r={999} />
        <Skeleton w="70%" h={56} r={12} />
        <Skeleton w="45%" h={16} />
        <Skeleton w={160} h={44} r={999} />
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

function LinkBanner() {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function start() {
    setBusy(true)
    setError(null)
    try {
      const { url } = await api<{ url: string }>('/link/steam/start')
      window.location.assign(url)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível iniciar o vínculo.')
      setBusy(false)
    }
  }

  return (
    <div className="link-banner">
      <div>
        <h3>Vincule sua conta Steam</h3>
        <p>
          É assim que encontramos você nas corridas. O login acontece no site da Steam e só confirma qual é o
          seu SteamID: não recebemos sua senha.
        </p>
        {error && <div className="form-error" style={{ marginTop: 10 }}>{error}</div>}
      </div>
      <button type="button" className="btn btn-primary" onClick={start} disabled={busy}>
        {busy ? 'Abrindo a Steam…' : 'Entrar com a Steam'}
      </button>
    </div>
  )
}

function MyDriverPanel({ me, onUnlinked }: { me: MyDriver | null; onUnlinked: () => void }) {
  const { refreshUser } = useAuth()
  const navigate = useNavigate()
  const [busy, setBusy] = useState(false)

  async function unlink() {
    setBusy(true)
    try {
      await api<UserDto>('/link/steam', { method: 'DELETE' })
      await refreshUser()
      onUnlinked()
    } finally {
      setBusy(false)
    }
  }

  const unlinkBtn = (
    <button type="button" className="btn btn-ghost btn-sm" onClick={unlink} disabled={busy}>
      Desvincular Steam
    </button>
  )

  if (!me) return <Loading what="Carregando seu piloto…" rows={2} />

  if (!me.found) {
    return (
      <EmptyState title="Ainda não vimos você correr" action={unlinkBtn}>
        Steam vinculada. O Pitwall registra as corridas oficiais desde que o scanner começou a rodar: depois da
        sua próxima corrida, os resultados aparecem aqui uns 10 minutos após a bandeira.
      </EmptyState>
    )
  }

  const badge = badgeLabel(me.badge)

  return (
    <section aria-labelledby="sec-piloto">
      <div className="sec-h">
        <h2 id="sec-piloto">Seu piloto</h2>
        <span className="rel">{me.racesFound} corrida{me.racesFound === 1 ? '' : 's'} encontrada{me.racesFound === 1 ? '' : 's'}</span>
      </div>

      <div className="driver">
        <div>
          <span className="driver-name">
            {me.driverId ? <Link to={`/pilotos/${me.driverId}`} className="plain">{me.name}</Link> : me.name}
          </span>
          <span className="driver-sub">
            {me.nationality && <span className="pill">{me.nationality}</span>}
            {badge && <span className="pill">{badge}</span>}
            {me.lastSeenAt && <span>visto {fmtRelative(me.lastSeenAt)}</span>}
          </span>
          <div style={{ marginTop: 'auto', display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            {me.driverId && <Link to={`/pilotos/${me.driverId}`} className="btn btn-sm btn-primary">Ver perfil completo</Link>}
            {unlinkBtn}
          </div>
        </div>
        <RankCell label="Driver Rank" title={me.driverRankTitle} tier={me.driverRankTier} />
        <RankCell label="Safety Rank" title={me.safetyRankTitle} tier={me.safetyRankTier} />
      </div>

      <div className="tbl-wrap">
        <table>
          <thead>
            <tr>
              <th>Data</th><th>Evento</th><th>Pista</th><th>Classe</th>
              <th className="num">Largada</th><th className="num">Chegada</th><th className="num">Melhor volta</th>
              <th className="num">ΔDR</th><th className="num">ΔSR</th>
            </tr>
          </thead>
          <tbody>
            {me.recent.map((r) => {
              const dr = fmtDelta(r.driverRankAdjustment)
              const sr = fmtDelta(r.safetyRankAdjustment)
              return (
                <tr key={r.resultId} className="row-link" onClick={() => navigate(`/resultados/${r.resultId}`)}>
                  <td className="mono">{fmtDateTime(r.startsAt)}</td>
                  <td><Link to={`/resultados/${r.resultId}`} onClick={(e) => e.stopPropagation()}>{r.title ?? 'Corrida diária'}</Link></td>
                  <td>{r.track ?? '—'}</td>
                  <td><ClassChip cls={r.carClass} /></td>
                  <td className="num">{(r.classGridPos ?? r.gridPos) ? `P${r.classGridPos ?? r.gridPos}` : '—'}</td>
                  <td className="num">{r.classPosition ? `P${r.classPosition}` : (r.finishStatus ?? '—')}</td>
                  <td className="num">{fmtLap(r.bestLapTime)}</td>
                  <td className={`num ${dr.cls}`}>{dr.text}</td>
                  <td className={`num ${sr.cls}`}>{sr.text}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </section>
  )
}

function RankCell({ label, title, tier }: { label: string; title: string | null; tier: number | null }) {
  return (
    <div>
      <span className="rank-lab">{label}</span>
      <span className={`rank-big ${rankClass(title)}`}>{title ?? '—'}{title && tier !== null ? ` ${tier}` : ''}</span>
    </div>
  )
}
