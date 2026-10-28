# Sandbox Ship

Jeu de stratégie spatiale 2D en temps réel, en bac à sable. Trois modes, à deux ou à
trois factions (à trois, chacun se bat contre tous) :

- **Campagne galactique** : une carte de planètes reliées par des routes. On y gagne des
  crédits, on bâtit des stations, des mines et des plateformes, on produit des vaisseaux et
  on déplace ses flottes. Quand deux camps se rencontrent sur une planète, la bataille se
  commande dans la vue tactique ou se résout seule. On perd quand on n'a plus de planète.
- **Escarmouche** : une seule bataille contre l'IA, sur une carte petite, moyenne ou grande.
  Chaque camp a une station améliorable avec deux champs d'astéroïdes juste derrière, des
  hangars qui lancent des chasseurs et un chantier naval. On gagne en détruisant la station
  adverse, point d'attache par point d'attache.
- **Bac à sable** : la même bataille, IA coupée, placement libre de n'importe quelle unité.

Les frégates, croiseurs et cuirassés ont des pièces destructibles, comme les stations :
tourelles, réacteurs (le vaisseau ralentit), générateur de bouclier, hangar. Au sommet, le
Titan (un seul à la fois par faction) porte un rayon spinal ; son sprite est composé au
démarrage à partir du cuirassé et des croiseurs de sa faction (`src/render/capitals.ts`).
N'importe quel vaisseau se pilote à la main (touche P). Chaque classe a une capacité, et
chaque héros un pouvoir à lui (bombardement orbital, dôme de bouclier, camouflage,
impulsion IEM, essaim...). Le panneau de sélection dit ce qu'elle fait et si elle est prête,
active ou en recharge ; E la déclenche, puis un clic la vise si besoin. En mode
« Automatique », les vaisseaux s'en servent seuls.
Éclaireurs et bombardiers sortent du chantier par escouades de trois. On construit autour
de la station et des avant-postes : un avant-poste se pose loin de la base, là où un de tes
vaisseaux tient la position hors combat, et ouvre une nouvelle zone de construction. Une
mine se pose sur un champ dans une zone, ou tenu par un de tes vaisseaux, jamais en plein
combat. L'IA s'adapte : elle répond à ce qu'elle a vu (frégates anti-chasseurs contre les
essaims, torpilleurs contre les gros vaisseaux), se replie quand elle est dépassée et
attaque quand elle a l'avantage. Trois difficultés changent ses revenus et sa réactivité.
Confort : une alerte signale les attaques et les pertes hors de vue (Retour arrière y
mène), les groupes et les vaisseaux inactifs sont à un clic, Maj + clic commande cinq
vaisseaux, un clic droit met une classe en production en boucle ou annule un élément de
file. Les sons sont synthétisés par le jeu (`src/audio/sfx.ts`, touche M pour couper) ;
explosions, impacts sur les boucliers, fumée et traînées rendent les combats lisibles.
Échap met en pause (options, commandes, recommencer) ; la fin d'une bataille montre les
statistiques de chaque faction et propose de rejouer la même carte ou une nouvelle. Le menu
montre un aperçu de la carte avant de lancer. En campagne, une flotte garde ses dégâts
d'une bataille à l'autre et se répare à quai sur une planète alliée, plus vite avec une
station ou un dock de réparation.
Les trois factions ont chacune leur trait et trois héros uniques. La production passe par
deux files, bâtiments et vaisseaux, et la recherche améliore armes, boucliers, blindage,
propulsion, mines et hangars. Plateformes et avant-postes montent jusqu'au niveau 3, et les
plateformes et docks peuvent se construire près des mines. En vue éloignée, les petits
vaisseaux deviennent des chevrons et les tirs des traits. Un brouillard de guerre couvre
les batailles et la carte, pour l'IA comme pour le joueur. Chaque carte, de campagne ou
d'escarmouche, se génère à partir d'une graine (la carte classique reste disponible). Sur la
carte, certaines planètes ont un bonus. La campagne se sauvegarde toute seule dans le
navigateur.

## Lancer

```bash
npm install
npm run play       # la dernière version publiée : http://localhost:4173
npm run dev        # la version en cours de développement : http://localhost:5173
npm test           # tests de la simulation
npm run balance    # l'IA contre elle-même, toutes les paires de factions (quelques minutes)
npm run build      # vérification des types + build de production dans dist/
```

Les commandes du jeu sont dans le menu (« Commandes ») et dans le bouton « Aide ». La vue
se déplace avec ZQSD (WASD en QWERTY), les flèches ou les bords de l'écran.

## Versions : dev et main

Deux branches git :

- `dev` reçoit le travail en cours ; `npm run dev` le lance.
- `main` ne contient que des versions finies. Elle ne bouge qu'à la publication d'une
  nouvelle version.

`npm run play` sert le dossier `release/`, qui ne change qu'à la publication. Le travail
sur `dev` n'y touche donc pas. Pour publier une version :

```bash
git switch main
git merge dev
npm run release    # tests, types, puis build dans release/ (refuse hors de main)
git tag v0.3.0     # avec le numéro de package.json, monté sur dev avant la fusion
git switch dev
```

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
