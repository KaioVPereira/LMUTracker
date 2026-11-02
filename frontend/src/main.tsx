import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { ClosedPage } from './pages/ClosedPage'
import './index.css'

// Site encerrado: só a página de encerramento, sem rotas, login nem chamadas à Api. O app completo é o App.tsx
// (montado aqui com BrowserRouter + AuthProvider + FollowsProvider até 2026-11-02).
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ClosedPage />
  </StrictMode>,
)
