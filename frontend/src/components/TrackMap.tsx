import { TRACK_MAPS } from '../lib/trackMaps'

/** Chave do traçado: o nome exibido da pista (o mesmo que o scanner grava em ScanEvent.Track). */
const ALIASES: Record<string, string> = { Qatar: 'Lusail', 'Spa Francorchamps': 'Spa-Francorchamps', Spa: 'Spa-Francorchamps' }

/**
 * Traçado real da pista (OpenStreetMap, © OpenStreetMap contributors, ODbL). Sem traçado para a pista, não mostra
 * nada — nunca um desenho genérico no lugar.
 */
export function TrackMap({ track, className }: { track: string | null | undefined; className?: string }) {
  const t = track ? TRACK_MAPS[ALIASES[track] ?? track] : undefined
  if (!t) return null
  return (
    <figure className={`track-map ${className ?? ''}`}>
      <svg viewBox={t.viewBox} role="img" aria-label={`Traçado de ${track}`}>
        <path d={t.d} className="tm-casing" />
        <path d={t.d} className="tm-line" />
        <circle cx={t.start[0]} cy={t.start[1]} r={t.r * 1.6} className="tm-start" />
      </svg>
      <figcaption>© OpenStreetMap</figcaption>
    </figure>
  )
}
