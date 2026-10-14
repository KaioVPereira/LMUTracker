import { Navigate, Route, Routes } from 'react-router-dom'
import { RequireAuth } from './components/RequireAuth'
import { CarPage } from './pages/CarPage'
import { DashboardPage } from './pages/DashboardPage'
import { DriverPage } from './pages/DriverPage'
import { HeadToHeadPage } from './pages/HeadToHeadPage'
import { LoginPage } from './pages/LoginPage'
import { MyProfilePage } from './pages/MyProfilePage'
import { RacePage } from './pages/RacePage'
import { RacesPage } from './pages/RacesPage'
import { RankingsPage } from './pages/RankingsPage'
import { ResultPage } from './pages/ResultPage'
import { SteamReturnPage } from './pages/SteamReturnPage'
import { TrackPage } from './pages/TrackPage'

// Leitura pública (como os perfis e resultados do próprio jogo); só o que é da conta exige login.
export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/" element={<DashboardPage />} />
      <Route path="/corridas" element={<RacesPage />} />
      <Route path="/corridas/:eventId" element={<RacePage />} />
      <Route path="/resultados/:resultId" element={<ResultPage />} />
      <Route path="/rankings" element={<RankingsPage />} />
      <Route path="/pistas/:track" element={<TrackPage />} />
      <Route path="/carros/:car" element={<CarPage />} />
      <Route path="/pilotos/:driverId/vs/:otherId" element={<HeadToHeadPage />} />
      <Route path="/pilotos/:driverId" element={<DriverPage />} />
      <Route path="/perfil" element={<RequireAuth><MyProfilePage /></RequireAuth>} />
      <Route path="/vincular/retorno" element={<RequireAuth><SteamReturnPage /></RequireAuth>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
