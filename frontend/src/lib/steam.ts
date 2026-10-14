import { api } from '../api/client'
import { isNative } from './native'

/**
 * Leva para o login da Steam (OpenID); a volta cai em /vincular/retorno. No app o login abre dentro do próprio app
 * (capacitor.config.ts → allowNavigation) e volta para https://localhost, onde o token da sessão está.
 */
export async function startSteamLink(): Promise<void> {
  const { url } = await api<{ url: string }>(isNative ? '/link/steam/start?app=true' : '/link/steam/start')
  window.location.assign(url)
}
