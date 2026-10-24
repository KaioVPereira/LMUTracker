import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import type { LiveEntry, LiveEvent } from '../api/types'
import { ClassBarCell, ClassChip, EmptyState, ErrorBox, Loading, MoreButton, Rank } from '../components/Bits'
import { Flag } from '../components/Flag'
import { Hero } from '../components/LiveEvent'
import { TopBar } from '../components/TopBar'
import { classKey, classShort } from '../lib/classes'
import { useFollows } from '../lib/follows'
import { fmtInt, fmtRelative, parseUtc, rankClass } from '../lib/format'
import { isLive, useNow } from '../lib/live'

// Evento em andamento: o que o RaceOS diz dele (fase, inscritos, splits) e quem está inscrito. O scanner relê o
// estado a cada ~10 min e a lista a cada 30 min; aqui relemos a cada 5 min (a lista de um evento grande, como as 24h,
// passa de 300 KB comprimida). O relógio e as fases andam sozinhos. Posições não existem: a cronometragem só vive
// nos servidores da corrida.
const REFRESH_MS = 5 * 60_000
const PAGE = 100

type Row = LiveEntry & { splitNo: number | null }

export function LivePage() {
  const { eventId = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const [data, setData] = useState<LiveEvent | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notFound, setNotFound] = useState(false)
  // ?q= vem do "Correndo agora" do perfil: abre a lista já procurando o piloto.
  const [q, setQ] = useState(() => new URLSearchParams(window.location.search).get('q') ?? '')
  const [limit, setLimit] = useState(PAGE)
  const { isFollowing } = useFollows()
  const now = useNow()

  const load = useCallback(() => {
    api<LiveEvent>(`/lmu/live/${encodeURIComponent(eventId)}`)
      .then((d) => { setData(d); setError(null) })
      .catch((e) => {
        if (e instanceof ApiError && e.status === 404) setNotFound(true)
        else setError(e instanceof ApiError ? e.message : 'Não foi possível carregar.')
      })
  }, [eventId])

  useEffect(() => {
    load()
    const id = window.setInterval(load, REFRESH_MS)
    return () => window.clearInterval(id)
  }, [load])

  const rows: Row[] = useMemo(
    () => (data?.splits ?? []).flatMap((s) => s.entries.map((e) => ({ ...e, splitNo: s.splitNo }))),
    [data],
  )
  const bySplit = data?.entriesSource === 'splits'
  const splitNos = (data?.splits ?? []).map((s) => s.splitNo).filter((n): n is number => n !== null)
  const split = params.get('split') === 'todos' || !bySplit ? null : Number(params.get('split') ?? splitNos[0] ?? 1)
  const cls = params.get('classe') ?? ''
  const classes = [...new Set(rows.map((r) => classKey(r.carClass)))].filter((k) => k !== 'other')
    .sort((a, b) => ['hyper', 'lmp2', 'lmp3', 'gt3'].indexOf(a) - ['hyper', 'lmp2', 'lmp3', 'gt3'].indexOf(b))

  const term = q.trim().toLowerCase()
  const searching = term.length > 0
  const shown = rows.filter((r) =>
    (searching || split === null || r.splitNo === split)
    && (!cls || classKey(r.carClass) === cls)
    && (!searching || [r.name, r.team, r.car, r.livery, ...r.members.map((m) => m.name)].some((x) => x?.toLowerCase().includes(term))))
  // Quem você segue: o próprio inscrito ou, em evento de equipe, um dos pilotos dela.
  const followed = rows.flatMap((r) => [
    ...(isFollowing(r.driverId) ? [{ who: r.name, row: r }] : []),
    ...r.members.filter((m) => isFollowing(m.driverId)).map((m) => ({ who: m.name, row: r })),
  ])
  const followsRow = (r: Row) => isFollowing(r.driverId) || r.members.some((m) => isFollowing(m.driverId))
  const known = shown.reduce((n, r) => n + (r.driverId ? 1 : 0) + r.members.filter((m) => m.driverId).length, 0)
  const team = data?.teamEvent ?? false
  const numbers = rows.some((r) => r.carNumber) // o RaceOS nem sempre manda o número; sem ele, sem coluna

  const set = (patch: Record<string, string | null>) => {
    const next = new URLSearchParams(params)
    for (const [k, v] of Object.entries(patch)) { if (v === null) next.delete(k); else next.set(k, v) }
    setParams(next, { replace: true })
    setLimit(PAGE)
  }

  if (notFound) return <><TopBar /><div className="wrap"><ErrorBox message="Evento não encontrado na agenda." /></div></>
  if (!data) return <><TopBar /><div className="wrap">{error ? <ErrorBox message={error} onRetry={load} /> : <Loading />}</div></>

  const race = data.race
  const live = isLive(race, now)
  const ended = parseUtc(race.endsAt).getTime() <= now
  const counts = classCounts(shown)
  const ranks = rankCounts(shown)

  return (
    <>
      <TopBar />
      <div className="wrap">
        <nav className="crumbs" aria-label="Caminho"><Link to="/">Início</Link><span>/</span><span>{race.title ?? 'Evento'}</span></nav>

        {data.hasResult && (
          <div className="flash" role="status">
            A bandeira caiu e o resultado completo já saiu.
            <Link to={`/corridas/${race.eventId}`} className="btn btn-primary btn-sm">Ver o resultado →</Link>
          </div>
        )}
        {!data.hasResult && ended && (
          <div className="card pad note">A corrida terminou. O resultado completo (posições, voltas, DR/SR) costuma sair uns minutos depois — esta página avisa quando chegar.</div>
        )}

        <Hero race={race} live={live} />

        {followed.length > 0 && (
          <section aria-labelledby="sec-seg-live">
            <div className="sec-h"><h2 id="sec-seg-live">Quem você segue</h2><span className="rel">{followed.length} neste evento</span></div>
            <div className="chips">
              {followed.map(({ who, row }) => (
                <button key={`${row.splitNo}-${row.name}-${who}`} type="button" className="chip"
                  onClick={() => { setQ(who); set({ split: row.splitNo ? String(row.splitNo) : null, classe: null }) }}>
                  {who} <span className="chip-sub">{team ? row.name : row.splitNo ? `split ${row.splitNo}` : 'inscrito'}</span>
                </button>
              ))}
            </div>
          </section>
        )}

        <section aria-labelledby="sec-pilotos">
          <div className="sec-h">
            <h2 id="sec-pilotos">{bySplit ? 'Pilotos por split' : 'Inscritos'}</h2>
            <span className="rel">{fmtInt(rows.length)} {bySplit ? 'pilotos' : 'inscrições'}</span>
            {data.entriesAt && <span className="rel" style={{ marginLeft: 'auto' }}>lista do RaceOS {fmtRelative(data.entriesAt, now)}</span>}
          </div>

          {rows.length === 0 ? (
            <EmptyState title="A lista de pilotos ainda não chegou">
              O Pitwall relê o evento no RaceOS a cada 10 minutos, desde meia hora antes da largada. Assim que a lista vier, ela aparece aqui.
            </EmptyState>
          ) : (
            <>
              <div className="filters">
                <label className="filter live-search">
                  Procurar
                  <input type="search" value={q} onChange={(e) => { setQ(e.target.value); setLimit(PAGE) }} placeholder="Piloto, equipe, carro ou pintura" />
                </label>
              </div>

              {classes.length > 1 && (
                <div className="chips" role="tablist" aria-label="Classe">
                  <button type="button" role="tab" className="chip" aria-selected={!cls} onClick={() => set({ classe: null })}>Todas as classes</button>
                  {classes.map((k) => (
                    <button key={k} type="button" role="tab" className={`chip chip-cls cls-${k}`} aria-selected={cls === k} onClick={() => set({ classe: k })}>
                      <span className="cls-dot" aria-hidden="true" />{classShort(k)}
                    </button>
                  ))}
                </div>
              )}

              {bySplit && !searching && (
                <div className="chips live-splits" role="tablist" aria-label="Splits">
                  <button type="button" role="tab" className="chip" aria-selected={split === null} onClick={() => set({ split: 'todos' })}>Todos</button>
                  {data.splits.filter((s) => s.splitNo !== null).map((s) => (
                    <button key={s.splitNo} type="button" role="tab" className="chip" aria-selected={s.splitNo === split} onClick={() => set({ split: String(s.splitNo) })}>
                      Split {s.splitNo} <span className="chip-sub">{s.entries.length}</span>
                    </button>
                  ))}
                </div>
              )}

              <p className="note live-sum">
                {searching ? `${fmtInt(shown.length)} encontrados${bySplit ? ' em todos os splits' : ''}` : split !== null ? `Split ${split}: ${fmtInt(shown.length)} pilotos` : `${fmtInt(shown.length)} ${bySplit ? 'pilotos' : 'inscritos'}`}
                {counts.map(([k, n]) => <span key={k} className={`cls-${k}`}> · {classShort(k)} <b>{n}</b></span>)}
                {shown.length > 0 && <> · {known} {team ? 'pilotos' : ''} com perfil no Pitwall</>}
              </p>
              {ranks.length > 0 && (
                <p className="note live-sum">
                  DR dos inscritos:{ranks.map(([k, n], i) => <span key={k} className={rankClass(k)}>{i ? ' ·' : ''} {k} <b>{n}</b></span>)}
                </p>
              )}

              {/* Desktop: tabela. Celular: um cartão por inscrito (a tabela não cabe — carro, DR e SR sumiam à direita). */}
              <div className="tbl-wrap live-table">
                <table className="results">
                  <thead>
                    <tr>
                      <th aria-hidden="true"></th>{numbers && <th className="num">Nº</th>}<th>{team ? 'Equipe e pilotos' : 'Piloto'}</th><th className="num">Classe</th><th>Carro</th>
                      {(split === null || searching) && bySplit && <th className="num">Split</th>}
                      <th title={team ? 'DR da inscrição segundo o RaceOS' : 'DR atual segundo o RaceOS'}>DR</th><th title={team ? 'SR da inscrição segundo o RaceOS' : 'SR atual segundo o RaceOS'}>SR</th>
                    </tr>
                  </thead>
                  <tbody>
                    {shown.length ? shown.slice(0, limit).map((r, i) => (
                      <tr key={`${r.splitNo}-${r.name}-${i}`} className={followsRow(r) ? 'is-followed' : undefined}>
                        <ClassBarCell cls={r.carClass} />
                        {numbers && <td className="num mono">{r.carNumber ? `#${r.carNumber}` : '—'}</td>}
                        <td><Who r={r} /><Crew r={r} /></td>
                        <td className="num"><ClassChip cls={r.carClass} /></td>
                        <td className="car"><Car r={r} /></td>
                        {(split === null || searching) && bySplit && <td className="num mono">{r.splitNo ?? '—'}</td>}
                        <td><Rank title={r.driverRankTitle} tier={r.driverRankTier} /></td>
                        <td><Rank title={r.safetyRankTitle} tier={r.safetyRankTier} /></td>
                      </tr>
                    )) : (
                      <tr><td colSpan={8} className="empty">Ninguém neste filtro.</td></tr>
                    )}
                  </tbody>
                </table>
              </div>
              <ul className="live-list">
                {shown.length ? shown.slice(0, limit).map((r, i) => (
                  <li key={`${r.splitNo}-${r.name}-${i}`} className={`live-item cls-${classKey(r.carClass)}${followsRow(r) ? ' is-followed' : ''}`}>
                    <div className="li-top">
                      <span className="li-name">{numbers && r.carNumber && <span className="mono li-num">#{r.carNumber}</span>}<Who r={r} /></span>
                      <ClassChip cls={r.carClass} />
                    </div>
                    <Crew r={r} />
                    <div className="li-car"><Car r={r} /></div>
                    <div className="li-ranks">
                      <span>DR <Rank title={r.driverRankTitle} tier={r.driverRankTier} /></span>
                      <span>SR <Rank title={r.safetyRankTitle} tier={r.safetyRankTier} /></span>
                      {bySplit && r.splitNo && <span>Split <b className="mono">{r.splitNo}</b></span>}
                    </div>
                  </li>
                )) : <li className="empty">Ninguém neste filtro.</li>}
              </ul>
              {shown.length > limit && <MoreButton shown={limit} total={shown.length} loading={false} onClick={() => setLimit((n) => n + PAGE)} />}
            </>
          )}
          <p className="note">
            Lista e DR/SR atuais vêm do RaceOS (relidos a cada 30 min). {bySplit ? '' : `O RaceOS não diz em que split cada piloto caiu enquanto o evento acontece${race.live?.splits ? ` (são ${race.live.splits} splits)` : ''} — isso aparece no resultado. `}
            Posições, voltas e tempos só existem nos servidores da corrida e chegam aqui no resultado, depois da bandeira.
          </p>
        </section>
      </div>
    </>
  )
}

/** Nome do inscrito (equipe, em evento de equipe) com link para o perfil quando o Pitwall conhece. */
function Who({ r }: { r: LiveEntry }) {
  return <>{r.driverId ? <Link to={`/pilotos/${r.driverId}`}>{r.name}</Link> : r.name}<Flag code={r.nationality} /></>
}

/** Equipe e pilotos (os pilotos com link para o perfil). */
function Crew({ r }: { r: LiveEntry }) {
  if (!r.team && r.members.length === 0) return null
  return (
    <span className="sub-line">
      {r.team && <>{r.team}{r.members.length > 0 && ' · '}</>}
      {r.members.map((m, j) => (
        <span key={m.name}>{j > 0 && ', '}{m.driverId ? <Link to={`/pilotos/${m.driverId}`}>{m.name}</Link> : m.name}</span>
      ))}
    </span>
  )
}

/** Carro (link para a página dele) e a pintura escolhida. */
function Car({ r }: { r: LiveEntry }) {
  return (
    <>
      {r.car ? <Link to={`/carros/${encodeURIComponent(r.car)}`} className="plain">{r.car}</Link> : '—'}
      {r.livery && <span className="sub-line">{r.livery}</span>}
    </>
  )
}

/** [["Platinum", 3], ["Gold", 40]…] — distribuição do DR, do maior para o menor. */
function rankCounts(rows: LiveEntry[]): [string, number][] {
  const order = ['Platinum', 'Gold', 'Silver', 'Bronze']
  const m = new Map<string, number>()
  for (const r of rows) if (r.driverRankTitle) m.set(r.driverRankTitle, (m.get(r.driverRankTitle) ?? 0) + 1)
  return [...m.entries()].sort((a, b) => (order.indexOf(a[0]) + 99) % 99 - (order.indexOf(b[0]) + 99) % 99)
}

/** [["hyper", 21], ["gt3", 28]] na ordem do site. */
function classCounts(rows: LiveEntry[]): [string, number][] {
  const m = new Map<string, number>()
  for (const r of rows) { const k = classKey(r.carClass); if (k !== 'other') m.set(k, (m.get(k) ?? 0) + 1) }
  const order = ['hyper', 'lmp2', 'lmp3', 'gt3']
  return [...m.entries()].sort((a, b) => order.indexOf(a[0]) - order.indexOf(b[0]))
}
