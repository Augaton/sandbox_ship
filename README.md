# Sandbox Ship

Jeu de stratégie spatiale 2D en temps réel, en bac à sable. Trois modes :

- **Campagne galactique** : 14 planètes reliées par des routes. On y gagne des crédits,
  on bâtit des stations, des mines et des plateformes, on produit des vaisseaux et on
  déplace ses flottes. Quand deux camps se rencontrent sur une planète, la bataille se
  commande dans la vue tactique ou se résout seule. On perd quand on n'a plus de planète.
- **Escarmouche** : une seule bataille contre l'IA. Chaque camp a une station améliorable,
  des mines sur les astéroïdes, des hangars qui lancent des chasseurs et un chantier naval.
  On gagne en détruisant la station adverse, point d'attache par point d'attache.
- **Bac à sable** : la même bataille, IA coupée, placement libre de n'importe quelle unité.

Les croiseurs et les cuirassés ont des pièces destructibles, comme les stations :
tourelles, réacteurs (le vaisseau ralentit), générateur de bouclier, hangar. N'importe quel
vaisseau se pilote à la main (touche P), et chaque classe a une capacité active (touche E).
Les trois factions ont chacune leur trait et trois héros uniques. La production passe par
deux files, bâtiments et vaisseaux, et la recherche améliore armes, boucliers, blindage,
propulsion, mines et hangars. Plateformes et hangars montent jusqu'au niveau 3, et les
plateformes et docks peuvent se construire près des mines. Un brouillard de guerre couvre
les batailles et la carte, pour l'IA comme pour le joueur. Chaque carte, de campagne ou
d'escarmouche, se génère à partir d'une graine (la carte classique reste disponible). Sur la
carte, certaines planètes ont un bonus. La campagne se sauvegarde toute seule dans le
navigateur.

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
- `src/data/galaxy.ts` : planètes, routes et règles de la campagne.
- `src/sim/` : simulation en TypeScript pur, à pas fixe, sans Phaser. `galaxy.ts` pour la
  carte, `battle.ts` pour les batailles de planète, qui tournent dans la même simulation
  que l'escarmouche, en accéléré quand elles se résolvent seules.
- `src/scenes/`, `src/render/`, `src/input/`, `src/ui/` : rendu Phaser, entrées et HUD en HTML.
