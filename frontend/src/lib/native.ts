import { Capacitor } from '@capacitor/core'
import { App } from '@capacitor/app'

/** true só dentro do APK. No site tudo aqui vira no-op. */
export const isNative = Capacitor.isNativePlatform()

/** Navegador de celular Android (fora do app): é para quem o site sugere baixar o APK. */
export const isAndroidBrowser = !isNative && typeof navigator !== 'undefined' && /Android/i.test(navigator.userAgent)

/** Versão instalada (versionName/versionCode do Android). Nula no site, onde não há app instalado. */
export async function getAppInfo(): Promise<{ version: string; build: number } | null> {
  if (!isNative) return null
  try {
    const info = await App.getInfo()
    return { version: info.version, build: Number(info.build) || 0 }
  } catch {
    // Sem saber a versão, melhor não afirmar que está desatualizado.
    return null
  }
}

// ---------- Botão voltar do Android ----------

const backHandlers: (() => void)[] = []

/** Quem está aberto por cima (janela, busca, menu) fecha primeiro no "voltar". Devolve o desregistro. */
export function pushBackHandler(fn: () => void) {
  backHandlers.push(fn)
  return () => {
    const i = backHandlers.lastIndexOf(fn)
    if (i >= 0) backHandlers.splice(i, 1)
  }
}

/** Voltar: fecha o que está por cima; senão volta uma tela; na tela inicial, minimiza (não fecha) o app. */
export function initBackButton(isRoot: () => boolean, goBack: () => void) {
  if (!isNative) return () => {}
  const sub = App.addListener('backButton', () => {
    const top = backHandlers[backHandlers.length - 1]
    if (top) top()
    else if (!isRoot()) goBack()
    else void App.minimizeApp()
  })
  return () => { void sub.then((s) => s.remove()) }
}
