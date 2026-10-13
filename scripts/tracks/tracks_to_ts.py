"""tracks.json (do osm_tracks.py) -> src/lib/trackMaps.ts. Uso: python -I tracks_to_ts.py <tracks.json> <saida.ts>"""
import json
import math
import sys

src, out = sys.argv[1], sys.argv[2]
data = json.load(open(src, encoding='utf-8'))
SIZE = 1000  # maior lado do viewBox
PAD = 30

entries = []
for name, t in sorted(data.items()):
    pts = t['points']
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    minx, maxx, miny, maxy = min(xs), max(xs), min(ys), max(ys)
    w, h = maxx - minx, maxy - miny
    k = (SIZE - 2 * PAD) / max(w, h)
    vw, vh = round(w * k + 2 * PAD), round(h * k + 2 * PAD)
    proj = [((x - minx) * k + PAD, (y - miny) * k + PAD) for x, y in pts]
    closes = t['closesWithinM'] <= 60
    d = 'M' + ' L'.join(f'{x:.0f} {y:.0f}' for x, y in proj) + (' Z' if closes else '')
    # largada: ponto do traçado mais perto do ponto de referência (origem da projeção, na reta principal)
    sx, sy = min(pts, key=lambda p: math.hypot(p[0], p[1]))
    start = [round((sx - minx) * k + PAD), round((sy - miny) * k + PAD)]
    entries.append((name, d, f'0 0 {vw} {vh}', start, t))

with open(out, 'w', encoding='utf-8', newline='\n') as f:
    f.write('// GERADO por scripts de traçado a partir do OpenStreetMap (relações type=circuit).\n')
    f.write('// Dados © OpenStreetMap contributors, licença ODbL — https://www.openstreetmap.org/copyright\n')
    f.write('// Não editar à mão: regere com scripts/tracks/osm_tracks.py + tracks_to_ts.py.\n\n')
    f.write('export interface TrackOutline {\n  /** Caminho SVG do traçado (coordenadas do viewBox). */\n  d: string\n  viewBox: string\n'
            '  /** Ponto da largada/chegada (aproximado: o mais perto da reta principal). */\n  start: [number, number]\n'
            '  /** Escala de traço sugerida para o tamanho do viewBox. */\n  r: number\n  /** Relação no OSM e comprimento medido (km). */\n'
            '  osmRelation: number\n  km: number\n}\n\n')
    f.write('export const TRACK_MAPS: Record<string, TrackOutline> = {\n')
    for name, d, vb, start, t in entries:
        f.write(f'  {json.dumps(name, ensure_ascii=False)}: {{\n')
        f.write(f'    d: {json.dumps(d)},\n')
        f.write(f'    viewBox: {json.dumps(vb)},\n')
        f.write(f'    start: [{start[0]}, {start[1]}],\n')
        f.write(f'    r: 9,\n')
        f.write(f'    osmRelation: {t["relation"]},\n')
        f.write(f'    km: {t["km"]},\n')
        f.write('  },\n')
    f.write('}\n')
sys.stdout.reconfigure(encoding='utf-8')
print('pistas geradas:', len(entries))
for name, d, vb, start, t in entries:
    print(f'  {name}: {t["osmName"]!r} {t["km"]} km (oficial {t["officialKm"]}), fecha a {t["closesWithinM"]} m, {d.count("L") + 1} pts')
