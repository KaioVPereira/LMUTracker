const TOKEN_KEY = 'pitwall.token'
// No site a Api é o mesmo domínio (/api). No APK a página vem de https://localhost, então chama a Api pelo endereço
// completo: produção no build "mobile", e a Api local do PC no "mobiledev" (o `adb reverse tcp:5280` faz o localhost
// do celular chegar no PC).
const BASE =
  import.meta.env.MODE === 'mobile' ? 'https://lmutracker.gamestrackers.com/api'
  : import.meta.env.MODE === 'mobiledev' ? 'http://localhost:5280/api'
  : (import.meta.env.VITE_API_BASE ?? '/api')

/** Endereço completo de um caminho da Api vindo do servidor (ex.: "/api/uploads/tickets/x.png"). No site fica igual. */
export function apiUrl(path: string) {
  return /^https?:/.test(path) ? path : BASE.replace(/\/api$/, '') + path
}

/** Disparado quando a Api responde 401 com um token salvo: a sessão expirou. */
export const LOGOUT_EVENT = 'pitwall:logout'

export function getToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY)
  } catch {
    return null
  }
}

export function setToken(token: string | null): void {
  try {
    if (token) localStorage.setItem(TOKEN_KEY, token)
    else localStorage.removeItem(TOKEN_KEY)
  } catch {
    // armazenamento indisponível (aba anônima bloqueada): a sessão dura só esta página
  }
}

export class ApiError extends Error {
  status: number

  constructor(message: string, status: number) {
    super(message)
    this.status = status
  }
}

export async function api<T>(path: string, init: { method?: string; body?: unknown } = {}): Promise<T> {
  const token = getToken()
  const headers: Record<string, string> = {}
  if (token) headers.Authorization = `Bearer ${token}`
  if (init.body !== undefined) headers['Content-Type'] = 'application/json'

  let res: Response
  try {
    res = await fetch(`${BASE}${path}`, {
      method: init.method ?? 'GET',
      headers,
      body: init.body !== undefined ? JSON.stringify(init.body) : undefined,
    })
  } catch {
    throw new ApiError('Sem conexão com o servidor. Verifique sua internet e tente de novo.', 0)
  }

  if (res.status === 401 && token) {
    setToken(null)
    window.dispatchEvent(new Event(LOGOUT_EVENT))
  }

  const text = await res.text()
  let data: unknown = null
  if (text) {
    try {
      data = JSON.parse(text)
    } catch {
      data = null
    }
  }

  if (!res.ok) {
    const message = (data as { message?: string } | null)?.message ?? `Erro ${res.status} no servidor.`
    throw new ApiError(message, res.status)
  }
  return data as T
}
