import type { CrewStint, SplitDriverLaps } from '../api/types'

// Carro dividido (corrida de equipe com troca de piloto): a linha de resultado é do carro e leva o nome de quem
// terminou; os turnos (crew) dizem quem guiou cada trecho.

/** Os pilotos do carro, na ordem em que apareceram, sem repetir. */
export function crewMembers(crew: CrewStint[] | null | undefined): { name: string; driverId: string | null }[] {
  const seen = new Set<string>()
  const out: { name: string; driverId: string | null }[] = []
  for (const s of crew ?? []) {
    const k = s.name.toLowerCase()
    if (seen.has(k)) continue
    seen.add(k)
    out.push({ name: s.name, driverId: s.driverId })
  }
  return out
}

/** Quem guiou a volta (null em corrida solo). */
export const driverOfLap = (crew: CrewStint[] | null | undefined, lap: number) =>
  crew?.find((s) => lap >= s.startLap && lap <= s.endLap)?.name ?? null

/** Rótulo do carro nos gráficos: "Ana / Bia" quando dividido. */
export const carLabel = (d: SplitDriverLaps) => {
  const m = crewMembers(d.crew)
  return m.length > 1 ? m.map((x) => x.name).join(' / ') : d.name
}
