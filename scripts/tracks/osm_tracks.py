"""Traçados das pistas do LMU a partir do OpenStreetMap — método do grafo.

Uso: python -I -u scripts/tracks/osm_tracks.py <pasta-de-cache> [pista ...]  (grava <cache>/tracks2.json)
Para cada pista: baixa a área do autódromo (tamanho pelo comprimento oficial), monta conjuntos de trechos
candidatos (cada relação type=circuit da área e, à parte, todos os highway=raceway sem pit/kart), divide os trechos
nos cruzamentos e procura o LAÇO FECHADO cujo comprimento mais se aproxima do oficial. Isso resolve relações com
trechos extras (pit lane, traçados antigos) e pistas sem relação. Dados © OpenStreetMap contributors (ODbL).
"""
import json
import math
import os
import sys
import time
import urllib.request

UA = 'PitwallTrackOutlines/1.0 (one-off fetch of race circuit outlines)'
API = 'https://api.openstreetmap.org/api/0.6'

# nome exibido -> (lat, lon de um ponto na reta principal, comprimento oficial em km)
TRACKS = {
    'Interlagos': (-23.7036, -46.6997, 4.309),
    'Fuji': (35.3717, 138.9273, 4.563),
    'Le Mans': (47.9497, 0.2245, 13.626),
    'Monza': (45.6156, 9.2811, 5.793),
    'Spa-Francorchamps': (50.4372, 5.9714, 7.004),
    'Long Beach': (33.7650, -118.1890, 3.167),
    'Laguna Seca': (36.5843, -121.7535, 3.602),
    'Portimão': (37.2270, -8.6267, 4.653),
    'Silverstone': (52.0719, -1.0156, 5.891),
    'Bahrain': (26.0325, 50.5106, 5.412),
    'Imola': (44.3439, 11.7167, 4.909),
    'COTA': (30.1328, -97.6411, 5.513),
    'Sebring': (27.4547, -81.3483, 6.019),
    'Lusail': (25.4900, 51.4542, 5.419),
    'Barcelona': (41.5700, 2.2611, 4.657),
    'Paul Ricard': (43.2506, 5.7917, 5.842),
    'Road Atlanta': (34.1500, -83.8150, 4.088),
    'Daytona': (29.1850, -81.0705, 5.73),
}


EXTRA_RELATIONS = {'Long Beach': [18052024]}


def get(url, cache_dir):
    key = url.replace('https://', '').replace('/', '_').replace('?', '_').replace('=', '_').replace(',', '_')[:180]
    name = os.path.join(cache_dir, key)
    if os.path.exists(name):
        return json.load(open(name, encoding='utf-8'))
    req = urllib.request.Request(url, headers={'User-Agent': UA, 'Accept': 'application/json'})
    last = None
    for _ in range(3):
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                data = json.loads(r.read().decode('utf-8'))
            json.dump(data, open(name, 'w', encoding='utf-8'))
            time.sleep(1.2)
            return data
        except Exception as e:  # noqa: BLE001
            last = e
            if '400' in str(e):
                break
            print('   erro', e)
            time.sleep(4)
    raise RuntimeError(f'{url}: {last}')


def get_area(x1, y1, x2, y2, cache, depth=0):
    """Área do mapa; se passar do limite de nós da API (400), divide em 4 e junta (até 3 níveis)."""
    url = f'{API}/map.json?bbox={x1:.5f},{y1:.5f},{x2:.5f},{y2:.5f}'
    try:
        return get(url, cache)
    except RuntimeError as e:
        if '400' not in str(e) or depth >= 3:
            print('   ', str(e)[:140])
            return None
    mx, my = (x1 + x2) / 2, (y1 + y2) / 2
    seen, elements = set(), []
    for bx in ((x1, y1, mx, my), (mx, y1, x2, my), (x1, my, mx, y2), (mx, my, x2, y2)):
        part = get_area(*bx, cache, depth + 1)
        if part is None:
            return None
        for el in part['elements']:
            k = (el['type'], el['id'])
            if k in seen:
                continue
            seen.add(k)
            elements.append(el)
    return {'elements': elements}


def hav(a, b):
    R = 6371.0
    la1, lo1, la2, lo2 = map(math.radians, (a[0], a[1], b[0], b[1]))
    h = math.sin((la2 - la1) / 2) ** 2 + math.cos(la1) * math.cos(la2) * math.sin((lo2 - lo1) / 2) ** 2
    return 2 * R * math.asin(math.sqrt(h))


def plen(pts):
    return sum(hav(pts[i], pts[i + 1]) for i in range(len(pts) - 1))


def excluded(tags):
    n = (tags.get('name') or '').lower()
    return (tags.get('sport') in ('karting', 'drag_racing', 'motocross') or 'pit' in n or 'kart' in n
            or 'paddock' in n or tags.get('service') in ('pit_lane', 'parking_aisle', 'driveway')
            or tags.get('raceway') in ('pit_lane', 'paddock', 'grid') or tags.get('area') == 'yes')


def best_cycle(way_nodes, coords, km):
    """way_nodes: listas de ids de nós. Divide nos cruzamentos, monta o grafo e devolve o laço mais perto de km."""
    use = {}
    for w in way_nodes:
        for n in w:
            use[n] = use.get(n, 0) + 1
    edges = []  # (a, b, [nós])
    for w in way_nodes:
        w = [n for n in w if n in coords]
        if len(w) < 2:
            continue
        cur = [w[0]]
        for n in w[1:]:
            cur.append(n)
            if use.get(n, 0) > 1 or n == w[-1]:
                if len(cur) >= 2 and cur[0] != cur[-1]:
                    edges.append((cur[0], cur[-1], cur))
                elif len(cur) >= 3:  # trecho que já é um laço sozinho
                    edges.append((cur[0], cur[-1], cur))
                cur = [n]
    adj = {}
    for i, (a, b, ns) in enumerate(edges):
        L = plen([coords[n] for n in ns])
        adj.setdefault(a, []).append((i, b, L))
        adj.setdefault(b, []).append((i, a, L))

    target = km
    best = None
    limit = km * 1.3
    t0 = time.time()

    def dfs(start, node, used, path, length):
        nonlocal best
        if time.time() - t0 > 20:
            return
        for i, nxt, L in adj.get(node, []):
            if i in used:
                continue
            nl = length + L
            if nl > limit:
                continue
            if nxt == start and nl > km * 0.6:
                score = abs(nl - target)
                if best is None or score < best[0]:
                    best = (score, list(path) + [(i, nxt)], nl)
                continue
            if any(nxt == p[1] for p in path):
                continue
            used.add(i)
            path.append((i, nxt))
            dfs(start, nxt, used, path, nl)
            path.pop()
            used.discard(i)

    # laço de um trecho só
    for i, (a, b, ns) in enumerate(edges):
        if a == b:
            L = plen([coords[n] for n in ns])
            if L > km * 0.6 and (best is None or abs(L - target) < best[0]):
                best = (abs(L - target), [(i, a)], L)
    starts = sorted(adj, key=lambda n: -len(adj[n]))  # todos: os de mais ligações podem estar só nas pistas auxiliares
    for s in starts:
        dfs(s, s, set(), [], 0.0)
    if not best:
        return None
    # monta a sequência de pontos do laço
    pts = []
    node = None
    for k, (i, nxt) in enumerate(best[1]):
        a, b, ns = edges[i]
        seq = ns if (nxt == b) else list(reversed(ns))
        if k == 0:
            node = seq[0]
        pts.extend([coords[n] for n in (seq if not pts else seq[1:])])
    return pts, best[2]


def rdp(pts, eps):
    if len(pts) < 3:
        return pts
    a, b = pts[0], pts[-1]
    best, idx = 0.0, 0
    for i in range(1, len(pts) - 1):
        d = seg_dist(pts[i], a, b)
        if d > best:
            best, idx = d, i
    if best > eps:
        return rdp(pts[:idx + 1], eps)[:-1] + rdp(pts[idx:], eps)
    return [a, b]


def seg_dist(p, a, b):
    (x, y), (x1, y1), (x2, y2) = p, a, b
    dx, dy = x2 - x1, y2 - y1
    if dx == dy == 0:
        return math.hypot(x - x1, y - y1)
    t = max(0, min(1, ((x - x1) * dx + (y - y1) * dy) / (dx * dx + dy * dy)))
    return math.hypot(x - (x1 + t * dx), y - (y1 + t * dy))


def main():
    cache = sys.argv[1]
    only = set(sys.argv[2:])
    os.makedirs(cache, exist_ok=True)
    outp = os.path.join(cache, 'tracks2.json')  # entrada do tracks_to_ts.py
    result = json.load(open(outp, encoding='utf-8')) if os.path.exists(outp) else {}
    for name, (lat, lon, km) in TRACKS.items():
        if only and name not in only:
            continue
        print('==', name, flush=True)
        span = max(1.0, km * 0.42)  # meia largura da área, em km
        dlat = span / 111.0
        dlon = span / (111.0 * math.cos(math.radians(lat)))
        area = get_area(lon - dlon, lat - dlat, lon + dlon, lat + dlat, cache)
        if area is None:
            print('   área falhou')
            continue
        coords = {e['id']: (e['lat'], e['lon']) for e in area['elements'] if e['type'] == 'node'}
        ways = {e['id']: e for e in area['elements'] if e['type'] == 'way'}
        sets = []
        for r in area['elements']:
            if r['type'] == 'relation' and r.get('tags', {}).get('type') == 'circuit':
                members = [ways[m['ref']]['nodes'] for m in r['members'] if m['type'] == 'way' and m['ref'] in ways]
                if members:
                    sets.append((f"relação {r['id']} {r['tags'].get('name')!r}", r['id'], members))
        # Relações conhecidas que não são type=circuit (circuito de rua mapeado como rota).
        for rid in EXTRA_RELATIONS.get(name, []):
            full = get(f'{API}/relation/{rid}/full.json', cache)
            for el in full['elements']:
                if el['type'] == 'node':
                    coords[el['id']] = (el['lat'], el['lon'])
            fw = {el['id']: el for el in full['elements'] if el['type'] == 'way'}
            rel = next(el for el in full['elements'] if el['type'] == 'relation' and el['id'] == rid)
            members = [fw[m['ref']]['nodes'] for m in rel['members'] if m['type'] == 'way' and m['ref'] in fw]
            sets.append((f"relação {rid} {rel.get('tags', {}).get('name')!r}", rid, members))
        race = [w['nodes'] for w in ways.values() if w.get('tags', {}).get('highway') == 'raceway' and not excluded(w['tags'])]
        if race:
            sets.append(('trechos raceway', None, race))
        best = None
        for label, rid, members in sets:
            found = best_cycle(members, coords, km)
            if not found:
                print(f'   {label}: sem laço')
                continue
            pts, L = found
            print(f'   {label}: laço de {L:.3f} km (oficial {km})')
            score = abs(L - km) - (0.05 if rid else 0)  # prefere relação em empate técnico
            if best is None or score < best[0]:
                best = (score, label, rid, pts, L)
        if not best or best[0] > km * 0.08:
            print('   nenhum laço confiável (>8% do comprimento oficial)')
            continue
        _, label, rid, pts, L = best
        lat0 = sum(p[0] for p in pts) / len(pts)
        xy = [((p[1] - lon) * 111320 * math.cos(math.radians(lat0)), -(p[0] - lat) * 110540) for p in pts]
        simp = rdp(xy, 4.0)
        result[name] = {'relation': rid or 0, 'osmName': label, 'km': round(L, 3), 'officialKm': km,
                        'closesWithinM': 0, 'points': [[round(x, 1), round(y, 1)] for x, y in simp]}
        print(f'   escolhido: {label} · {len(simp)} pts', flush=True)
        json.dump(result, open(outp, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print('pistas:', len(result), '/', len(TRACKS))


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    main()
