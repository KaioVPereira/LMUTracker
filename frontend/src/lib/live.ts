import { useEffect, useState } from 'react'
import type { EventInfo, LiveSpot, UpcomingRace } from '../api/types'
import { fmtGameTime, isWet, raceSession, skyLabel } from './eventPhase'
import { parseUtc } from './format'

// Utilitários das corridas ao vivo (componentes em components/LiveEvent.tsx).

const CLOCK_MS = 30_000

/** Relógio de estado: "Ao vivo" e "em X min" mudam com ele, não a cada render. */
export function useNow(every = CLOCK_MS): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), every)
    return () => window.clearInterval(id)
  }, [every])
  return now
}

export const isLive = (r: UpcomingRace, now: number) =>
  parseUtc(r.startsAt).getTime() <= now && now < parseUtc(r.endsAt).getTime()

/** "15:00 no jogo · 25 °C · céu limpo" — condições da largada da corrida. */
export function conditions(info: EventInfo | null): string | null {
  const s = raceSession(info)
  if (!s) return null
  const parts = [`${fmtGameTime(s.timeOfDay)} no jogo`]
  if (s.temperatureC !== null) parts.push(`${s.temperatureC} °C`)
  if (s.rainChance) parts.push(`chuva ${s.rainChance}%`)
  else if (isWet(s.sky)) parts.push(skyLabel(s.sky)!.toLowerCase())
  return parts.join(' · ')
}

/** Link para a página do evento em andamento, já procurando o piloto. */
export const liveHref = (spot: LiveSpot, name: string) => `/ao-vivo/${spot.eventId}?q=${encodeURIComponent(name)}`

