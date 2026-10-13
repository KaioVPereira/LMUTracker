import { useState, type FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { ApiError } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { Logo } from '../components/TopBar'

type Mode = 'login' | 'register'

export function LoginPage() {
  const { user, login, register } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const from = (location.state as { from?: string } | null)?.from ?? '/'

  const [mode, setMode] = useState<Mode>('login')
  const [identifier, setIdentifier] = useState('')
  const [username, setUsername] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  if (user) return <Navigate to={from} replace />

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      if (mode === 'login') await login(identifier, password)
      else await register(username, email, password)
      navigate(from, { replace: true })
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Não foi possível concluir. Tente de novo.')
    } finally {
      setBusy(false)
    }
  }

  function switchMode(next: Mode) {
    setMode(next)
    setError(null)
  }

  return (
    <div className="auth">
      <aside className="auth-hero">
        <Logo />
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <span className="eyebrow">Le Mans Ultimate</span>
          <h1>Cada corrida oficial, cronometrada no seu perfil.</h1>
          <p>
            Crie sua conta, vincule a Steam e acompanhe seus resultados, a evolução do Driver Rank e do Safety
            Rank e a agenda das próximas corridas.
          </p>
        </div>
        <div className="auth-points">
          <span><b>Agenda ao vivo</b> das corridas diárias, por nível.</span>
          <span><b>Seus resultados</b> de cada split, com largada, chegada e melhor volta.</span>
          <span><b>Variação de DR e SR</b> corrida a corrida.</span>
        </div>
      </aside>

      <main className="auth-panel">
        <div className="auth-card">
          <div className="tabs" role="tablist" aria-label="Acesso">
            <button type="button" role="tab" aria-selected={mode === 'login'} onClick={() => switchMode('login')}>
              Entrar
            </button>
            <button type="button" role="tab" aria-selected={mode === 'register'} onClick={() => switchMode('register')}>
              Criar conta
            </button>
          </div>

          <form className="auth-form" onSubmit={onSubmit} noValidate>
            {mode === 'login' ? (
              <div className="field">
                <label htmlFor="identifier">Usuário ou e-mail</label>
                <input id="identifier" autoComplete="username" value={identifier}
                  onChange={(e) => setIdentifier(e.target.value)} required />
              </div>
            ) : (
              <>
                <div className="field">
                  <label htmlFor="username">Nome de usuário</label>
                  <input id="username" autoComplete="username" value={username}
                    onChange={(e) => setUsername(e.target.value)} required minLength={3} maxLength={32} />
                </div>
                <div className="field">
                  <label htmlFor="email">E-mail</label>
                  <input id="email" type="email" autoComplete="email" value={email}
                    onChange={(e) => setEmail(e.target.value)} required />
                </div>
              </>
            )}
            <div className="field">
              <label htmlFor="password">Senha</label>
              <input id="password" type="password" value={password} onChange={(e) => setPassword(e.target.value)}
                autoComplete={mode === 'login' ? 'current-password' : 'new-password'} required
                minLength={mode === 'register' ? 8 : undefined} />
            </div>

            {error && <div className="form-error" role="alert">{error}</div>}

            <button type="submit" className="btn btn-primary" disabled={busy}>
              {busy ? 'Aguarde…' : mode === 'login' ? 'Entrar' : 'Criar conta'}
            </button>
            {mode === 'register' && <p className="note">A senha precisa ter pelo menos 8 caracteres.</p>}
          </form>
        </div>
      </main>
    </div>
  )
}
