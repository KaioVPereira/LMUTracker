import { countryName } from '../lib/format'

// Bandeiras em SVG (country-flag-icons, MIT), uma por arquivo: o navegador só baixa as que aparecem na tela.
// Emoji de bandeira não serve — o Windows mostra as duas letras no lugar.
const files = import.meta.glob<string>('/node_modules/country-flag-icons/3x2/*.svg', { query: '?no-inline', import: 'default', eager: true })
const urls: Record<string, string> = {}
for (const [path, url] of Object.entries(files)) urls[path.slice(path.lastIndexOf('/') + 1, -4)] = url

/** Bandeira do país do piloto (código ISO como o jogo grava: "BR", "GB"…), com o nome no tooltip. */
export function Flag({ code, withName = false }: { code: string | null | undefined; withName?: boolean }) {
  if (!code) return null
  const key = code.trim().toUpperCase()
  const name = countryName(key)
  const url = urls[key]
  if (!url) return <span className="pill" title={name}>{key}</span>
  return (
    <span className="flag-wrap" title={name}>
      <img className="flag" src={url} alt={name} loading="lazy" decoding="async" width={21} height={14} />
      {withName && <span className="flag-name">{name}</span>}
    </span>
  )
}
