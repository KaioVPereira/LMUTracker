import type { SplitLap } from '../api/types'

/**
 * Ritmo de corrida: a mediana das voltas limpas (sem a 1ª, sem pit e até 107% da melhor do piloto — tira rodada,
 * bandeira amarela e tráfego pesado). Null com menos de 3 voltas limpas.
 */
export function cleanPace(laps: SplitLap[]): number | null {
  const times = laps.filter((l) => l.lap > 1 && !l.pit && l.lapTime).map((l) => l.lapTime!)
  if (times.length < 3) return null
  const best = Math.min(...times)
  const clean = times.filter((t) => t <= best * 1.07).sort((a, b) => a - b)
  if (clean.length < 3) return null
  const m = clean.length >> 1
  return clean.length % 2 ? clean[m] : (clean[m - 1] + clean[m]) / 2
}

/** "+0.412" / "−0.120" (s), para diferenças de tempo. */
export const fmtDiff = (v: number | null | undefined, digits = 3) =>
  v === null || v === undefined ? '—' : `${v > 0 ? '+' : v < 0 ? '−' : '±'}${Math.abs(v).toFixed(digits)}`
