# Sandbox Ship

Jeu de stratégie spatiale 2D en temps réel, en bac à sable. Chaque camp a une station
améliorable, des mines sur les astéroïdes, des hangars qui lancent des chasseurs et un
chantier naval. On gagne en détruisant la station adverse, point d'attache par point
d'attache.

## Lancer

```bash
npm install
npm run dev        # http://localhost:5173
npm test           # tests de la simulation
npm run build      # vérification des types + build de production dans dist/
```

Les commandes du jeu sont dans le bouton « Aide ».

## Ajouter les sprites

Sans pack importé, le jeu dessine des formes provisoires. Pour les vrais sprites :

1. Télécharger les packs (gratuits, CC0) depuis itch.io :
   - Foozle, Void - Fleet Pack 1, 2 et 3 : <https://foozlecc.itch.io/void-fleet-pack-1>,
     <https://foozlecc.itch.io/void-fleet-pack-2>, <https://foozlecc.itch.io/void-fleet-pack-3>
   - Foozle, Void - Environment Pack : <https://foozlecc.itch.io/void-environment-pack>
   - zintoki, Space Breaker : <https://zintoki.itch.io/space-breaker>
2. Déposer les `.zip` tels quels dans `assets/raw/`. Un dossier déjà extrait marche aussi.
3. Lancer `npm run import-assets`.

Le script extrait les archives avec `7z`, copie les PNG dans `public/assets/` et écrit
`public/assets/manifest.json`. Une bande horizontale de frames carrées devient une
animation. Si un fichier est mal découpé, corrige-le dans `assets/overrides.json` :
`{ "<clé du manifest>": { "frameW": 64, "frameH": 64 } }`.

Le jeu trouve les sprites par mots-clés dans le nom des fichiers, par exemple
`klaed` + `fighter` + `base` (voir `src/render/sprites.ts`). Ce qui ne correspond à rien
reste en forme provisoire.

## Organisation

- `src/data/` : statistiques des vaisseaux, de la station et des structures.
- `src/sim/` : simulation en TypeScript pur, à pas fixe, sans Phaser. Les tests et la
  future carte galactique s'en servent directement.
- `src/scenes/`, `src/render/`, `src/input/`, `src/ui/` : rendu Phaser, entrées et HUD en HTML.
