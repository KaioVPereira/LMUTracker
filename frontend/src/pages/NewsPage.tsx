import { useEffect } from 'react'
import type { ChangelogEntry } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { ErrorBox, RowsSkeleton } from '../components/Bits'
import { ChangelogCard } from '../components/Changelog'
import { TopBar } from '../components/TopBar'
import { markSeen } from '../lib/changelog'
import { useApi } from '../lib/useApi'

/** Tudo o que já foi lançado, da novidade mais recente para a mais antiga. Abrir a página conta como "visto". */
export function NewsPage() {
  const { user, loading: authLoading } = useAuth()
  const all = useApi<ChangelogEntry[]>('/changelog')
  useEffect(() => { if (!authLoading && all.data) markSeen(!!user) }, [authLoading, all.data, user])

  return (
    <>
      <TopBar />
      <div className="wrap news-page">
        <div className="page-head">
          <span className="eyebrow">Pitwall</span>
          <h1>Novidades</h1>
          <p className="note">O que foi lançado, melhorado e corrigido. Quando sai algo novo, ele também aparece no seu próximo acesso.</p>
        </div>
        {all.error ? <ErrorBox message={all.error} onRetry={all.reload} />
          : !all.data ? <RowsSkeleton rows={4} />
          : <div className="news-list">{all.data.map((e) => <ChangelogCard key={e.key} e={e} />)}</div>}
      </div>
    </>
  )
}
