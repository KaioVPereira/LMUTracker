import type { CapacitorConfig } from '@capacitor/cli'

// O `cap sync` avalia este arquivo em Node, então dá para variar por build.
// CAP_DEV=1 é setado só pelo script mobile:sync:dev (veja package.json).
const isDevBuild = process.env.CAP_DEV === '1'

const config: CapacitorConfig = {
  appId: 'com.gamestrackers.pitwall',
  appName: 'Pitwall',
  // Sem `server.url`: o front vai empacotado no APK e chama a Api de produção pelo endereço completo
  // (ver src/api/client.ts), igual ao Finanças.
  webDir: 'dist',

  server: {
    // O login da Steam (vincular a conta) abre DENTRO do app e volta para https://localhost/vincular/retorno, onde
    // o token da sessão já está. Qualquer outro endereço externo abre no navegador do celular (ex.: baixar o APK).
    allowNavigation: ['steamcommunity.com'],
  },

  android: {
    // O WebView serve os assets de https://localhost — é essa a Origin que a Api enxerga e libera no CORS.
    androidScheme: 'https',
    // Só no build de dev: a página vem de https://localhost e a Api local é http (via `adb reverse`), o que seria
    // mixed content bloqueado. Em release fica desligado.
    allowMixedContent: isDevBuild,
    backgroundColor: '#070b16',
  },

  plugins: {
    // Plugin nativo do Capacitor 8: desenha de ponta a ponta (edge-to-edge) e injeta --safe-area-inset-* no CSS.
    // O site é sempre escuro, então os ícones das barras ficam claros.
    SystemBars: {
      style: 'DARK',
      insetsHandling: 'css',
      initialViewportFitValueHint: 'cover',
    },
  },
}

export default config
