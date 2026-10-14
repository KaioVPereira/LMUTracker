import { useEffect, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { fmtSize, useAppUpdate } from '../lib/appUpdate'
import { initBackButton, isAndroidBrowser, isNative } from '../lib/native'
import { Modal } from './Modal'

const read = (k: string) => { try { return localStorage.getItem(k) } catch { return null } }
const write = (k: string, v: string) => { try { localStorage.setItem(k, v) } catch { /* só nesta sessão */ } }

const SKIPPED_KEY = 'pitwall.update.skipped'      // versionCode que a pessoa pediu para lembrar depois
const SUGGEST_KEY = 'pitwall.app.suggestedAt'     // quando o site sugeriu o app pela última vez
const SUGGEST_EVERY = 14 * 24 * 60 * 60 * 1000    // no navegador, volta a sugerir depois de 14 dias

/** Algo aberto por cima (janela, busca, menu da conta) — o "voltar" do Android fecha isso antes de mudar de tela. */
const overlayOpen = () => !!document.querySelector('[aria-modal="true"], .cp-backdrop, .acct-menu')

/**
 * Peças que só existem no app ou no celular, montadas uma vez em volta das rotas:
 * - no app: botão voltar do Android e o aviso de versão nova assim que ela é publicada;
 * - no navegador do Android: sugestão de baixar o app.
 */
export function AppShell() {
  const navigate = useNavigate()
  const location = useLocation()

  // Voltar do Android: fecha o que está por cima (as janelas fecham com Esc), senão volta uma tela; na tela inicial
  // minimiza o app.
  useEffect(() => initBackButton(
    () => window.location.pathname === '/' && !overlayOpen(),
    () => {
      if (overlayOpen()) {
        const target = document.activeElement instanceof HTMLElement ? document.activeElement : document.body
        target.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
      } else if (window.history.length > 1) navigate(-1)
      else navigate('/', { replace: true })
    },
  ), [navigate])

  // O fluxo da Steam no app não passa pelo login do site: a tela de retorno já tem a sessão.
  if (location.pathname.startsWith('/vincular')) return null
  return isNative ? <UpdateNotice /> : isAndroidBrowser ? <AppSuggestion /> : null
}

/** No app: janela "nova versão disponível" na abertura, com o que mudou. "Depois" só volta na próxima versão. */
function UpdateNotice() {
  const { latest, installed, updateAvailable, downloadHref } = useAppUpdate()
  const [closed, setClosed] = useState(false)
  if (!updateAvailable || !latest || closed || read(SKIPPED_KEY) === String(latest.versionCode)) return null

  const later = () => { write(SKIPPED_KEY, String(latest.versionCode)); setClosed(true) }
  return (
    <Modal title="Nova versão" onClose={later}>
      <div className="app-update">
        <p className="app-update-lead">
          A versão <b>{latest.version}</b> do Pitwall está disponível{installed && <> (você está na {installed.version})</>}.
        </p>
        {latest.notes && (
          <div className="app-notes">
            <b>O que mudou</b>
            <p>{latest.notes}</p>
          </div>
        )}
        {/* Sem `download`: no app o link abre no navegador do celular, que baixa e oferece instalar por cima. */}
        <a href={downloadHref} className="btn btn-primary app-update-go" onClick={() => setClosed(true)}>
          <DownloadIcon /> Atualizar · {fmtSize(latest.sizeBytes)}
        </a>
        <button type="button" className="btn btn-ghost" onClick={later}>Depois</button>
        <p className="note">A atualização instala por cima e mantém o seu login.</p>
      </div>
    </Modal>
  )
}

/** No navegador do Android: faixa sugerindo baixar o app (some por 14 dias ao dispensar). */
function AppSuggestion() {
  const { latest, downloadHref } = useAppUpdate()
  const [hidden, setHidden] = useState(() => Date.now() - Number(read(SUGGEST_KEY) ?? 0) < SUGGEST_EVERY)
  if (!latest || hidden) return null

  const dismiss = () => { write(SUGGEST_KEY, String(Date.now())); setHidden(true) }
  return (
    <aside className="app-suggest" aria-label="App Android">
      <span className="app-suggest-ico" aria-hidden="true"><span className="mk" /></span>
      <span className="app-suggest-text">
        <b>Pitwall para Android</b>
        <span>Abre direto e avisa quando tiver versão nova.</span>
      </span>
      <a href={downloadHref} className="btn btn-sm btn-primary" onClick={dismiss}>Baixar</a>
      <Link to="/app" className="app-suggest-more" onClick={dismiss}>Saiba mais</Link>
      <button type="button" className="app-suggest-x" onClick={dismiss} aria-label="Agora não">
        <svg aria-hidden="true" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round"><path d="M6 6l12 12M18 6 6 18" /></svg>
      </button>
    </aside>
  )
}

export function DownloadIcon() {
  return (
    <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M12 4v11M7 10l5 5 5-5M5 20h14" />
    </svg>
  )
}
