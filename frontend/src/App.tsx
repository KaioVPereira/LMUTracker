import { Navigate, Route, Routes } from 'react-router-dom'
import { RequireAuth } from './components/RequireAuth'
import { DashboardPage } from './pages/DashboardPage'
import { DriverPage } from './pages/DriverPage'
import { HeadToHeadPage } from './pages/HeadToHeadPage'
import { LoginPage } from './pages/LoginPage'
import { MyProfilePage } from './pages/MyProfilePage'
import { RacePage } from './pages/RacePage'
import { RacesPage } from './pages/RacesPage'
import { ResultPage } from './pages/ResultPage'
import { SteamReturnPage } from './pages/SteamReturnPage'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/" element={<RequireAuth><DashboardPage /></RequireAuth>} />
      <Route path="/corridas" element={<RequireAuth><RacesPage /></RequireAuth>} />
      <Route path="/corridas/:eventId" element={<RequireAuth><RacePage /></RequireAuth>} />
      <Route path="/resultados/:resultId" element={<RequireAuth><ResultPage /></RequireAuth>} />
      <Route path="/perfil" element={<RequireAuth><MyProfilePage /></RequireAuth>} />
      <Route path="/pilotos/:driverId/vs/:otherId" element={<RequireAuth><HeadToHeadPage /></RequireAuth>} />
      <Route path="/pilotos/:driverId" element={<RequireAuth><DriverPage /></RequireAuth>} />
      <Route path="/vincular/retorno" element={<RequireAuth><SteamReturnPage /></RequireAuth>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
