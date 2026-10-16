import { badgeDescription, badgeLabel } from '../lib/format'

// Ícones de badge idênticos aos do jogo (o próprio LMU os carrega de /badges/<nome>.svg), um arquivo por badge.
const files = import.meta.glob<string>('../assets/badges/*.svg', { query: '?no-inline', import: 'default', eager: true })
const urls: Record<string, string> = {}
for (const [path, url] of Object.entries(files)) urls[path.slice(path.lastIndexOf('/') + 1, -4)] = url

/** Badge do piloto (SR limpo, alerta, perigo…) com a imagem do jogo; o nome e a descrição ficam no tooltip. */
export function Badge({ name, size = 28 }: { name: string | null | undefined; size?: number }) {
  if (name === null || name === undefined) return null
  const key = name.length > 0 ? name : 'sr-rookie'
  const label = badgeLabel(key) ?? key
  const description = badgeDescription(key)
  const url = urls[key]
  if (!url) return <span className="pill">{label}</span>
  return (
    <img className="badge-img" src={url} alt={label} title={description ? `${label} — ${description}` : label}
      width={size} height={size} loading="lazy" decoding="async" />
  )
}
