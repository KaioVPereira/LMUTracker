import { DownloadIcon } from '../components/AppShell'
import { Loading } from '../components/Bits'
import { TopBar } from '../components/TopBar'
import { fmtSize, useAppUpdate } from '../lib/appUpdate'
import { fmtDay } from '../lib/format'
import { isNative } from '../lib/native'

const BENEFITS = [
  { t: 'Abre direto', d: 'Ícone na tela inicial, sem barra do navegador: corridas, rankings e o seu perfil a um toque.' },
  { t: 'Sempre atualizado', d: 'Quando sai uma versão nova, o app avisa na hora e atualiza por cima.' },
  { t: 'Steam dentro do app', d: 'Vincule a sua conta Steam sem sair do app.' },
]

/** App Android: versão mais nova liberada e o download. No app, compara com a instalada. */
export function AppAndroidPage() {
  const { latest, installed, updateAvailable, loading, downloadHref } = useAppUpdate()
  const current = isNative && !updateAvailable

  return (
    <>
      <TopBar />
      <div className="wrap app-page">
        <header className="page-head">
          <span className="eyebrow">App</span>
          <h1>Pitwall para Android</h1>
          <p className="note">
            {isNative
              ? <>Instalado neste celular: versão <b>{installed?.version ?? '—'}</b>.</>
              : 'O Pitwall no seu celular, com aviso de versão nova.'}
          </p>
        </header>

        {!isNative && (
          <div className="grid-cards app-benefits">
            {BENEFITS.map((b) => (
              <div key={b.t} className="card pad"><b>{b.t}</b><p className="note">{b.d}</p></div>
            ))}
          </div>
        )}

        <section className="card pad app-release" aria-labelledby="app-rel-h">
          <h2 id="app-rel-h" className="sr-only">Versão</h2>
          {loading ? <Loading rows={2} /> : !latest ? (
            <p className="note">Nenhuma versão publicada ainda.</p>
          ) : (
            <>
              {isNative && (
                <p className={`app-status ${updateAvailable ? 'is-new' : 'is-ok'}`}>
                  {updateAvailable ? `Nova versão ${latest.version} disponível` : 'Você está na versão mais nova'}
                </p>
              )}
              <dl className="app-facts">
                <div><dt>Mais recente</dt><dd className="mono">{latest.version}</dd></div>
                <div><dt>Publicada</dt><dd>{latest.publishedAt ? fmtDay(latest.publishedAt) : '—'}</dd></div>
                <div><dt>Tamanho</dt><dd className="mono">{fmtSize(latest.sizeBytes)}</dd></div>
              </dl>
              {latest.notes && (
                <div className="app-notes">
                  <b>O que mudou</b>
                  <p>{latest.notes}</p>
                </div>
              )}
              {/* Sem `download`: no app o link abre no navegador do celular, que baixa e oferece instalar. */}
              <a href={downloadHref} className={`btn ${current ? '' : 'btn-primary'} app-download`}>
                <DownloadIcon /> {current ? 'Baixar de novo' : `Baixar versão ${latest.version}`}
              </a>
              <p className="note">
                Na primeira vez o Android pede para permitir instalar apps pelo navegador. A atualização instala por cima e
                mantém o seu login.
              </p>
            </>
          )}
        </section>
      </div>
    </>
  )
}
