// Classes de carro: uma cor por classe, igual em todo o site (tokens --c-* no index.css).
// O RaceOS manda nomes variados para a mesma classe (LMP2 / LMP2_ELMS, GT3 / LMGT3).

export type ClassKey = 'hyper' | 'lmp2' | 'lmp3' | 'gt3' | 'other'

export function classKey(raw: string | null | undefined): ClassKey {
  const c = (raw ?? '').toUpperCase()
  if (c.startsWith('HYPER') || c === 'LMH' || c === 'LMDH') return 'hyper'
  if (c.startsWith('LMP2')) return 'lmp2'
  if (c.startsWith('LMP3')) return 'lmp3'
  if (c.includes('GT3')) return 'gt3'
  return 'other'
}

const SHORT: Record<ClassKey, string> = { hyper: 'HYPER', lmp2: 'LMP2', lmp3: 'LMP3', gt3: 'GT3', other: '' }

/** Rótulo curto: "LMP2_ELMS" -> "LMP2". Classe desconhecida mantém o nome cru. */
export function classShort(raw: string | null | undefined): string {
  const k = classKey(raw)
  return k === 'other' ? (raw ?? '').replace(/_/g, ' ') : SHORT[k]
}

/** "Hyper,GT3" -> ["Hyper", "GT3"]. */
export const splitClasses = (s: string | null | undefined) => (s ? s.split(',').filter(Boolean) : [])
