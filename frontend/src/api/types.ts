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
  resultId: string
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
  driverId: string | null
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

// ---------- corridas, splits, piloto na corrida, perfil ----------

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}

export interface RaceListItem {
  eventId: string
  title: string | null
  track: string | null
  tier: string | null
  eventType: string
  startsAt: string
  status: string
  splits: number
  drivers: number
}

export interface SplitSummary {
  splitNo: number
  drivers: number
  mostLapsCompleted: number
  winnerName: string | null
  fastestLap: number | null
}

export interface RaceDetail {
  eventId: string
  title: string | null
  track: string | null
  tier: string | null
  eventType: string
  startsAt: string
  raceEndsAt: string
  status: 'Pending' | 'Ready' | 'Fetched' | 'Failed' | string
  totalSplits: number | null
  splits: SplitSummary[]
}

export interface ResultRow {
  resultId: string
  driverId: string | null
  name: string
  nationality: string | null
  badge: string | null
  carClass: string | null
  carNumber: string | null
  vehName: string | null
  teamName: string | null
  gridPos: number | null
  classGridPos: number | null
  position: number | null
  classPosition: number | null
  laps: number
  bestLapTime: number | null
  finishTime: number | null
  finishStatus: string | null
  pitStops: number
  qualiPosition: number | null
  qualiClassPosition: number | null
  qualiBestLapTime: number | null
  driverRankTitle: string | null
  driverRankTier: number | null
  safetyRankTitle: string | null
  safetyRankTier: number | null
  driverRankAdjustment: number | null
  safetyRankAdjustment: number | null
  hasLaps: boolean
}

export interface SplitResults {
  eventId: string
  splitNo: number
  rows: ResultRow[]
}

export interface Lap {
  lap: number
  position: number | null
  lapTime: number | null
  s1: number | null
  s2: number | null
  s3: number | null
  elapsed: number | null
  fuel: number | null
  frontCompound: string | null
  rearCompound: string | null
  wearFL: number | null
  wearFR: number | null
  wearRL: number | null
  wearRR: number | null
  pit: boolean
}

export interface ResultDetail {
  eventId: string
  title: string | null
  track: string | null
  tier: string | null
  eventType: string
  startsAt: string
  splitNo: number
  splitDrivers: number
  classFastestLap: number | null
  row: ResultRow
  laps: Lap[]
}

export interface DriverRace {
  resultId: string
  eventId: string
  title: string | null
  track: string | null
  tier: string | null
  eventType: string
  startsAt: string
  splitNo: number
  carClass: string | null
  vehName: string | null
  classGridPos: number | null
  classPosition: number | null
  position: number | null
  bestLapTime: number | null
  finishStatus: string | null
  driverRankTitle: string | null
  driverRankTier: number | null
  safetyRankTitle: string | null
  safetyRankTier: number | null
  driverRankAdjustment: number | null
  safetyRankAdjustment: number | null
}

export interface DriverStats {
  races: number
  wins: number
  podiums: number
  top5: number
  poles: number
  dnfs: number
  bestFinish: number | null
  lapsCompleted: number
}

export interface DriverProfile {
  driverId: string
  name: string
  nationality: string | null
  badge: string | null
  driverRankTitle: string | null
  driverRankTier: number | null
  driverRankProgress: number | null
  safetyRankTitle: string | null
  safetyRankTier: number | null
  safetyRankProgress: number | null
  lastSeenAt: string
  isMe: boolean
  stats: DriverStats
  races: Paged<DriverRace>
}
