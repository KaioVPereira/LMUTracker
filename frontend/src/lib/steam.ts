import { api } from '../api/client'

/** Leva para o login da Steam (OpenID); a volta cai em /vincular/retorno. */
export async function startSteamLink(): Promise<void> {
  const { url } = await api<{ url: string }>('/link/steam/start')
  window.location.assign(url)
}
