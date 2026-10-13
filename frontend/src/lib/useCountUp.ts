import { useEffect, useRef, useState } from 'react'

const reduceMotion = () => typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches

/**
 * Número que "conta" até o valor (600 ms, desacelerando). Na primeira vez sai do zero; depois, do valor anterior.
 * Com "reduzir movimento" ligado no sistema, mostra o valor direto.
 */
export function useCountUp(target: number | null | undefined, duration = 600): number | null {
  const [shown, setShown] = useState<number | null>(target ?? null)
  const from = useRef(0)

  useEffect(() => {
    if (target === null || target === undefined) return
    if (reduceMotion() || from.current === target) {
      from.current = target
      setShown(target)
      return
    }
    const start = performance.now()
    const begin = from.current
    let raf = 0
    const tick = (t: number) => {
      const p = Math.min(1, (t - start) / duration)
      const eased = 1 - Math.pow(1 - p, 3)
      setShown(Math.round(begin + (target - begin) * eased))
      if (p < 1) raf = requestAnimationFrame(tick)
      else from.current = target
    }
    raf = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(raf)
  }, [target, duration])

  return shown
}
