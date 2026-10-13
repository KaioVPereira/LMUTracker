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

/** Uma sessão do evento: minutos; horário no jogo em minutos desde 0h; céu 0 (limpo) … 10 (tempestade). */
export interface SessionInfo {
  kind: 'practice' | 'qualifying' | 'warmup' | 'race' | string
  minutes: number
  waitMinutes: number
  timeOfDay: number
  sky: number | null
  temperatureC: number | null
  rainChance: number | null
}

export interface EventInfo {
  sessions: SessionInfo[]
  fixedSetup: boolean | null
  maxPlayers: number | null
  tyreWear?: number | null
  fuelUse?: number | null
  damage?: number | null
  tireSets?: number | null
  trackLimits?: 'off' | 'default' | 'strict' | 'relaxed' | null
  tyreWarmers?: boolean | null
}

export interface UpcomingRace {
  eventId: string
  title: string | null
  track: string | null
  tier: string | null
  eventType: string
  startsAt: string
  endsAt: string
  /** Só para corridas vistas na agenda a partir do scanner v3. */
  info: EventInfo | null
}

export interface RecentRace {
  eventId: string
  title: string | null
  track: string | null
  tier: string | null
  startsAt: string
  splits: number
  drivers: number
  /** "Hyper,GT3" — classes que correram. */
  classes: string | null
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
  classes: string | null
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
  classes: string | null
  splits: SplitSummary[]
  info: EventInfo | null
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
  incidents: number | null
  penalties: number | null
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

// ---------- busca global ----------

export interface SearchDriver {
  driverId: string
  name: string
  nationality: string | null
  driverRankTitle: string | null
  driverRankTier: number | null
  races: number
}

export interface SearchTrack { track: string; races: number }
export interface SearchCar { car: string; carClass: string | null; starts: number }

export interface SearchRace {
  eventId: string
  title: string | null
  track: string | null
  tier: string | null
  startsAt: string
  classes: string | null
}

export interface SearchResults {
  query: string
  drivers: SearchDriver[]
  tracks: SearchTrack[]
  cars: SearchCar[]
  races: SearchRace[]
}

// ---------- tela inicial ----------

export interface LapRecord {
  track: string
  carClass: string | null
  fixedSetup: boolean
  lapTime: number
  driverId: string | null
  driverName: string
  nationality: string | null
  car: string | null
  eventId: string
  setAt: string
  splitNo: number
}

export interface TopSplitWinner {
  eventId: string
  title: string | null
  track: string | null
  tier: string | null
  startsAt: string
  carClass: string | null
  driverId: string | null
  driverName: string
  nationality: string | null
  car: string | null
  laps: number
  splitDrivers: number
  gapToSecond: number | null
}

export interface RankClimber {
  driverId: string | null
  name: string
  nationality: string | null
  races: number
  driverRankGain: number
  fromTitle: string | null
  fromTier: number | null
  fromProgress: number | null
  toTitle: string | null
  toTier: number | null
  toProgress: number | null
}

export interface CloseFinish {
  eventId: string
  title: string | null
  track: string | null
  startsAt: string
  carClass: string | null
  gap: number
  winnerName: string
  winnerId: string | null
  secondName: string
  secondId: string | null
}

export interface ActiveDriver { driverId: string | null; name: string; nationality: string | null; races: number }
export interface ClassShare { carClass: string; starts: number }

export interface Home {
  records: LapRecord[]
  winners: TopSplitWinner[]
  climbers: RankClimber[]
  closeFinishes: CloseFinish[]
  mostActive: ActiveDriver[]
  classShare: ClassShare[]
  generatedAt: string
}

// ---------- perfil: estatísticas e comparação ----------

/** Rank numa escala contínua: Bronze 1 = 0…100, Bronze 2 = 100…200, Bronze 3 = 200…300, Silver 1 = 300… */
export interface RankPoint {
  at: string
  eventId: string
  title: string | null
  track: string | null
  driverRankTitle: string | null
  driverRankTier: number | null
  driverRankProgress: number | null
  driverScore: number | null
  driverRankAdjustment: number | null
  safetyRankTitle: string | null
  safetyRankTier: number | null
  safetyRankProgress: number | null
  safetyScore: number | null
  safetyRankAdjustment: number | null
}

export interface ProfileSummary {
  races: number
  wins: number
  podiums: number
  top5: number
  poles: number
  dnfs: number
  finished: number
  avgPositionsGained: number | null
  avgClassFinish: number | null
  avgQualiClassPosition: number | null
  racesWithIncidents: number
  incidentsPerRace: number | null
  penalties: number
  cleanRaces: number
  lapsCompleted: number
  /** Variação típica do ritmo numa corrida, em % (menor = mais constante). */
  paceConsistency: number | null
  topSplitRaces: number
}

export interface TrackStat {
  track: string
  carClass: string | null
  races: number
  wins: number
  podiums: number
  avgClassFinish: number | null
  bestClassFinish: number | null
  bestLap: number | null
  incidentsPerRace: number | null
}

export interface CarStat {
  car: string
  carClass: string | null
  races: number
  wins: number
  podiums: number
  dnfs: number
  avgClassFinish: number | null
  bestClassFinish: number | null
}

export interface Rival {
  driverId: string | null
  name: string
  nationality: string | null
  sharedRaces: number
  aheadOfThem: number
  behindThem: number
  lastRaced: string
}

export interface Achievement {
  key: string
  title: string
  description: string
  achieved: boolean
  at: string | null
  eventId: string | null
  progress: number | null
  goal: number | null
}

export interface DriverProfileStats {
  driverId: string
  summary: ProfileSummary
  rankHistory: RankPoint[]
  tracks: TrackStat[]
  cars: CarStat[]
  rivals: Rival[]
  achievements: Achievement[]
  generatedAt: string
}

export interface HeadToHeadSide {
  driverId: string
  name: string
  nationality: string | null
  driverRankTitle: string | null
  driverRankTier: number | null
  safetyRankTitle: string | null
  safetyRankTier: number | null
  races: number
  wins: number
  podiums: number
}

export interface HeadToHeadRace {
  eventId: string
  title: string | null
  track: string | null
  startsAt: string
  splitNo: number
  myClass: string | null
  myPosition: number | null
  myClassPosition: number | null
  myBestLap: number | null
  myStatus: string | null
  theirClass: string | null
  theirPosition: number | null
  theirClassPosition: number | null
  theirBestLap: number | null
  theirStatus: string | null
}

export interface HeadToHead {
  me: HeadToHeadSide
  them: HeadToHeadSide
  shared: number
  meAhead: number
  themAhead: number
  sameClassRaces: number
  avgBestLapGap: number | null
  races: HeadToHeadRace[]
}

// ---------- detalhe da corrida ----------

export interface SplitLap {
  lap: number
  position: number | null
  classPosition: number | null
  lapTime: number | null
  elapsed: number | null
  pit: boolean
  compound: string | null
  fuel: number | null
}

export interface SplitDriverLaps {
  resultId: string
  driverId: string | null
  name: string
  carClass: string | null
  carNumber: string | null
  car: string | null
  gridPos: number | null
  classGridPos: number | null
  position: number | null
  classPosition: number | null
  finishStatus: string | null
  bestLap: number | null
  laps: SplitLap[]
}

export interface SplitLaps { eventId: string; splitNo: number; mostLaps: number; drivers: SplitDriverLaps[] }

export interface SplitEvent {
  at: number | null
  kind: 'penalty' | 'served' | 'steward' | string
  driver: string | null
  opponent: string | null
  detail: string | null
  lap: number | null
}

export interface SplitEvents { eventId: string; splitNo: number; available: boolean; events: SplitEvent[] }
