import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import type { Paged, RaceListItem } from '../api/types'
import { ErrorBox, TierChip } from '../components/Bits'
import { Pager } from '../components/Pager'
import { TopBar } from '../components/TopBar'
import { fmtDateTime, fmtInt, typeLabel } from '../lib/format'
import { useApi } from '../lib/useApi'

const FILTERS = [
  { key: '', label: 'Todas' },
  { key: 'beginner', label: 'Iniciante' },
  { key: 'intermediate', label: 'Intermediário' },
  { key: 'advanced', label: 'Avançado' },
  { key: 'weekly', label: 'Semanais' },
]

export function RacesPage() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const tier = params.get('nivel') ?? ''
  const page = Math.max(1, Number(params.get('pagina') ?? 1))
  const { data, loading, error } = useApi<Paged<RaceListItem>>(
    `/lmu/races?page=${page}${tier ? `&tier=${encodeURIComponent(tier)}` : ''}`)

  function go(next: { nivel?: string; pagina?: number }) {
    const p = new URLSearchParams()
    const n = next.nivel ?? tier
    if (n) p.set('nivel', n)
    if ((next.pagina ?? 1) > 1) p.set('pagina', String(next.pagina))
    setParams(p)
  }

  return (
    <>
      <TopBar />
      <div className="wrap">
        <div className="page-head">
          <span className="eyebrow">Resultados</span>
          <h1>Corridas</h1>
          <p className="note">Corridas oficiais já encerradas que o scanner guardou. Clique para ver os splits.</p>
        </div>

        <div className="chips" role="tablist" aria-label="Nível">
          {FILTERS.map((f) => (
            <button key={f.key} type="button" role="tab" aria-selected={tier === f.key}
              className="chip" onClick={() => go({ nivel: f.key, pagina: 1 })}>
              {f.label}
            </button>
          ))}
        </div>

        {error && <ErrorBox message={error} />}
        <div className="tbl-wrap">
          <table>
            <thead>
              <tr><th>Largada</th><th>Evento</th><th>Pista</th><th>Nível</th><th>Tipo</th><th className="num">Splits</th><th className="num">Pilotos</th></tr>
            </thead>
            <tbody>
              {data?.items.length ? data.items.map((r) => (
                <tr key={r.eventId} className="row-link" onClick={() => navigate(`/corridas/${r.eventId}`)}>
                  <td className="mono">{fmtDateTime(r.startsAt)}</td>
                  <td><Link to={`/corridas/${r.eventId}`} onClick={(e) => e.stopPropagation()}>{r.title ?? 'Corrida'}</Link></td>
                  <td>{r.track ?? '—'}</td>
                  <td><TierChip tier={r.tier} /></td>
                  <td>{typeLabel(r.eventType)}</td>
                  <td className="num">{r.splits}</td>
                  <td className="num">{fmtInt(r.drivers)}</td>
                </tr>
              )) : (
                <tr><td colSpan={7} className="empty">
                  {loading ? 'Carregando…' : 'Nenhuma corrida com resultado ainda neste filtro.'}
                </td></tr>
              )}
            </tbody>
          </table>
        </div>
        {data && <Pager page={data.page} pageSize={data.pageSize} total={data.total} onPage={(p) => go({ pagina: p })} />}
      </div>
    </>
  )
}
