import { Link } from 'react-router-dom'
import type { Leaderboard } from '../api/types'
import { fmtDateTime, fmtLap } from '../lib/format'
import { useFollows } from '../lib/follows'
import { useLoadMore } from '../lib/useLoadMore'
import { ClassBarCell, ClassChip, EmptyState, MoreButton, RowsSkeleton } from './Bits'
import { Flag } from './Flag'

/**
 * Tabela de ranking: melhor volta de cada piloto, com a diferença para o 1º e a corrida em que foi feita.
 * Com `path` (o endpoint da 1ª página), mostra "Mostrar mais" e busca as páginas seguintes.
 */
export function LeaderboardTable({ data, loading, path = null, showTrack = false }: {
  data: Leaderboard | null; loading: boolean; path?: string | null; showTrack?: boolean
}) {
  const { isFollowing } = useFollows()
  const paged = useLoadMore(path, data, (d) => d.rows, (d) => d.drivers)
  if (loading && !data) return <RowsSkeleton rows={8} />
  if (!data || data.rows.length === 0) {
    return <EmptyState title="Ninguém neste recorte ainda">Troque a classe ou o tipo de setup — ou volte depois das próximas corridas.</EmptyState>
  }
  return (
    <>
      <div className="tbl-wrap">
        <table className="lb">
          <thead>
            <tr>
              <th aria-hidden="true"></th><th className="num">#</th><th>Piloto</th><th>Carro</th>
              <th className="num">Melhor volta</th><th className="num">Dif.</th><th>Quando</th>{showTrack && <th>Pista</th>}
            </tr>
          </thead>
          <tbody>
            {paged.rows.map((r) => (
              <tr key={`${r.rank}-${r.driverId ?? r.name}`} className={isFollowing(r.driverId) ? 'is-followed' : undefined}>
                <ClassBarCell cls={r.carClass} />
                <td className={`num pos-cell${r.rank <= 3 ? ` lb-top lb-${r.rank}` : ''}`}>{r.rank}</td>
                <td>
                  {r.driverId ? <Link to={`/pilotos/${r.driverId}`}>{r.name}</Link> : r.name}
                  <Flag code={r.nationality} />
                </td>
                <td className="car">{r.car ? <Link to={`/carros/${encodeURIComponent(r.car)}`} className="plain">{r.car}</Link> : '—'} <ClassChip cls={r.carClass} /></td>
                <td className={`num ${r.rank === 1 ? 'best' : ''}`}>{fmtLap(r.bestLap)}</td>
                <td className="num rel">{r.gapToFirst === null ? '—' : `+${r.gapToFirst.toFixed(3)}`}</td>
                <td><Link to={`/corridas/${r.eventId}?split=${r.splitNo}`} className="plain mono rel">{fmtDateTime(r.setAt)}</Link></td>
                {showTrack && <td>{data.track ?? '—'}</td>}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {path && paged.hasMore && <MoreButton shown={paged.rows.length} total={paged.total} loading={paged.loading} onClick={paged.loadMore} />}
      <p className="note">{data.drivers.toLocaleString('pt-BR')} pilotos neste recorte · uma linha por piloto, com a melhor volta dele em qualquer corrida registrada.</p>
    </>
  )
}
