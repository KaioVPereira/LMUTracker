import { useEffect } from 'react'
import { initBackButton } from '../lib/native'

/**
 * Pitwall encerrado (2026-11-02): a RaceControl bloqueou o acesso aos dados. É a única tela do site e do app — o
 * resto do front continua no repositório, mas não é mais montado (ver main.tsx).
 */
export function ClosedPage() {
  // No app, "voltar" só minimiza: não há outra tela.
  useEffect(() => initBackButton(() => true, () => {}), [])

  return (
    <main className="closed">
      <div className="closed-card">
        <span className="logo">
          <span className="mk" aria-hidden="true" />
          PITWALL
        </span>
        <h1>O Pitwall foi encerrado</h1>
        <p>
          A RaceControl bloqueou o nosso acesso aos dados das corridas do Le Mans Ultimate. Sem esses dados, o site não
          terá mais informações de corridas, resultados, rankings e perfis — e, por isso, será fechado.
        </p>
        <p className="closed-thanks">Obrigado a todos que acompanharam.</p>
      </div>
    </main>
  )
}
