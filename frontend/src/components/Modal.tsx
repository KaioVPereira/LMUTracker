import { useEffect, useId, useRef, type ReactNode } from 'react'

/**
 * Janela sobre a página (diálogo modal): fecha com Esc, clique fora ou no X; trava a rolagem do fundo e devolve o
 * foco a quem a abriu. No celular vira uma folha que sobe de baixo.
 */
export function Modal({ title, onClose, children, wide = false }: { title: string; onClose: () => void; children: ReactNode; wide?: boolean }) {
  const titleId = useId()
  const box = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    const overflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    // Foco no primeiro campo (ou na própria janela), sem roubar de um autoFocus.
    if (!box.current?.contains(document.activeElement)) box.current?.focus()
    return () => {
      document.removeEventListener('keydown', onKey)
      document.body.style.overflow = overflow
      opener?.focus?.()
    }
  }, [onClose])

  return (
    <div className="modal-backdrop" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose() }}>
      <div ref={box} className={`modal ${wide ? 'modal-wide' : ''}`} role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}>
        <div className="modal-head">
          <h2 id={titleId}>{title}</h2>
          <button type="button" className="modal-x" onClick={onClose} aria-label="Fechar">
            <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round"><path d="M6 6l12 12M18 6 6 18" /></svg>
          </button>
        </div>
        <div className="modal-body">{children}</div>
      </div>
    </div>
  )
}
