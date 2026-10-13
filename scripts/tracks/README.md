# Traçados das pistas (OpenStreetMap)

`frontend/src/lib/trackMaps.ts` é gerado por estes dois scripts, a partir das relações `type=circuit` (e dos trechos
`highway=raceway`) do OpenStreetMap. Dados © OpenStreetMap contributors, licença ODbL — o site mostra o crédito.

    python -I -u scripts/tracks/osm_tracks.py <pasta-de-cache> [pista ...]
    python -I scripts/tracks/tracks_to_ts.py <pasta-de-cache>/tracks2.json frontend/src/lib/trackMaps.ts

- Pista nova: adicione em `TRACKS` (nome exibido no site, um ponto na reta principal, comprimento oficial em km).
  O script escolhe o laço fechado de comprimento mais perto do oficial (descarta acima de 8% de diferença).
- A pasta de cache guarda as respostas da API (rodar de novo não repete chamadas). A API pede uso gentil: o script
  espera ~1 s entre chamadas.
- Le Mans (Circuit de la Sarthe) ainda não tem traçado: a relação do circuito completo não foi encontrada no OSM
  (só a do Circuit Bugatti) — a página mostra a corrida sem mapa.
