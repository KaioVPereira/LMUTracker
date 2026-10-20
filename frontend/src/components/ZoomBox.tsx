import { useEffect, useState, type ReactNode } from 'react'

const NARROW = '(max-width: 860px)'

function useNarrow() {
  const [narrow, setNarrow] = useState(() => typeof window !== 'undefined' && window.matchMedia(NARROW).matches)
  useEffect(() => {
    const m = window.matchMedia(NARROW)
    const on = () => setNarrow(m.matches)
    m.addEventListener('change', on)
    return () => m.removeEventListener('change', on)
  }, [])
  return narrow
}

/** Níveis de zoom; 0 = caber na largura da tela (como era antes). */
const LEVELS = [0, 1, 1.5, 2, 3]

/**
 * Gráfico no celular: em vez de espremer o SVG na largura da tela (texto ilegível), ele abre na largura de leitura
 * (`width` px = 100%, o tamanho em que o texto do gráfico foi desenhado), com rolagem para os lados — e para baixo,
 * quando fica mais alto que a tela. Botões − / + trocam o zoom (da tela inteira até 300%). No computador não muda nada.
 */
export function ZoomBox({ width, children }: { width: number; children: ReactNode }) {
  const narrow = useNarrow()
  const [level, setLevel] = useState(1)
  if (!narrow) return <>{children}</>
  const z = LEVELS[level]
  return (
    <div className="zoombox">
      <div className="zoombox-bar">
        <span className="zoombox-hint">{z === 0 ? 'Gráfico inteiro na tela' : 'Arraste para ver o gráfico todo'}</span>
        <div className="zoombox-btns" role="group" aria-label="Zoom do gráfico">
          <button type="button" onClick={() => setLevel((l) => Math.max(0, l - 1))} disabled={level === 0} aria-label="Diminuir o zoom">−</button>
          <span className="mono" aria-live="polite">{z === 0 ? 'tela' : `${z * 100}%`}</span>
          <button type="button" onClick={() => setLevel((l) => Math.min(LEVELS.length - 1, l + 1))} disabled={level === LEVELS.length - 1} aria-label="Aumentar o zoom">+</button>
        </div>
      </div>
      <div className="zoombox-scroll">
        <div className="zoombox-in" style={z ? { width: Math.round(width * z) } : undefined}>{children}</div>
      </div>
    </div>
  )
}
