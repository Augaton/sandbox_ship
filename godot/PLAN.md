# Portage vers Godot (jeu natif)

La version web (TypeScript + Phaser, v0.2) est gelée : elle reste jouable (`npm run play`)
et sert de référence. Tout le travail neuf se fait ici, en **Godot 4.7 et C#**.

Approche : d'abord un port fidèle, aux mêmes règles, vérifié contre la version TypeScript ;
ensuite seulement, on profite du moteur (particules, shaders, sons, export natif).

## Organisation

```
godot/
  Sim/          bibliothèque C# pure (net10.0), sans Godot : toute la simulation et les données
  Sim.Tests/    tests xUnit de la simulation (dotnet test, sans lancer Godot)
  Game/         projet Godot : rendu, interface, sons, en C# ; référence Sim
  SandboxShip.slnx   solution de travail ; Game/Game.sln sert à l'export de Godot
  build/        exécutables exportés (ignoré par git)
```

La simulation reste indépendante du moteur, comme dans la version web : elle se teste et se
mesure seule, et le rendu ne fait que la lire.

## Vérification

- Les 131 tests TypeScript sont portés un à un.
- Tests croisés TypeScript ↔ C# : même graine, même résultat. Le générateur pseudo-aléatoire
  (mulberry32) donne les mêmes nombres au bit près ; la génération des cartes et les
  premiers pas de simulation sont comparés à une tolérance près (les fonctions
  trigonométriques de V8 et de .NET peuvent différer au dernier bit).
- L'équilibrage se mesure aussi en C# : `BALANCE=1 dotnet test Sim.Tests --filter Balance
  --logger "console;verbosity=detailed"` (36 parties entre IA ; `--filter ModeBalance` pour les modes de bataille).

## Étapes

0. **Outils et squelette** (fait).
1. **Simulation** (fait) : tout `src/sim` et `src/data` porté ; tests portés ; mêmes cartes et,
   avant les changements de règles, mêmes parties que la version web à 10⁻⁹ près.
2. **Bataille jouable** (fait) : sprites des packs (`assets/looks.json`, écrit par
   `npm run fixtures`), vaisseaux composés au démarrage, pièces aux couleurs des factions,
   caméra, sélection et ordres, pilotage, brouillard, effets, niveaux de détail.
3. **Interface de bataille** (fait) : HUD complet, minicarte, bac à sable, dialogues.
4. **Campagne** (fait) : galaxie, panneau, invites de bataille, sauvegarde dans `user://`.
5. **Menu, options, sons** (fait) : menu complet, options, sons synthétisés au démarrage.
   Reste : touches reconfigurables.
6. **Export natif** (fait) : Linux et Windows, icône, version 0.2.0. Windows non essayé.
   Passe d'optimisation faite : `--start=stress` mesure une bataille de 1 300 à 1 500 entités.
7. **Au-delà du port** (en cours) : voir ci-dessous.

## Règles propres à cette version

La version web reste la référence du port, mais ces règles s'en écartent volontairement ; les
tests de `FixtureTests` et `CrossTests` les listent.

- Les vaisseaux ordonnés ensemble avancent en flotte : au pas du plus lent, les traînards
  rattrapent, les vaisseaux de ligne devant, petits et soutiens derrière.
- Les appareils lancés escortent leur porteur et attaquent sa cible.
- Nouvelles classes : Rempart (tank, « Provocation »), Transporteur (deux grands hangars,
  « Catapultes »), Léviathan (au-dessus du Titan, station niveau 4, un seul).
- Deux Titans au plus. Les gros vaisseaux ont plusieurs capacités (cuirassé 2, Titan 3,
  Léviathan 4) et sortent du chantier par un portail ; Titan et Léviathan, avec une trompe.
- Les chasseurs se construisent aussi au chantier (4 pour 300).
- Recherche : il faut un laboratoire ; l'escarmouche en donne un derrière chaque station.
- Plateformes : 3 autour de la station, 2 par avant-poste, 1 par mine ; leur aspect suit leur
  niveau.
- Station : quatrième niveau, plus solide à chaque niveau ; améliorations bien plus chères
  (4 000, 9 000, 18 000). L'IA épargne la moitié de son revenu pour le niveau suivant et attaque
  par vagues plus grosses : parties d'environ 24 min en médiane entre IA, jusqu'à près d'une heure.
- HUD : les choix pas encore débloqués sont masqués ; le prix reste affiché, en rouge s'il
  manque des crédits.
- Raid : en escarmouche (case du menu, cochée par défaut), un Colosse neutre (faction des
  Errants, non jouable) garde le point de la carte le plus loin des stations. Il frappe quiconque
  approche ; ses quatre générateurs de bouclier sont la clé. Le premier à l'abattre gagne
  8 000 crédits et un niveau de recherche. L'IA ne le chasse pas encore.
- Rempart, Transporteur, Titan, Léviathan et Colosse ont des sprites originaux, dessinés au démarrage
  par `Render/ShipForge.cs` dans la manière des packs (relief éclairé ramené à leurs palettes),
  avec leurs réacteurs, leur destruction et un socle sous chaque tourelle.
- Les Errants sont une vraie faction, toujours neutre et non jouable : trait « Gardiens » (bouclier et
  coque ×1,2, vitesse ×0,8) et une flotte entière à eux (toutes les classes), forgée par `ShipForge`
  dans une palette bronze, or et turquoise.
- Bouclier rompu : un bouclier tombé à zéro ne se recharge qu'après un délai selon la taille
  (3 s petite, 4 s moyenne, 6 s grande), structures comprises.
- Primes : abattre un vaisseau rapporte 20 % de sa valeur (le coût pour un héros) au dernier tireur,
  dans tous les modes.
- Campagne : trois tailles de galaxie, petite (1300 × 850, 10 à 12 planètes), moyenne (celle de la
  version web) et grande (2200 × 1350, 22 à 30 planètes) ; la galaxie classique garde la sienne.
  L'IA galactique ne reste plus chez elle quand les Errants tiennent le seul passage vers l'ennemi,
  et ne rappelle plus l'éclaireur parti prendre une planète libre derrière elle.
- Campagne : deux flottes ennemies qui se croisent sur une voie s'arrêtent là où elles se
  rencontrent et se battent (interception) ; aucune ne passe derrière les lignes de l'autre. Le
  vainqueur reprend sa route ; après une impasse, les deux font demi-tour. Aucune planète n'est en
  jeu, et une partie sauvegardée pendant le combat le reprend au chargement.
- Recherche : quatre techs de plus, Logistique (vaisseaux construits 7 % plus vite par niveau),
  Réparation (+25 % : soutiens, docks, flottes au repos), Défenses (+10 % de dégâts des stations,
  plateformes et de leurs tourelles) et Énergie (capacités rechargées 6 % plus vite). Armement,
  Boucliers et Blindage montent au niveau 5, Hangars restent à 3, les autres vont à 4 ; les
  niveaux 4 et 5 demandent une station niveau 4.
- Économie : les gros vaisseaux coûtent bien plus cher et se construisent plus lentement (croiseur
  2 000 cr / 34 s, Rempart 2 600 / 40 s, Transporteur 3 600 / 50 s, cuirassé 4 800 / 60 s, Titan
  14 000 / 120 s, Léviathan 32 000 / 200 s) ; les petits gardent leur prix et restent la réponse
  économique plus longtemps.
- Fin de partie : la Lance stellaire (station niveau 4, 30 000 cr, une par faction) charge 4 min
  une fois construite, puis frappe n'importe où sur la carte après 5 s d'alerte visible de tous :
  9 000 dégâts dans un grand rayon. Sa charge se voit autour d'elle et en haut de l'écran ; la
  détruire l'arrête. L'IA la construit et vise ce qui vaut le plus. Le Colosse devient le vaisseau
  ultime (station niveau 4, 60 000 cr, un seul) ; seul celui des Errants paie la prime de raid.
- Campagne, Lance stellaire : bâtiment de planète (station niveau 4, 30 000 cr, une par faction),
  qui charge 5 min puis frappe une planète ennemie à 3 voies au plus : plateformes, canon ionique
  et dock détruits, station abaissée d'un niveau, une mine perdue, flottes présentes réduites de
  moitié. Elle tombe avec sa planète. L'IA en construit une une fois riche et tire dès qu'elle est
  chargée sur la planète rivale la mieux défendue à portée.
- Campagne, contrôle de la carte : zoom (molette, + et -) et déplacement (ZQSD, flèches, clic
  molette, bords), H pour tout revoir ; une flotte en route se sélectionne, se redirige (demi-tour
  si plus court) ou s'arrête à la prochaine planète ; point de ralliement par planète pour les
  vaisseaux neufs ; listes Flottes, Empire (revenus, files, planètes inactives) et Journal (F, E, J).
- Campagne, ravitaillement : une flotte loin de son territoire (ni sur une planète à elle ou voisine,
  ni sur une voie qui y touche) perd 10 % d'état par minute, jusqu'à 30 % ; la régénération
  Nautolan s'y arrête aussi. Les Errants n'en souffrent pas.
- Campagne, spécialités nouvelles sur les galaxies générées (jamais la classique) : Nébuleuse (les
  flottes y sont invisibles à l'ennemi), Relais hyperspatial (flottes du propriétaire ×1,5 sur les
  voies qui en partent), Ruines anciennes (1 500 cr et un niveau de recherche au premier
  propriétaire). Tirées à part : la génération de la galaxie reste celle de la version web.
- Campagne, événements toutes les 3 à 5 min (le premier vers 4-6 min) : flotte abandonnée à
  récupérer, bande errante qui frappe une planète frontalière (détruit ses mines sans la prendre),
  tempête ionique (une voie à ×0,5 pendant 3 min), essor commercial (revenu ×2 pendant 4 min).
- Campagne, diplomatie (touche T) : trêves de 10 min, sans combat ni envoi de flotte entre les deux
  camps. Une IA accepte quand elle se sent la plus faible ou qu'elle se bat déjà ailleurs, et
  peut proposer elle-même ; rompue, elle refuse toute trêve pendant 15 min. Jamais avec les
  Errants.
- Campagne, IA : offensives groupées (rassemblement puis attaque à 1,4 fois la défense, 1,1 après
  6 min d'attente), renforts vers une planète menacée, récupération des épaves, retour des flottes
  usées. Parties entre IA : toutes finies, médiane 14 min.
- Performances (2026-10-30) : la simulation n'était pas optimisée dans le jeu exporté (Godot
  compile en « ExportRelease », configuration que `Sim.csproj` ne connaissait pas) ; corrigé, une
  très grosse bataille passe de 7,9 à 2 ms de simulation par image. Les objets de calcul réutilisés
  par le mouvement sont propres à chaque fil (des mondes simulés en parallèle se les partageaient :
  tests, préchauffage). Pendant le chargement, un préchauffage simule hors écran une escarmouche,
  un combat et une petite campagne : plus de saccade à la première décision de l'IA.
- Files de production : la barre de progression de l'élément en cours s'affiche de nouveau (elle
  restait vide depuis le portage), avec les secondes restantes.
- Démarrage : le logo du jeu remplace celui de Godot (`boot.png`), puis un écran de chargement
  au même endroit montre la forge des sprites (sur tous les cœurs : 5 s au lieu de 24) et une
  astuce. Le même écran passe en fondu entre deux écrans pendant que le suivant se construit.
- Menu principal : une colonne à gauche, sur une bataille de démonstration (`Sim/Showcase.cs` :
  deux ou trois factions tirées au hasard, Errants compris, flottes aléatoires, caméra qui suit
  l'action, sans son) ; une nouvelle commence quand la précédente est finie. Nouvelle partie,
  options, commandes et crédits s'ouvrent dans un panneau plus large, avec un résumé des choix
  et le bouton de lancement toujours en bas. Les dialogues en jeu apparaissent en fondu.
- L'IA galactique ne reste plus bloquée sur un vaisseau qu'elle ne peut plus construire.
- Campagne : plusieurs planètes se sélectionnent (Maj + clic, rectangle) ; le clic droit y envoie
  toutes leurs flottes, un vaisseau commandé l'est sur chacune, et chaque planète a sa
  production en boucle (clic droit), qui attend les crédits, se perd avec la planète et se sauvegarde.
- Campagne : les Errants tiennent la planète la plus isolée des capitales (station niveau 3, plateformes
  niveau 3, canon ionique, dock, le Colosse et son escorte) ; ils ne construisent ni ne bougent, ne comptent
  pas pour la victoire, et leur planète se prend comme une autre. L'IA ne les attaque que nettement plus
  forte. Les primes gagnées en bataille (Colosse compris, avec son niveau de recherche) vont au trésor galactique.
- Camps, races et équipes (fondation du salon multijoueur) : la simulation sépare le camp (`Side` : qui possède
  quoi, qui combat qui) de la race (`Faction` : traits, prix, héros, sprites, noms). Jusqu'à six camps, deux
  d'une même race possibles ; chacun a sa couleur (celle de sa race pour les camps 0 à 3, puis six couleurs de
  joueur). Les camps d'une même équipe sont alliés : jamais visés ni blessés, frappes et lance comprises ; ils se
  réparent, voient et gagnent ensemble, et en campagne partagent planètes, voies et ravitaillement sans
  combattre. Les Errants (camp 3) restent ennemis de tous. Escarmouche et campagne se créent depuis des places
  (`Slot`) ; les anciennes sauvegardes se chargent. Sans équipes, les parties sont identiques à avant. Galaxies
  générées : 4 planètes de plus par joueur au-delà de trois (petite et moyenne), pour séparer les capitales.
- Nouvelle partie : les modes en cartes (campagne, escarmouche, survie, contrôle de zone, assaut, régicide, bac à
  sable), puis le salon du mode choisi : jusqu'à six places (toi, une IA à sa difficulté, ouverte, fermée ; joueur
  distant viendra avec le réseau), race, équipe et couleur (unique) par place, l'ordre des places donnant les
  positions ; carte, graine et aperçu numéroté ; règles du mode (objectif, durée, raid, brouillard). Le lancement
  reste grisé, avec la raison, tant que la partie ne tient pas. Le salon est une donnée simple (`Sim/Lobby.cs`, prête
  à passer sur le réseau) ; le dernier de chaque mode se garde dans `user://lobby.json`, avec une graine neuve.
- Multijoueur, fondations (pas encore de réseau) : tout ce que fait le joueur, en bataille comme en campagne,
  devient une commande (`Sim/Commands.cs` : données pures, ids et points, sans le camp qui la donne) qu'applique
  `Commands.Apply` : un camp n'ordonne que ce qui est à lui et ne vise que ce que son équipe voit ; une commande
  mal formée est refusée. Chaque écran envoie les siennes à un « puits » (`Sim/CommandSinks.cs`) : appliquées
  sur-le-champ en solo (parties identiques), gardées pour le pas suivant chez un hôte. Les outils du bac à sable
  ne servent qu'au jeu seul (`World.SandboxTools`). Instantanés de bataille (`Sim/Snapshot*.cs`) : ce que voit
  l'équipe d'un joueur, en binaire versionné, relu dans une copie du monde sans rien simuler ; 48 Ko pour la
  bataille de mesure (1 270 entités), 1,6 Ko pour un duel à 5 min.
- Modes de bataille (`Sim/Modes/`), avec un panneau en haut du HUD, leurs alertes et leur texte de fin ; le salon
  vérifie une partie avec `Scenario.Check(options)`, qui dit pourquoi elle ne peut pas se jouer.
- Survie : toutes les places en une équipe contre des vagues d'Errants venues des bords, la première après 2 min 30,
  puis toutes les 90 s, chacune plus grosse et plus lourde, un boss toutes les cinq (cuirassé, Titan, Léviathan,
  Colosse). Repousser la 20e gagne, plus aucune station perd. L'IA défend les stations de toute l'équipe. Entre IA :
  seule, vague 10 à 19 ; à deux, jusqu'à la 20e, sans abattre le Colosse.
- Contrôle de zone : trois balises (le centre, deux points symétriques loin des stations) ; les vaisseaux d'un seul
  camp en prennent une en 12 s (après avoir défait la prise adverse), deux camps la gèlent. Une balise tenue rapporte
  0,1 point/s à son camp tant qu'il a une station ; le premier au score (100) gagne, détruire les stations aussi.
  L'IA envoie ses vaisseaux inactifs à la balise la plus proche qu'elle ne tient pas. Entre IA : 8 à 14 min.
- Assaut : l'équipe 1 tient une forteresse (station niveau 4, trois plateformes niveau 3, canon ionique, 9 vaisseaux),
  l'équipe 2 arrive d'une tête de pont (station niveau 3 nue) avec une flotte de 12 000 cr et 30 cr/s de plus. La
  défense gagne si une station tient au bout du temps (15 min), l'attaque en les détruisant toutes. L'IA défend sans
  sortir ; en attaque, une vague dès 5 000 cr de vaisseaux prêts, tout dans les 3 dernières minutes. Entre IA : 13
  victoires de la défense, 11 de l'attaque sur 24.
- Régicide : chaque camp commence avec son amiral (héros) pour vaisseau amiral, couronné, connu de tous sur la
  minicarte, jamais reconstruit ; le perdre élimine le camp, dont tout s'autodétruit sans prime. La dernière équipe
  avec un vaisseau amiral gagne, les stations ne décident rien. L'IA le garde à 900 de sa station, sur le flanc
  opposé aux ennemis, avec une escorte de 2 à 6 vaisseaux ; ses vagues plongent sur un vaisseau amiral ennemi en vue,
  et après 20 min elle y jette tout. Mort subite à 25 min (annoncée 2 min avant) : les vaisseaux amiraux perdent
  leurs boucliers et plus rien ne répare leur coque. Entre IA : 13 parties sur 15 finies en 60 min (médiane 35 min),
  contre 9 avant tir concentré et mort subite ; les nulles restantes opposent Kla'ed et Nautolan.
- Tir concentré (tous modes) : les tourelles d'un vaisseau tirent sur la cible qu'on lui a ordonné d'attaquer dès
  qu'elle est à leur portée, et non plus sur l'ennemi le plus proche.

## Ce qui change de technique

| Web | Godot |
|---|---|
| Phaser (sprites, caméra, Graphics) | Node2D, Sprite2D, Camera2D, `_Draw()` |
| HUD en HTML/CSS | nœuds Control + Theme |
| Web Audio | AudioStreamPlayer (sons générés ou pré-rendus) |
| localStorage | fichiers dans `user://` |
| Vite, npm, vitest | Godot, dotnet, xUnit |
