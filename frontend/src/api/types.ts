// Espelho dos DTOs da Api (.NET serializa em camelCase).

export interface UserDto {
  id: string
  username: string
  displayName: string
  email: string
  steamId: string | null
  linkedAt: string | null
  createdAt: string
}

export interface AuthResponse {
  accessToken: string
  accessTokenExpiresAt: string
  user: UserDto
}

export interface UpcomingRace {
  eventId: string
  title: string | null
  track: string | null
  tier: string | null
  eventType: string
  startsAt: string
  endsAt: string
}

export interface RecentRace {
  eventId: string
  title: string | null
  track: string | null
  tier: string | null
  startsAt: string
  splits: number
  drivers: number
}

export interface Overview {
  eventsTracked: number
  racesFetched: number
  resultsStored: number
  driversIndexed: number
  lastActivityAt: string | null
  upcoming: UpcomingRace[]
  recent: RecentRace[]
}

export interface MyResult {
  eventId: string
  title: string | null
  track: string | null
  tier: string | null
  startsAt: string
  splitNo: number
  carClass: string | null
  vehName: string | null
  gridPos: number | null
  classGridPos: number | null
  position: number | null
  classPosition: number | null
  laps: number
  bestLapTime: number | null
  finishStatus: string | null
  driverRankAdjustment: number | null
  safetyRankAdjustment: number | null
}

export interface MyDriver {
  linked: boolean
  found: boolean
  steamId: string | null
  name: string | null
  nationality: string | null
  badge: string | null
  driverRankTitle: string | null
  driverRankTier: number | null
  safetyRankTitle: string | null
  safetyRankTier: number | null
  lastSeenAt: string | null
  racesFound: number
  recent: MyResult[]
}
