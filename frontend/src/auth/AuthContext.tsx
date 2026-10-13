import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, getToken, LOGOUT_EVENT, setToken } from '../api/client'
import type { AuthResponse, UserDto } from '../api/types'

interface AuthState {
  user: UserDto | null
  /** true até sabermos se o token salvo ainda vale. */
  loading: boolean
  login: (usernameOrEmail: string, password: string) => Promise<void>
  register: (username: string, email: string, password: string) => Promise<void>
  logout: () => void
  refreshUser: () => Promise<void>
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserDto | null>(null)
  const [loading, setLoading] = useState(() => getToken() !== null)

  const refreshUser = useCallback(async () => {
    setUser(await api<UserDto>('/auth/me'))
  }, [])

  // Sessão salva: confere com a Api se o token ainda vale.
  useEffect(() => {
    if (!getToken()) return
    api<UserDto>('/auth/me')
      .then(setUser)
      .catch(() => setToken(null))
      .finally(() => setLoading(false))
  }, [])

  // 401 em qualquer chamada => sessão expirou.
  useEffect(() => {
    const onLogout = () => setUser(null)
    window.addEventListener(LOGOUT_EVENT, onLogout)
    return () => window.removeEventListener(LOGOUT_EVENT, onLogout)
  }, [])

  const login = useCallback(async (usernameOrEmail: string, password: string) => {
    const res = await api<AuthResponse>('/auth/login', { method: 'POST', body: { usernameOrEmail, password } })
    setToken(res.accessToken)
    setUser(res.user)
  }, [])

  const register = useCallback(async (username: string, email: string, password: string) => {
    const res = await api<AuthResponse>('/auth/register', { method: 'POST', body: { username, email, password } })
    setToken(res.accessToken)
    setUser(res.user)
  }, [])

  const logout = useCallback(() => {
    setToken(null)
    setUser(null)
  }, [])

  const value = useMemo(
    () => ({ user, loading, login, register, logout, refreshUser }),
    [user, loading, login, register, logout, refreshUser],
  )
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth fora do AuthProvider')
  return ctx
}
