import { useEffect, useRef, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import type { CarListItem, CarRanking, DriverRankingKind, Leaderboard, Ranking, RankingRow, SearchTrack } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { ClassChip, EmptyState, ErrorBox, Rank, RowsSkeleton } from '../components/Bits'
import { LeaderboardTable } from '../components/Leaderboard'
import { TopBar } from '../components/TopBar'
import { classShort } from '../lib/classes'
import { CLASS_TABS, SETUP_TABS } from '../lib/filters'
import { useFollows } from '../lib/follows'
import { fmtPct } from '../lib/format'
import { useApi } from '../lib/useApi'

type TabKey = 'voltas' | 'dr' | 'sr' | 'vitorias' | 'poles' | 'recuperacao' | 'em-alta' | 'limpos' | 'ativos' | 'carros'

const TABS: { key: TabKey; label: string; kind?: DriverRankingKind; about: string }[] = [
  { key: 'voltas', label: 'Voltas mais rápidas', about: 'A melhor volta de cada piloto em corridas oficiais, por pista e classe. Setup fixo e livre são rankings separados.' },
  { key: 'dr', label: 'Driver Rank', kind: 'dr', about: 'Os pilotos com o Driver Rank mais alto, pelo último rank visto em corrida.' },
  { key: 'sr', label: 'Safety Rank', kind: 'sr', about: 'Os pilotos com o Safety Rank mais alto, pelo último rank visto em corrida.' },
  { key: 'vitorias', label: 'Vitórias', kind: 'wins', about: 'Quem mais venceu na classe (P1 do split), com pódios e aproveitamento.' },
  { key: 'poles', label: 'Poles', kind: 'poles', about: 'Quem mais largou na frente: P1 da classe na classificação.' },
  { key: 'recuperacao', label: 'Recuperação', kind: 'gains', about: 'Quem mais ganhou posições na classe, do grid à bandeira (só corridas terminadas).' },
  { key: 'em-alta', label: 'Em alta', kind: 'risers', about: 'Quem mais ganhou Driver Rank no período, somando o que o jogo reportou em cada corrida.' },
  { key: 'limpos', label: 'Mais limpos', kind: 'clean', about: 'Menos incidentes (contatos relatados pelo jogo) por corrida.' },
  { key: 'ativos', label: 'Mais ativos', kind: 'active', about: 'Quem mais correu no período.' },
  { key: 'carros', label: 'Carros', about: 'Os carros que mais vencem na classe, com taxa de vitória e pódios.' },
]

const PERIODS = [
  { key: '7d', label: '7 dias' },
  { key: '30d', label: '30 dias' },
  { key: 'all', label: 'Tudo' },
]

const ALL_CLASSES = [{ key: '', label: 'Todas' }, ...CLASS_TABS]

type SetParams = (next: Record<string, string>) => void

/** Rankings em abas: voltas, DR, SR, vitórias, em alta, mais limpos, mais ativos e carros (tudo na URL). */
export function RankingsPage() {
  const [params, setParams] = useSearchParams()
  const tab = TABS.find((t) => t.key === params.get('aba')) ?? TABS[0]
  const tabsRef = useRef<HTMLDivElement>(null)

  // No celular a barra de abas rola: mantém a aba aberta à vista (também ao chegar por link direto).
  useEffect(() => {
    const bar = tabsRef.current
    const active = bar?.querySelector<HTMLElement>('[aria-selected="true"]')
    if (bar && active) bar.scrollTo({ left: active.offsetLeft - (bar.clientWidth - active.offsetWidth) / 2, behavior: 'smooth' })
  }, [tab.key])

  const set: SetParams = (next) => {
    const p = new URLSearchParams(params)
    for (const [k, v] of Object.entries(next)) { if (v) p.set(k, v); else p.delete(k) }
    if ('classe' in next) p.delete('carro') // carro de outra classe não faz sentido
    setParams(p, { replace: true })
  }
  // Trocar de aba mantém só o que vale em todas (classe e período).
  const openTab = (key: TabKey) => {
    const p = new URLSearchParams()
    if (key !== 'voltas') p.set('aba', key)
    for (const k of ['classe', 'periodo']) { const v = params.get(k); if (v) p.set(k, v) }
    setParams(p, { replace: true })
  }

  return (
    <>
      <TopBar />
      <div className="wrap">
        <div className="page-head">
          <span className="eyebrow">Rankings</span>
          <h1>{tab.label}</h1>
          <p className="note">{tab.about}</p>
        </div>

        <div ref={tabsRef} className="tabs-line rk-tabs" role="tablist" aria-label="Rankings">
          {TABS.map((t) => (
            <button key={t.key} type="button" role="tab" aria-selected={t.key === tab.key} onClick={() => openTab(t.key)}>{t.label}</button>
          ))}
        </div>

        {tab.key === 'voltas' && <LapsPanel params={params} set={set} />}
        {tab.kind && <DriverRankingPanel key={tab.kind} kind={tab.kind} params={params} set={set} />}
        {tab.key === 'carros' && <CarsPanel params={params} set={set} />}
      </div>
    </>
  )
}

function Chips({ label, items, value, onPick, classes = false }: {
  label: string; items: { key: string; label: string }[]; value: string; onPick: (k: string) => void; classes?: boolean
}) {
  return (
    <div className="chips" role="tablist" aria-label={label}>
      {items.map((c) => (
        <button key={c.key || 'all'} type="button" role="tab" aria-selected={value === c.key}
          className={classes && c.key ? `chip chip-cls cls-${c.key}` : 'chip'} onClick={() => onPick(c.key)}>
          {classes && c.key && <span className="cls-dot" aria-hidden="true" />}{c.label}
        </button>
      ))}
    </div>
  )
}

// ---------------------------------------------------------------- voltas

function LapsPanel({ params, set }: { params: URLSearchParams; set: SetParams }) {
  const tracks = useApi<SearchTrack[]>('/lmu/tracks')
  const cars = useApi<CarListItem[]>('/lmu/cars')
  const track = params.get('pista') ?? tracks.data?.[0]?.track ?? ''
  const cls = params.get('classe') || 'gt3'
  const setup = params.get('setup') ?? ''
  const car = params.get('carro') ?? ''

  const q = new URLSearchParams({ take: '50' })
  if (track) q.set('track', track)
  if (cls) q.set('class', cls)
  if (setup) q.set('setup', setup)
  if (car) q.set('car', car)
  const lb = useApi<Leaderboard>(track ? `/lmu/leaderboard?${q}` : null)
  const carsOfClass = (cars.data ?? []).filter((c) => !cls || classShort(c.carClass).toLowerCase() === cls)

  return (
    <div className="rk-panel">
      <div className="filters">
        <label className="filter">
          <span>Pista</span>
          <select value={track} onChange={(e) => set({ pista: e.target.value })}>
            {(tracks.data ?? []).map((t) => <option key={t.track} value={t.track}>{t.track}</option>)}
          </select>
        </label>
        <label className="filter">
          <span>Carro</span>
          <select value={car} onChange={(e) => set({ carro: e.target.value })}>
            <option value="">Todos da classe</option>
            {carsOfClass.map((c) => <option key={c.car} value={c.car}>{c.car}</option>)}
          </select>
        </label>
      </div>
      <Chips label="Classe" items={CLASS_TABS} value={cls} onPick={(k) => set({ classe: k })} classes />
      <Chips label="Setup" items={SETUP_TABS} value={setup} onPick={(k) => set({ setup: k })} />
      <LeaderboardTable data={lb.data} loading={lb.loading || tracks.loading} />
    </div>
  )
}

// ---------------------------------------------------------------- pilotos

const signed = (n: number | null) => (n === null ? '—' : `${n > 0 ? '+' : ''}${n.toLocaleString('pt-BR')}`)
const dec2 = (n: number | null) => (n ?? 0).toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

function RankCell({ title, tier, progress }: { title: string | null; tier: number | null; progress: number | null }) {
  return (
    <span className="rk-cell">
      <Rank title={title} tier={tier} />
      {title && progress !== null && (
        <span className="rk-prog" title={`${progress}% do tier`}><span style={{ width: `${Math.max(0, Math.min(100, progress))}%` }} /></span>
      )}
    </span>
  )
}

/** Colunas de cada aba: cabeçalho e célula. A primeira é sempre a métrica principal (destacada). */
function columnsFor(kind: DriverRankingKind): { h: string; num?: boolean; cell: (r: RankingRow) => ReactNode }[] {
  const dr = { h: 'Driver Rank', cell: (r: RankingRow) => <RankCell title={r.driverRankTitle} tier={r.driverRankTier} progress={r.driverRankProgress} /> }
  const sr = { h: 'Safety Rank', cell: (r: RankingRow) => <RankCell title={r.safetyRankTitle} tier={r.safetyRankTier} progress={r.safetyRankProgress} /> }
  const starts = { h: 'Corridas', num: true, cell: (r: RankingRow) => r.starts.toLocaleString('pt-BR') }
  const wins = { h: 'Vitórias', num: true, cell: (r: RankingRow) => r.wins.toLocaleString('pt-BR') }
  switch (kind) {
    case 'dr': return [dr, sr, starts, wins]
    case 'sr': return [sr, dr, starts, wins]
    case 'wins': return [
      wins,
      { h: 'Pódios', num: true, cell: (r) => r.podiums.toLocaleString('pt-BR') },
      starts,
      { h: '% vitórias', num: true, cell: (r) => fmtPct(r.extra) },
      dr,
    ]
    case 'poles': return [
      { h: 'Poles', num: true, cell: (r) => r.value.toLocaleString('pt-BR') },
      starts,
      { h: '% poles', num: true, cell: (r) => fmtPct(r.extra) },
      wins, dr,
    ]
    case 'gains': return [
      { h: 'Posições ganhas', num: true, cell: (r) => <span className={r.value < 0 ? 'down' : 'up'}>{signed(r.value)}</span> },
      { h: 'Por corrida', num: true, cell: (r) => signed(r.extra === null ? null : Math.round(r.extra * 10) / 10) },
      starts, dr,
    ]
    case 'risers': return [
      { h: 'DR ganho', num: true, cell: (r) => <span className="up">{signed(r.value)}</span> },
      { h: 'SR ganho', num: true, cell: (r) => <span className={r.extra !== null && r.extra < 0 ? 'down' : 'up'}>{signed(r.extra)}</span> },
      starts, dr,
    ]
    case 'clean': return [
      { h: 'Incidentes/corrida', num: true, cell: (r) => dec2(r.value) },
      { h: 'Penalidades/corrida', num: true, cell: (r) => dec2(r.extra) },
      starts, sr,
    ]
    case 'active': return [
      starts,
      { h: 'Voltas', num: true, cell: (r) => (r.extra ?? 0).toLocaleString('pt-BR') },
      wins, dr,
    ]
  }
}

function DriverRankingPanel({ kind, params, set }: { kind: DriverRankingKind; params: URLSearchParams; set: SetParams }) {
  const { user } = useAuth()
  const { isFollowing } = useFollows()
  const rankOnly = kind === 'dr' || kind === 'sr'
  const period = params.get('periodo') ?? '30d'
  const cls = rankOnly ? '' : (params.get('classe') ?? '')
  const me = user?.driver?.driverId ?? null

  const q = new URLSearchParams({ period, take: '50' })
  if (cls) q.set('class', cls)
  if (me) q.set('me', me)
  const r = useApi<Ranking>(`/lmu/rankings/drivers/${kind}?${q}`)
  const cols = columnsFor(kind)
  const data = r.data
  const meOutside = data?.me && !data.rows.some((x) => x.rank === data.me!.rank) ? data.me : null

  const row = (x: RankingRow) => {
    const isMe = me !== null && x.driverId === me
    return (
      <tr key={`${x.rank}-${x.driverId ?? x.name}`} className={isMe ? 'is-me' : isFollowing(x.driverId) ? 'is-followed' : undefined}>
        <td className={`num pos-cell${x.rank <= 3 ? ` lb-top lb-${x.rank}` : ''}`}>{x.rank.toLocaleString('pt-BR')}</td>
        <td>
          {x.driverId ? <Link to={`/pilotos/${x.driverId}`}>{x.name}</Link> : x.name}
          {x.nationality && <span className="pill nat">{x.nationality}</span>}
          {isMe && <span className="pill me">você</span>}
        </td>
        {cols.map((c, i) => <td key={c.h} className={[c.num ? 'num' : '', i === 0 ? 'rk-main' : ''].join(' ').trim() || undefined}>{c.cell(x)}</td>)}
      </tr>
    )
  }

  const periodItems = rankOnly
    ? PERIODS.map((p) => ({ ...p, label: p.key === 'all' ? 'Todos os pilotos' : `Ativos em ${p.label}` }))
    : PERIODS

  return (
    <div className="rk-panel">
      <Chips label={rankOnly ? 'Pilotos vistos em' : 'Período'} items={periodItems} value={period} onPick={(k) => set({ periodo: k === '30d' ? '' : k })} />
      {!rankOnly && <Chips label="Classe" items={ALL_CLASSES} value={cls} onPick={(k) => set({ classe: k })} classes />}

      {data?.me && (
        <div className="rk-me">
          <span className="rk-me-pos">#{data.me.rank.toLocaleString('pt-BR')}</span>
          <span>é a sua posição entre <b>{data.total.toLocaleString('pt-BR')}</b> pilotos neste ranking.</span>
        </div>
      )}

      {r.loading && !data ? <RowsSkeleton rows={8} /> : r.error ? <ErrorBox message={r.error} onRetry={r.reload} /> : !data?.rows.length ? (
        <EmptyState title="Ninguém neste recorte ainda">Troque o período ou a classe — ou volte depois das próximas corridas.</EmptyState>
      ) : (
        <>
          <div className="tbl-wrap">
            <table className="lb rk-table">
              <thead>
                <tr><th className="num">#</th><th>Piloto</th>{cols.map((c) => <th key={c.h} className={c.num ? 'num' : undefined}>{c.h}</th>)}</tr>
              </thead>
              <tbody>
                {data.rows.map(row)}
                {meOutside && <><tr className="rk-gap" aria-hidden="true"><td colSpan={cols.length + 2}>⋯</td></tr>{row(meOutside)}</>}
              </tbody>
            </table>
          </div>
          <p className="note">
            {data.total.toLocaleString('pt-BR')} pilotos neste recorte
            {data.minStarts > 0 && <> · mínimo de {data.minStarts} corridas no período</>}
            {rankOnly && <> · rank do jogo como visto na última corrida de cada piloto</>}
            {kind === 'wins' && <> · vitória = P1 da classe no split</>}
          </p>
        </>
      )}
    </div>
  )
}

// ---------------------------------------------------------------- carros

function CarsPanel({ params, set }: { params: URLSearchParams; set: SetParams }) {
  const period = params.get('periodo') ?? '30d'
  const cls = params.get('classe') ?? ''
  const q = new URLSearchParams({ period })
  if (cls) q.set('class', cls)
  const r = useApi<CarRanking>(`/lmu/rankings/cars?${q}`)
  const data = r.data

  return (
    <div className="rk-panel">
      <Chips label="Período" items={PERIODS} value={period} onPick={(k) => set({ periodo: k === '30d' ? '' : k })} />
      <Chips label="Classe" items={ALL_CLASSES} value={cls} onPick={(k) => set({ classe: k })} classes />
      {r.loading && !data ? <RowsSkeleton rows={8} /> : r.error ? <ErrorBox message={r.error} onRetry={r.reload} /> : !data?.rows.length ? (
        <EmptyState title="Nenhum carro neste recorte ainda" />
      ) : (
        <>
          <div className="tbl-wrap">
            <table className="lb rk-table rk-table-cars">
              <thead>
                <tr>
                  <th className="num">#</th><th>Carro</th><th className="num">Vitórias</th><th className="num">% vitórias</th>
                  <th className="num">Pódios</th><th className="num">Largadas</th><th className="num">Pilotos</th><th className="num">Chegada média</th>
                </tr>
              </thead>
              <tbody>
                {data.rows.map((c) => (
                  <tr key={c.car}>
                    <td className={`num pos-cell${c.rank <= 3 ? ` lb-top lb-${c.rank}` : ''}`}>{c.rank}</td>
                    <td className="car"><Link to={`/carros/${encodeURIComponent(c.car)}`}>{c.car}</Link> <ClassChip cls={c.carClass} /></td>
                    <td className="num rk-main">{c.wins.toLocaleString('pt-BR')}</td>
                    <td className="num">{fmtPct(c.winRate)}</td>
                    <td className="num">{c.podiums.toLocaleString('pt-BR')}</td>
                    <td className="num">{c.starts.toLocaleString('pt-BR')}</td>
                    <td className="num">{c.drivers.toLocaleString('pt-BR')}</td>
                    <td className="num">{c.avgClassFinish === null ? '—' : `P${c.avgClassFinish.toLocaleString('pt-BR')}`}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p className="note">Vitória = P1 da classe no split · carros com pelo menos {data.minStarts} largadas no período · chegada média na classe, só de quem terminou.</p>
        </>
      )}
    </div>
  )
}
