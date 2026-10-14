// Gera as imagens-fonte que o `capacitor-assets` usa para o ícone e a splash do APK (mesmo esquema do Finanças).
// Desenho: a marca do Pitwall — quadriculado de bandeira em lima sobre o azul-noite do site.
import sharp from 'sharp'
import fs from 'node:fs'
import path from 'node:path'

const out = path.resolve(import.meta.dirname, '../assets')
fs.mkdirSync(out, { recursive: true })

const LIME = '#c8f031'
const INK = '#0b1200'
const BG_FROM = '#14244a'
const BG_TO = '#070b16'
const gradient = `<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${BG_FROM}"/><stop offset="1" stop-color="${BG_TO}"/></linearGradient></defs>`

// Bandeira quadriculada 4×4 num quadro de 1024, cantos arredondados, com leve inclinação como no logo.
const flag = () => {
  const cell = 120, size = cell * 4, x0 = 512 - size / 2, y0 = 512 - size / 2
  let squares = ''
  for (let r = 0; r < 4; r++)
    for (let c = 0; c < 4; c++)
      squares += `<rect x="${x0 + c * cell}" y="${y0 + r * cell}" width="${cell}" height="${cell}" fill="${(r + c) % 2 ? INK : LIME}"/>`
  return `<g transform="rotate(-8 512 512)"><clipPath id="c"><rect x="${x0}" y="${y0}" width="${size}" height="${size}" rx="64"/></clipPath>` +
    `<g clip-path="url(#c)">${squares}</g><rect x="${x0}" y="${y0}" width="${size}" height="${size}" rx="64" fill="none" stroke="${LIME}" stroke-width="16"/></g>`
}

const svg = (body, size = 1024) =>
  Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${size} ${size}">${gradient}${body}</svg>`)

// Ícone adaptativo: o Android recorta a borda, então o desenho encolhe para a zona segura (66%).
const foreground = `<g transform="translate(512 512) scale(0.62) translate(-512 -512)">${flag()}</g>`
const full = `<rect width="1024" height="1024" fill="url(#g)"/><g transform="translate(512 512) scale(0.86) translate(-512 -512)">${flag()}</g>`

await sharp(svg(full)).png().toFile(path.join(out, 'icon-only.png'))
await sharp(svg(foreground)).png().toFile(path.join(out, 'icon-foreground.png'))
await sharp(svg('<rect width="1024" height="1024" fill="url(#g)"/>')).png().toFile(path.join(out, 'icon-background.png'))

// Splash: o ícone arredondado no centro, sobre o fundo do site (o app é sempre escuro).
const splash = svg(`<rect width="2732" height="2732" fill="${BG_TO}"/><g transform="translate(1366 1366) scale(0.42) translate(-512 -512)"><rect width="1024" height="1024" rx="230" fill="url(#g)"/><g transform="translate(512 512) scale(0.86) translate(-512 -512)">${flag()}</g></g>`, 2732)
await sharp(splash).png().toFile(path.join(out, 'splash.png'))
await sharp(splash).png().toFile(path.join(out, 'splash-dark.png'))

console.log('ícones gerados em', out)
