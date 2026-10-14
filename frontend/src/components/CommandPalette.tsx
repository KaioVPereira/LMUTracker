import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import type { SearchResults } from '../api/types'
import { classKey, classShort } from '../lib/classes'
import { fmtDateTime, fmtInt, rankClass } from '../lib/format'
import { ClassChips, Skeleton } from './Bits'

type Kind = 'driver' | 'track' | 'car' | 'race' | 'page'

/** Um item da paleta: o que mostrar e para onde ir. É também o que fica guardado nas buscas recentes. */
interface Item {
  id: string
  kind: Kind
  label: string
  sub?: string
  href: string
  cls?: string | null      // classe do carro (chip)
  classes?: string | null  // classes da corrida
  rank?: string | null
}

const RECENT_KEY = 'pitwall.recent-search'
const RECENT_MAX = 6
const DEBOUNCE_MS = 150

function readRecent(): Item[] {
  try {
    const raw = localStorage.getItem(RECENT_KEY)
    const list = raw ? (JSON.parse(raw) as Item[]) : []
    return Array.isArray(list) ? list.filter((i) => i && i.href && i.label).slice(0, RECENT_MAX) : []
  } catch {
    return []
  }
}

function saveRecent(list: Item[]) {
  try {
    localStorage.setItem(RECENT_KEY, JSON.stringify(list.slice(0, RECENT_MAX)))
  } catch {
    // sem armazenamento (aba anônima): as recentes valem só nesta página
  }
}

const SHORTCUTS: Item[] = [
  { id: 'p:inicio', kind: 'page', label: 'Início', sub: 'Corrida ao vivo e agenda', href: '/' },
  { id: 'p:corridas', kind: 'page', label: 'Corridas', sub: 'Todos os resultados guardados', href: '/corridas' },
  { id: 'p:rankings', kind: 'page', label: 'Rankings', sub: 'Voltas mais rápidas por pista e classe', href: '/rankings' },
  { id: 'p:perfil', kind: 'page', label: 'Meu perfil', sub: 'Suas corridas e seu rank', href: '/perfil' },
]

const GROUPS: { kind: Kind; title: string }[] = [
  { kind: 'driver', title: 'Pilotos' },
  { kind: 'track', title: 'Pistas' },
  { kind: 'car', title: 'Carros' },
  { kind: 'race', title: 'Corridas' },
]

/** 3 = nome igual ao termo, 2 = começa com ele, 1 = contém. */
function matchScore(label: string, q: string): number {
  const l = fold(label)
  return l === q ? 3 : l.startsWith(q) ? 2 : 1
}

/**
 * Itens da resposta, com os grupos ordenados pela melhor correspondência: buscar "fuji" põe a pista Fuji antes do
 * piloto "fuji yoshi". Empate mantém a ordem padrão (pilotos, pistas, carros, corridas).
 */
function toItems(r: SearchResults, query: string): Item[] {
  const q = fold(query.trim())
  const all = [
    ...r.drivers.map((d): Item => ({
      id: `d:${d.driverId}`, kind: 'driver', label: d.name, href: `/pilotos/${d.driverId}`,
      sub: [d.nationality, `${fmtInt(d.races)} corrida${d.races === 1 ? '' : 's'}`].filter(Boolean).join(' · '),
      rank: d.driverRankTitle ? `${d.driverRankTitle}${d.driverRankTier !== null ? ` ${d.driverRankTier}` : ''}` : null,
    })),
    ...r.tracks.map((t): Item => ({
      id: `t:${t.track}`, kind: 'track', label: t.track, href: `/pistas/${encodeURIComponent(t.track)}`,
      sub: `${fmtInt(t.races)} corrida${t.races === 1 ? '' : 's'} com resultado`,
    })),
    ...r.cars.map((c): Item => ({
      id: `c:${c.car}:${c.carClass ?? ''}`, kind: 'car', label: c.car, href: `/carros/${encodeURIComponent(c.car)}`,
      sub: `${fmtInt(c.starts)} largada${c.starts === 1 ? '' : 's'}`, cls: c.carClass,
    })),
    ...r.races.map((e): Item => ({
      id: `r:${e.eventId}`, kind: 'race', label: e.title ?? 'Corrida', href: `/corridas/${e.eventId}`,
      sub: `${e.track ?? '—'} · ${fmtDateTime(e.startsAt)}`, classes: e.classes,
    })),
  ]
  const best = new Map<Kind, number>()
  for (const i of all) best.set(i.kind, Math.max(best.get(i.kind) ?? 0, matchScore(i.label, q)))
  const order = GROUPS.map((g) => g.kind).sort((a, b) => (best.get(b) ?? 0) - (best.get(a) ?? 0))
  return order.flatMap((k) => all.filter((i) => i.kind === k))
}

const fold = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()

/** Destaca o trecho que casou com a busca (sem acento e sem caixa). */
function Highlight({ text, query }: { text: string; query: string }) {
  const q = fold(query.trim())
  const t = fold(text.normalize('NFC'))
  const i = q ? t.indexOf(q) : -1
  if (i < 0 || t.length !== text.normalize('NFC').length) return <>{text}</>
  const src = text.normalize('NFC')
  return <>{src.slice(0, i)}<mark>{src.slice(i, i + q.length)}</mark>{src.slice(i + q.length)}</>
}

const KIND_ICON: Record<Kind, ReactNode> = {
  driver: <><circle cx="12" cy="8" r="4" /><path d="M4 21c1-4 4-6 8-6s7 2 8 6" /></>,
  track: <><path d="M5 18c4 0 3-7 7-7s3 5 7 4" /><circle cx="5" cy="18" r="2" /><path d="M19 6v9" /><path d="M19 6h-4l1 2-1 2h4" /></>,
  car: <><path d="M3 15l2-5h11l3 3h2v2" /><circle cx="7" cy="16" r="2" /><circle cx="17" cy="16" r="2" /></>,
  race: <path d="M5 21V4h11l-2 4 2 4H5" />,
  page: <path d="M9 6l6 6-6 6" />,
}

function KindIcon({ kind }: { kind: Kind }) {
  return (
    <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      {KIND_ICON[kind]}
    </svg>
  )
}

/**
 * Paleta de busca global. Abre por Ctrl+K / ⌘K, pela tecla "/" ou pelo botão da barra. Setas navegam, Enter abre,
 * Esc fecha. Sem termo, mostra as buscas recentes e atalhos. Montada só enquanto está aberta: cada abertura começa limpa.
 */
export function CommandPalette({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate()
  const inputRef = useRef<HTMLInputElement>(null)
  const listRef = useRef<HTMLDivElement>(null)
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<{ q: string; items: Item[] } | null>(null)
  const [error, setError] = useState(false)
  const [recent, setRecent] = useState<Item[]>(readRecent)
  const [active, setActive] = useState(0)
  const seq = useRef(0)

  // Enquanto aberta: foco no campo e rolagem da página travada; ao fechar, o foco volta para onde estava.
  useEffect(() => {
    const prev = document.activeElement as HTMLElement | null
    const overflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    requestAnimationFrame(() => inputRef.current?.focus())
    return () => {
      document.body.style.overflow = overflow
      prev?.focus?.()
    }
  }, [])

  // Busca enquanto digita, com espera curta; resposta de um termo antigo é descartada.
  const term = query.trim()
  useEffect(() => {
    if (term.length < 2) return
    const mine = ++seq.current
    const id = window.setTimeout(() => {
      api<SearchResults>(`/lmu/search?q=${encodeURIComponent(term)}`)
        .then((r) => { if (mine === seq.current) { setResults({ q: term, items: toItems(r, term) }); setError(false); setActive(0) } })
        .catch(() => { if (mine === seq.current) setError(true) })
    }, DEBOUNCE_MS)
    return () => window.clearTimeout(id)
  }, [term])

  const searching = term.length >= 2
  const loading = searching && results?.q !== term && !error
  const items = useMemo<Item[]>(() => {
    if (!searching) return [...recent, ...SHORTCUTS.filter((s) => !recent.some((r) => r.href === s.href))]
    return results?.q === term ? results.items : (results?.items ?? [])
  }, [searching, recent, results, term])

  // Mantém a opção ativa visível ao navegar pelo teclado.
  useEffect(() => {
    listRef.current?.querySelector<HTMLElement>(`[data-index="${active}"]`)?.scrollIntoView({ block: 'nearest' })
  }, [active])

  function choose(item: Item) {
    if (item.kind !== 'page') {
      const next = [item, ...readRecent().filter((r) => r.id !== item.id)]
      saveRecent(next)
    }
    onClose()
    navigate(item.href)
  }

  function clearRecent() {
    saveRecent([])
    setRecent([])
    setActive(0)
  }

  function onKey(e: KeyboardEvent) {
    if (e.key === 'Escape') { e.preventDefault(); onClose() }
    else if (e.key === 'ArrowDown') { e.preventDefault(); setActive((a) => (items.length ? (a + 1) % items.length : 0)) }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive((a) => (items.length ? (a - 1 + items.length) % items.length : 0)) }
    else if (e.key === 'Enter' && items[active]) { e.preventDefault(); choose(items[active]) }
  }

  // Grupos na ordem dos itens (já vêm ordenados pela correspondência); sem termo, "Recentes" e "Atalhos".
  const sections: { title: string; list: { item: Item; index: number }[]; action?: ReactNode }[] = []
  const indexed = items.map((item, index) => ({ item, index }))
  if (!searching) {
    const rec = indexed.filter((x) => x.item.kind !== 'page')
    if (rec.length) sections.push({
      title: 'Recentes', list: rec,
      action: <button type="button" className="cp-clear" onClick={clearRecent}>Limpar</button>,
    })
    sections.push({ title: 'Atalhos', list: indexed.filter((x) => x.item.kind === 'page') })
  } else {
    const kinds = [...new Set(items.map((i) => i.kind))]
    for (const k of kinds) {
      const title = GROUPS.find((g) => g.kind === k)?.title ?? ''
      sections.push({ title, list: indexed.filter((x) => x.item.kind === k) })
    }
  }

  const activeId = items[active] ? `cp-opt-${active}` : undefined

  return (
    <div className="cp-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
      <div className="cp" role="dialog" aria-modal="true" aria-label="Buscar no Pitwall" onKeyDown={onKey}>
        <div className="cp-head">
          <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><circle cx="11" cy="11" r="7" /><path d="m20 20-3.5-3.5" /></svg>
          <input
            ref={inputRef}
            className="cp-input"
            placeholder="Pilotos, pistas, carros, corridas…"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            role="combobox"
            aria-expanded="true"
            aria-controls="cp-list"
            aria-activedescendant={activeId}
            aria-autocomplete="list"
            autoComplete="off"
            spellCheck={false}
            enterKeyHint="go"
          />
          <button type="button" className="cp-close" onClick={onClose} aria-label="Fechar busca">Esc</button>
        </div>

        <div className="cp-body" id="cp-list" role="listbox" aria-label="Resultados" ref={listRef}>
          {loading && !results && (
            <div className="cp-loading" role="status" aria-label="Buscando">
              {[0, 1, 2, 3].map((i) => (
                <div key={i} className="cp-opt"><Skeleton w={18} h={18} r={6} /><span className="sk-stack" style={{ gap: 6, flex: 1 }}><Skeleton w="55%" /><Skeleton w="30%" h={10} /></span></div>
              ))}
            </div>
          )}
          {error && searching && <p className="cp-msg">Não deu para buscar agora. Confira a conexão e tente de novo.</p>}
          {searching && !loading && !error && items.length === 0 && (
            <p className="cp-msg">Nada encontrado para “{term}”. Tente parte do nome do piloto, a pista ou o carro.</p>
          )}
          {term.length === 1 && <p className="cp-msg">Digite pelo menos 2 letras.</p>}

          {sections.map((s) => (
            <div key={s.title} className="cp-group" role="group" aria-label={s.title}>
              <div className="cp-group-h"><span>{s.title}</span>{s.action}</div>
              {s.list.map(({ item, index }) => (
                <div
                  key={item.id}
                  id={`cp-opt-${index}`}
                  data-index={index}
                  role="option"
                  aria-selected={index === active}
                  className={`cp-opt${index === active ? ' is-active' : ''}${loading ? ' is-stale' : ''}`}
                  onMouseMove={() => { if (index !== active) setActive(index) }}
                  onClick={() => choose(item)}
                >
                  <span className={`cp-ico${item.cls ? ` cls-${classKey(item.cls)}` : ''}`}><KindIcon kind={item.kind} /></span>
                  <span className="cp-text">
                    <b>{searching ? <Highlight text={item.label} query={term} /> : item.label}</b>
                    {item.sub && <span>{item.sub}</span>}
                  </span>
                  <span className="cp-meta">
                    {item.rank && <span className={`rk ${rankClass(item.rank.split(' ')[0])}`}>{item.rank}</span>}
                    {item.cls && <span className={`cls cls-${classKey(item.cls)}`}>{classShort(item.cls)}</span>}
                    {item.classes && <ClassChips classes={item.classes} />}
                  </span>
                </div>
              ))}
            </div>
          ))}
        </div>

        <div className="cp-foot" aria-hidden="true">
          <span><kbd>↑</kbd><kbd>↓</kbd> navegar</span><span><kbd>Enter</kbd> abrir</span><span><kbd>Esc</kbd> fechar</span>
        </div>
      </div>
    </div>
  )
}
