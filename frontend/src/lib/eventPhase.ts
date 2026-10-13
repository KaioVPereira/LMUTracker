import type { EventInfo, SessionInfo, UpcomingRace } from '../api/types'
import { parseUtc } from './format'

// Linha do tempo de um evento a partir da configuração (sessões e duração) — é o "ao vivo" que dá para mostrar:
// o RaceOS não publica a cronometragem de outros pilotos durante a corrida, mas o horário de cada sessão é fixo.

export type PhaseKind = 'practice' | 'qualifying' | 'warmup' | 'grid' | 'race'

export interface Phase {
  kind: PhaseKind
  label: string
  start: number
  end: number
  minutes: number
}

const LABEL: Record<PhaseKind, string> = {
  practice: 'Treino', qualifying: 'Classificação', warmup: 'Warm-up', grid: 'Grid', race: 'Corrida',
}

/** Fases na ordem, com início e fim em ms. A corrida termina no fim previsto pelo RaceOS (endsAt). */
export function phasesOf(race: UpcomingRace): Phase[] {
  const sessions = race.info?.sessions ?? []
  if (!sessions.length) return []
  const MIN = 60_000
  let t = parseUtc(race.startsAt).getTime()
  const out: Phase[] = []
  for (const s of sessions) {
    const kind = (['practice', 'qualifying', 'warmup', 'race'].includes(s.kind) ? s.kind : null) as PhaseKind | null
    if (!kind) continue
    if (kind === 'race' && s.waitMinutes > 0) {
      out.push({ kind: 'grid', label: LABEL.grid, start: t, end: t + s.waitMinutes * MIN, minutes: s.waitMinutes })
      t += s.waitMinutes * MIN
    }
    const end = kind === 'race' ? Math.max(t + s.minutes * MIN, parseUtc(race.endsAt).getTime()) : t + s.minutes * MIN
    out.push({ kind, label: LABEL[kind], start: t, end, minutes: s.minutes })
    t = end
  }
  return out
}

/** Fase atual (índice) ou -1 antes do início / fases.length depois do fim. */
export function currentPhase(phases: Phase[], now: number): number {
  if (!phases.length || now < phases[0].start) return -1
  const i = phases.findIndex((p) => now >= p.start && now < p.end)
  return i >= 0 ? i : phases.length
}

/** Sessão da corrida (para clima e horário no jogo da largada). */
export const raceSession = (info: EventInfo | null): SessionInfo | null =>
  info?.sessions.find((s) => s.kind === 'race') ?? info?.sessions[info.sessions.length - 1] ?? null

const SKY = [
  'Céu limpo', 'Poucas nuvens', 'Parcialmente nublado', 'Nublado', 'Encoberto', 'Garoa', 'Chuva leve',
  'Chuva leve', 'Chuva', 'Chuva forte', 'Tempestade',
]
export const skyLabel = (sky: number | null) => (sky === null ? null : SKY[Math.min(SKY.length - 1, Math.max(0, sky))])
export const isWet = (sky: number | null) => sky !== null && sky >= 5

/** 870 -> "14:30" (horário no jogo). */
export function fmtGameTime(minutes: number): string {
  const m = Math.round(minutes) % 1440
  return `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`
}

/** "T 3 · Q 7 · C 20 min" — resumo compacto das sessões. */
export function sessionsSummary(info: EventInfo | null): string | null {
  if (!info?.sessions.length) return null
  const short: Record<string, string> = { practice: 'T', qualifying: 'Q', warmup: 'W', race: 'C' }
  return info.sessions.filter((s) => short[s.kind]).map((s) => `${short[s.kind]} ${Math.round(s.minutes)}`).join(' · ') + ' min'
}
