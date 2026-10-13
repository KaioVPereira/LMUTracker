// Datas da Api vêm em UTC. Se alguma vier sem fuso, tratamos como UTC (nunca como hora local).
export function parseUtc(value: string): Date {
  return new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(value) ? value : `${value}Z`)
}

const dateTime = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })
const timeOnly = new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit' })
const dayOnly = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', year: 'numeric' })

export const fmtDateTime = (v: string) => dateTime.format(parseUtc(v))
export const fmtTime = (v: string) => timeOnly.format(parseUtc(v))
export const fmtDay = (v: string) => dayOnly.format(parseUtc(v))

/** "em 12 min", "há 3 h", "agora". */
export function fmtRelative(v: string, now = Date.now()): string {
  const min = Math.round((parseUtc(v).getTime() - now) / 60000)
  if (Math.abs(min) < 1) return 'agora'
  const abs = Math.abs(min)
  const span = abs < 60 ? `${abs} min` : abs < 1440 ? `${Math.round(abs / 60)} h` : `${Math.round(abs / 1440)} d`
  return min > 0 ? `em ${span}` : `há ${span}`
}

/** 91.5928 -> "1:31.593" */
export function fmtLap(seconds: number | null | undefined): string {
  if (!seconds) return '—'
  const m = Math.floor(seconds / 60)
  const s = seconds - m * 60
  return `${m}:${s.toFixed(3).padStart(6, '0')}`
}

export function fmtDelta(n: number | null | undefined): { text: string; cls: string } {
  if (n === null || n === undefined) return { text: '—', cls: 'flat' }
  if (n > 0) return { text: `+${n}`, cls: 'up' }
  if (n < 0) return { text: `${n}`, cls: 'down' }
  return { text: '±0', cls: 'flat' }
}

export const fmtInt = (n: number) => n.toLocaleString('pt-BR')

const TIERS: Record<string, string> = { beginner: 'Iniciante', intermediate: 'Intermediário', advanced: 'Avançado' }
export const tierLabel = (t: string | null) => (t ? (TIERS[t] ?? t) : '—')

const TYPES: Record<string, string> = { daily: 'Diária', weekly: 'Semanal', special: 'Especial', championship: 'Campeonato', hosted: 'Hospedada' }
export const typeLabel = (t: string) => TYPES[t] ?? t

const BADGES: Record<string, string> = {
  'sr-noob': 'SR Novato',
  'sr-rookie': 'SR Rookie',
  'sr-probation': 'SR Em observação',
  'sr-warning': 'SR Alerta',
  'sr-danger': 'SR Perigo',
  'sr-clean': 'SR Limpo',
  'sr-saint': 'SR Exemplar',
  s397: 'Studio 397',
  'irl-driver': 'Piloto real',
  'content-creator': 'Criador de conteúdo',
  'early-access': 'Early access',
  'test-driver': 'Piloto de testes',
}
export const badgeLabel = (b: string | null) => (b ? (BADGES[b] ?? b) : null)

export const classLabel = (c: string | null) => (c ? c.replace(/_/g, ' ') : '—')

const RANKS = ['bronze', 'silver', 'gold', 'platinum']
export const rankClass = (r: string | null) => (r && RANKS.includes(r.toLowerCase()) ? `rk-${r.toLowerCase()}` : '')

export function initials(name: string): string {
  const parts = name.trim().split(/\s+/)
  return ((parts[0]?.[0] ?? '') + (parts.length > 1 ? (parts[parts.length - 1][0] ?? '') : '')).toUpperCase() || '?'
}
