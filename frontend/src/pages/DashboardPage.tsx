import { useCallback, useEffect, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import type { MyDriver, Overview, UserDto } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { TierChip } from '../components/Bits'
import { TopBar } from '../components/TopBar'
import {
  badgeLabel, classLabel, fmtDateTime, fmtDelta, fmtInt, fmtLap, fmtRelative, fmtTime, parseUtc,
  rankClass, typeLabel,
} from '../lib/format'

const REFRESH_MS = 60_000
const CLOCK_MS = 30_000

/** Relógio de estado: "Ao vivo" e "em X min" mudam com ele, não a cada render. */
function useNow(): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), CLOCK_MS)
    return () => window.clearInterval(id)
  }, [])
  return now
}

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

  return (
    <>
      <TopBar />
      <div className="wrap">
        {flash && (
          <div className="flash" role="status">
            {flash}{' '}
            <button type="button" className="btn btn-ghost btn-sm" onClick={() => setFlash(null)}>Fechar</button>
          </div>
        )}

        <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
          <span className="eyebrow">Painel</span>
          <h1 style={{ fontSize: 'clamp(26px, 4vw, 36px)' }}>Olá, {user.displayName}</h1>
        </div>

        {user.steamId ? <MyDriverPanel me={me} onUnlinked={loadMe} /> : <LinkBanner />}

        <section aria-labelledby="sec-geral">
          <div className="sec-h">
            <span className="eyebrow">Le Mans Ultimate</span>
            <h2 id="sec-geral">Visão geral</h2>
            {overview?.lastActivityAt && (
              <span className="rel">scanner ativo {fmtRelative(overview.lastActivityAt, now)}</span>
            )}
          </div>
          {overviewError && <div className="form-error">{overviewError}</div>}
          <div className="tiles">
            <Tile n={overview?.eventsTracked} l="Corridas acompanhadas" />
            <Tile n={overview?.racesFetched} l="Corridas com resultado" />
            <Tile n={overview?.resultsStored} l="Resultados guardados" />
            <Tile n={overview?.driversIndexed} l="Pilotos conhecidos" />
          </div>
        </section>

        <section aria-labelledby="sec-agenda">
          <div className="sec-h">
            <span className="eyebrow">Agenda</span>
            <h2 id="sec-agenda">Próximas corridas</h2>
          </div>
          <div className="tbl-wrap">
            <table>
              <thead>
                <tr><th>Largada</th><th>Evento</th><th>Pista</th><th>Nível</th><th>Tipo</th><th>Quando</th></tr>
              </thead>
              <tbody>
                {overview?.upcoming.length ? overview.upcoming.map((r) => {
                  const live = parseUtc(r.startsAt).getTime() <= now && now < parseUtc(r.endsAt).getTime()
                  return (
                    <tr key={r.eventId} className="row-link" onClick={() => navigate(`/corridas/${r.eventId}`)}>
                      <td className="mono">{fmtTime(r.startsAt)}</td>
                      <td><Link to={`/corridas/${r.eventId}`} onClick={(e) => e.stopPropagation()}>{r.title ?? 'Corrida diária'}</Link></td>
                      <td>{r.track ?? '—'}</td>
                      <td><TierChip tier={r.tier} /></td>
                      <td>{typeLabel(r.eventType)}</td>
                      <td>{live ? <span className="live">Ao vivo</span> : <span className="rel">{fmtRelative(r.startsAt, now)}</span>}</td>
                    </tr>
                  )
                }) : (
                  <tr><td colSpan={6} className="empty">
                    {overview ? 'Nenhuma corrida na agenda agora. O scanner atualiza a cada 2 minutos.' : 'Carregando…'}
                  </td></tr>
                )}
              </tbody>
            </table>
          </div>
        </section>

        <section aria-labelledby="sec-recentes">
          <div className="sec-h">
            <span className="eyebrow">Resultados</span>
            <h2 id="sec-recentes">Corridas recentes</h2>
            <Link to="/corridas" className="sec-link">Ver todas</Link>
          </div>
          <div className="tbl-wrap">
            <table>
              <thead>
                <tr><th>Data</th><th>Evento</th><th>Pista</th><th>Nível</th><th className="num">Splits</th><th className="num">Pilotos</th></tr>
              </thead>
              <tbody>
                {overview?.recent.length ? overview.recent.map((r) => (
                  <tr key={r.eventId} className="row-link" onClick={() => navigate(`/corridas/${r.eventId}`)}>
                    <td className="mono">{fmtDateTime(r.startsAt)}</td>
                    <td><Link to={`/corridas/${r.eventId}`} onClick={(e) => e.stopPropagation()}>{r.title ?? 'Corrida diária'}</Link></td>
                    <td>{r.track ?? '—'}</td>
                    <td><TierChip tier={r.tier} /></td>
                    <td className="num">{r.splits}</td>
                    <td className="num">{fmtInt(r.drivers)}</td>
                  </tr>
                )) : (
                  <tr><td colSpan={6} className="empty">
                    {overview ? 'Assim que uma corrida da agenda terminar, os resultados aparecem aqui.' : 'Carregando…'}
                  </td></tr>
                )}
              </tbody>
            </table>
          </div>
        </section>

        <div className="foot">
          Pitwall não é afiliado à Motorsport Games nem ao Le Mans Ultimate. Horários no seu fuso local.
        </div>
      </div>
    </>
  )
}

function Tile({ n, l }: { n: number | undefined; l: string }) {
  return (
    <div className="tile">
      <span className="n">{n === undefined ? '—' : fmtInt(n)}</span>
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

  if (!me) return <div className="card pad note">Carregando seu piloto…</div>

  if (!me.found) {
    return (
      <section aria-labelledby="sec-piloto">
        <div className="sec-h">
          <span className="eyebrow">Seu piloto</span>
          <h2 id="sec-piloto">Ainda não te encontramos</h2>
        </div>
        <div className="card pad" style={{ display: 'flex', justifyContent: 'space-between', gap: 16, flexWrap: 'wrap', alignItems: 'center' }}>
          <p className="note" style={{ maxWidth: '62ch' }}>
            Steam vinculada (<span className="mono">{me.steamId}</span>). Seus resultados aparecem aqui depois da
            sua próxima corrida oficial: o scanner guarda as corridas a partir de quando começou a rodar.
          </p>
          {unlinkBtn}
        </div>
      </section>
    )
  }

  const badge = badgeLabel(me.badge)

  return (
    <section aria-labelledby="sec-piloto">
      <div className="sec-h">
        <span className="eyebrow">Seu piloto</span>
        <h2 id="sec-piloto">{me.name ?? 'Seu piloto'}</h2>
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
            {me.driverId && <Link to={`/pilotos/${me.driverId}`} className="btn btn-sm">Ver perfil completo</Link>}
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
                  <td><span className="cls">{classLabel(r.carClass)}</span></td>
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
      <span className={`rank-big ${rankClass(title)}`}>{title ?? '—'}</span>
      {tier !== null && <span className="mono note">Tier {tier}</span>}
    </div>
  )
}
