import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// Em dev, o front (5173) repassa /api para a Api .NET (5280). Em produção, o mesmo domínio serve os
// dois (o proxy manda /api para a Api), então o front sempre chama caminhos relativos.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: process.env.VITE_API_TARGET ?? 'http://localhost:5280',
        changeOrigin: true,
      },
    },
  },
})
