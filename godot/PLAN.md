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
- Postures par vaisseau (`Stance`, défensive par défaut, le comportement d'avant) : agressive (cherche
  plus loin, poursuit sans laisse, reste où le combat l'a mené), tenir la position (ni poursuite, ni
  esquive, ni repli, cible à portée de tir), passive (ne choisit aucune cible, tire seulement sur ordre
  d'attaque). Ordres : patrouille sur 15 points au plus, en formation et en combattant ; garde ou escorte
  d'un allié (vaisseau ou bâtiment, place derrière lui) ; file d'ordres avec Maj (32 au plus). L'IA
  envoie ses vagues en agressive, sa flotte de garde en défensive, l'escorte d'une mine en tenue.
- Vétérans (`Veterans.cs`) : expérience des points de coque et de bouclier ôtés à l'ennemi et d'une
  demi-robustesse de plus pour le coup de grâce, rapportés à la robustesse du tireur (tourelle et
  appareil comptent pour leur vaisseau) ; ni les mines ni les camps sans faction n'en donnent. Rangs à
  3, 8 et 16 : par rang +5 % de dégâts, +5 % de coque, +10 % de régénération. Le vrai tireur sert aussi
  aux meilleurs vaisseaux de fin de partie. En campagne, chaque flotte garde ses rangs (`RankCounts`) :
  les recrues tombent d'abord, les vaisseaux qui partent emportent les meilleurs.
- Nouvelles classes : Rempart (tank, « Provocation »), Transporteur (deux grands hangars,
  « Catapultes »), Léviathan (au-dessus du Titan, station niveau 4, un seul).
- Vaisseaux capitaux longs (coque en capsule le long du cap : `ShipDef.Beam`, `Worlds.Keel` et
  `Worlds.Gap` ; portée, impacts, collisions, vision, zones des capacités, clic, anneaux, bouclier
  et terrain la suivent), du plus fin au plus massif. Titan : 380 sur 110, une lame à quille
  unique, superstructure en gradins, coques latérales et ailerons à l'arrière. Léviathan : 520 sur
  190, proue fourchue, deux grandes coques latérales portant les bordées, passerelle, 24 pièces, lent
  (vitesse 19, virage 0,11). Colosse : 620 sur 210, trimaran (quille et deux coques extérieures
  reliées par des entretoises), grande fourche à la proue, cœur couronné. Le Colosse du raid est
  couché le long du bord de la carte, flanc vers le centre.
- Cuirassé un cinquième plus grand que dans la version web (rayon 68), ses pièces déplacées et
  agrandies d'autant. Rempart (rayon 50) : un mur, un grand croissant de blindage devant une coque
  ramassée. Transporteur : un pont d'envol éclairé entre deux coques-hangars ouvertes à la proue.
- Terrain : il garde 500 de terrain dégagé autour des vaisseaux du raid, et seulement la place de
  leur coque plus 200 autour des autres vaisseaux posés par un mode (vaisseau amiral du régicide).
- Profondeur : éclaireurs, chasseurs et bombardiers volent au-dessus des autres vaisseaux, sans
  pousser ni être poussés par eux (seulement par les bâtiments). À l'écran, une couche à eux au-dessus
  des vaisseaux, une ombre portée par vaisseau (lumière en haut à gauche, d'autant plus loin qu'il vole
  haut, posée sur les coques survolées), le roulis dans les virages et le décollage des appareils lancés.
- Deux Titans au plus. Les gros vaisseaux ont plusieurs capacités (cuirassé 2, Titan 3,
  Léviathan 4).
- Réserve (`Reserves.cs`) : aucun vaisseau ne sort plus du chantier. Une fois construit, chacun
  attend en réserve (panneau au-dessus des groupes, un bouton par sorte) et se glisse sur la carte,
  ou se place d'un clic (Maj : plusieurs) ; clic droit sur un bouton : tous ceux de cette sorte au
  point de ralliement, chacun sur sa place. Une escadrille arrive d'un bloc, de front. Ils arrivent
  par l'hyperespace près de ses forces : à 900 de ses bâtiments construits, à 600 de ses vaisseaux
  moyens et grands, jamais à moins de 700 d'un bâtiment ennemi ni sur un bâtiment, sur un terrain
  libre. Le saut dure 1,5 s (petits), 2 s (moyens), 3 s (grands), 6 s (Titan, Léviathan, Colosse),
  son point marqué pour tous (silhouette, tunnel de lumière, hologramme). En sortant, le vaisseau
  jaillit de derrière son point, étiré, freine net et laisse des traînées ; l'onde de choc d'une
  grande coque écarte les appareils proches. Un capital arrive autrement : une tempête se forme sur
  son point pendant le saut (nuages sombres qui tournent, éclairs de plus en plus serrés, roulement
  de tonnerre), puis il en sort lentement, la proue d'abord (4,5 s), des éclairs courant sur sa
  coque, dans un éclair, le tonnerre et la trompe, l'écran tremblant longuement ; la tempête se
  dissipe en 6 s. La réserve et les sauts comptent dans les limites (Titans, Léviathan, héros). L'IA
  appelle sa réserve à son point de ralliement (`Reserves.CallAll`). Réseau : commande `Deploy`
  (commandes 3). Tests : `ReserveTests`. `--start=jumps` montre des sauts à chaque stade.
- Quatre classes de plus (instantanés 11, protocole 14), avec des sprites originaux forgés pour
  chaque race : Artillerie (moyenne, 1 400 cr, station 2 ; obus de siège lents à 950 de portée,
  ×2,5 contre les bâtiments, presque rien contre les petits, abattus par la défense ponctuelle ;
  « Tir de barrage »), Corvette (petite, deux pour 450 cr ; deux flaks à défense ponctuelle ;
  « Salve »), Brouilleur (moyen, 900 cr, station 2 ; « Brouillage » : dans une zone de 320, les
  ennemis, bâtiments compris, perdent 40 % de portée et leurs boucliers ne se rechargent plus
  pendant 10 s), Croiseur furtif (moyen, 1 300 cr, station 2 ; dans le brouillard, ses ennemis ne
  le voient qu'à 300 au plus, sauf dans les 3 s qui suivent un tir ; « Camouflage »). L'IA les
  construit (corvettes contre les essaims, artillerie contre les défenses) et l'attaque de l'assaut
  emmène deux artilleries.
- Les chasseurs se construisent aussi au chantier (4 pour 300), et passent par la réserve.
- Recherche : il faut un laboratoire ; l'escarmouche en donne un derrière chaque station.
- Plateformes : 3 autour de la station, 2 par avant-poste, 1 par mine ; leur aspect suit leur
  niveau. Bien plus solides et plus chères que dans la version web : 600 cr et 2 000 de coque, puis
  +900 (3 500), +1 500 (6 000) et un niveau 4, le bastion (+3 000 cr, station niveau 3, 10 000 de
  coque, rayon 34) : une grande plateforme qui porte quatre tourelles destructibles, deux lourdes
  et deux légères, reconstruites comme celles de la station. En escarmouche (sauf assaut et survie),
  chaque station commence avec deux plateformes niveau 3 devant elle, vers le centre de la carte,
  contre les rushs ; la forteresse de l'assaut garde ses trois plateformes niveau 3. Mesures du
  2026-11-17 (`BALANCE=1`, 36 parties entre IA) : d'abord 21 nuls à 60 min (médiane 50 min) ; les
  stations n'étaient jamais touchées, les flottes restaient petites, la moitié du revenu épargnée
  pour la station. Réglage de l'IA : elle retient les défenses ennemies vues tant qu'elles tiennent
  et prend d'autant plus d'artillerie (+1 par défense connue, 6 au plus) : 14 nuls ; passé son heure
  du tout-pour-le-tout, elle n'épargne plus pour sa station : 2 nuls, médiane 44 min, Kla'ed 50 %,
  Nairan 42 %, Nautolan 50 %. Essais écartés : plateformes de départ niveau 2 (4 nuls, Kla'ed
  29 %), et l'armée du tout-pour-le-tout lancée droit sur la station (29 nuls : un suicide).
- Plus de file de bâtiment : l'amélioration de la station et les niveaux des plateformes et des
  avant-postes avancent tous en même temps, un niveau à la fois par bâtiment (sa barre dans son
  panneau et dans l'onglet Bâtiments).
- Avant-postes : à 1 500 au moins de la station et des autres avant-postes du même camp.
- Cartes d'escarmouche : deux tailles de plus, très grande (10 000 × 6 700) et immense
  (13 000 × 8 700) ; les galaxies restent en trois tailles (une plus grande taille de carte donne
  une grande galaxie).
- Les cercles des zones d'arrivée ne s'affichent plus pendant un glisser-déposer : la silhouette,
  verte ou rouge avec la raison, suffit.
- Station : quatrième niveau, plus solide à chaque niveau ; améliorations bien plus chères
  (4 000, 9 000, 18 000). L'IA épargne la moitié de son revenu pour le niveau suivant et attaque
  par vagues plus grosses : parties d'environ 24 min en médiane entre IA, jusqu'à près d'une heure.
- HUD : les choix pas encore débloqués sont masqués ; le prix reste affiché, en rouge s'il
  manque des crédits.
- Raid : en escarmouche (case du menu, cochée par défaut), un Colosse neutre (faction des
  Errants, non jouable) garde le point de la carte le plus loin des stations. Il frappe quiconque
  approche ; ses quatre générateurs de bouclier sont la clé. Le premier à l'abattre gagne
  8 000 crédits et un niveau de recherche. L'IA ne le chasse pas encore.
- Rempart, Transporteur, Titan, Léviathan, Colosse, Artillerie, Corvette, Brouilleur et Croiseur
  furtif ont des sprites originaux, dessinés au démarrage par `Render/ShipForge.cs` dans la manière
  des packs (relief éclairé ramené à leurs palettes), avec leurs réacteurs, leur destruction et un
  socle sous chaque tourelle.
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
  - Alliances : vision partagée, aucun combat, victoire commune (comme une équipe : sans elle, deux
    alliés restés seuls devraient se trahir pour finir). Une IA s'allie à qui elle fait confiance
    (40, ÷ son amitié) ou contre un tiers plus fort que chacun des deux, jamais pour finir la partie
    à deux. Rompue : 2 min de trêve pour séparer les flottes, rancune de 30 min, confiance -70 chez
    la trahie et -15 chez toutes les autres IA.
  - Commerce, en paix seulement : 1 crédit/s chacun, +1 par voie entre leurs planètes, 6 au plus ;
    il s'arrête avec la paix.
  - Tributs, offerts ou exigés : une somme, ou tant par minute pendant 10 min, contre une trêve. Le
    prix d'une IA : rien si elle se sent la plus faible, jusqu'au tiers de son revenu sinon. Un
    payeur qui cesse ou ne peut plus payer rompt la trêve. Une IA n'exige un tribut que d'un joueur :
    entre IA, tribut après tribut, deux IA restées seules ne finissaient plus la partie.
  - Confiance de chaque IA envers chaque camp (-100 à 100) : +3/min en trêve ou en commerce, +5 en
    alliance, -5 par bataille qu'on lui ouvre (pas sous -60 par les seules attaques), revient vers
    0 sinon. Elle ne joue pas sur les trêves : deux IA qui la gagnaient en trêve signaient la
    suivante pour toujours.
  - Réseau : commandes de galaxie 17 à 19, protocole 8, instantané de galaxie 3. Sauvegardes : les
    anciennes se chargent (leurs offres sont des trêves). Tests : `DealsTests`.
- Personnalités d'IA (`Sim/Data/Personalities.cs`), en bataille comme en campagne : équilibrée (l'IA
  d'avant, au calcul près), agressive, défensive, économe, technologique. Au salon, « Au hasard » par
  défaut, tiré de la graine et de la place (le même partout, sans toucher aux tirages du monde) ;
  rediffusions 3. Tests : `PersonalityTests` ; séries IA contre IA : `PersonalityBalanceTests`.
  - Séries du 2026-11-03 (chaque paire, même race des deux côtés, chaque départ tour à tour : 16
    parties par personnalité). Bataille (60 min au plus) : technologique 75 %, défensive 63 %,
    équilibrée 44 %, économe 31 %, agressive 25 % ; 2 nuls ; médiane 38 min. La technologique fait
    jeu égal avec l'équilibrée et bat surtout les trois autres. Avant réglage : agressive 6 %,
    technologique 81 %. Campagne (petite galaxie, 60 min au plus) : équilibrée, agressive et
    technologique 56 %, économe 38 %, défensive 31 % ; 2 nuls ; médiane 31 min. À ±24 points près
    sur 16 parties : la technologique en bataille reste à surveiller.
  - Le 2026-11-17, avec les plateformes de départ, la technologique ne cherchait pas plus que les
    autres contre un joueur passif : sa chance de recherche passe de 0,2 à 0,35. L'IA compte dans sa
    flotte les vaisseaux en réserve et en saut. Séries de personnalités pas encore refaites.
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
- Survie, sans fin depuis la 0.5.0 : toutes les places en une équipe contre des vagues d'Errants venues des bords, la
  première après 2 min 30, puis toutes les 90 s, 2 s de moins à chaque vague jusqu'à 50 s ; une vague battue avant l'heure
  amène la suivante 25 s après. Chacune est plus grosse et plus lourde (titans dès la 16e, léviathans dès la 26e, au plus
  50 vaisseaux : au-delà, ils grossissent) ; un boss toutes les cinq (cuirassé, Titan, Léviathan, Colosse), puis plusieurs
  à la fois ; passé la 20e, la recherche des Errants monte d'un niveau toutes les deux vagues (élite). Une vague sur
  quatre dès la 7e a un thème : essaim, armada lourde, porte-avions. Chaque vague battue paie les places debout
  (200 + 40 par vague). Plus aucune station termine la partie, sur un tableau des scores : un dixième de la valeur des
  Errants abattus, 1 000 par boss, 100 fois le numéro de chaque vague battue, 2 par seconde d'avance ; les dix meilleures
  parties par nombre de joueurs restent sur la machine (`user://survival-records.json`), aussi montrées au salon.
  L'IA défend les stations de toute l'équipe. `--wave=N` fait commencer une partie de vérification à la vague N.
- Contrôle de zone : trois balises (le centre, deux points symétriques loin des stations) ; les vaisseaux d'un seul
  camp en prennent une en 12 s (après avoir défait la prise adverse), deux camps la gèlent. Une balise tenue rapporte
  0,1 point/s à son camp tant qu'il a une station ; le premier au score (100) gagne, détruire les stations aussi.
  L'IA envoie ses vaisseaux inactifs à la balise la plus proche qu'elle ne tient pas. Entre IA : 8 à 14 min.
- Assaut : l'équipe 1 tient une forteresse (station niveau 4, trois plateformes niveau 3, canon ionique, 9 vaisseaux),
  l'équipe 2 arrive d'une tête de pont (station niveau 3 nue) avec une flotte de 14 000 cr (20 vaisseaux) et 30 cr/s
  de plus. La défense gagne si une station tient au bout du temps (15 min), l'attaque en les détruisant toutes. L'IA
  défend sans sortir ; en attaque, une vague dès 5 000 cr de vaisseaux prêts (oisifs ou encore en route vers le point
  de ralliement), tout dans les 3 dernières minutes. Entre IA : 114 victoires de la défense, 102 de l'attaque sur 216
  (équilibrage du 2026-11-04).
- Régicide : chaque camp commence avec son amiral (héros) pour vaisseau amiral, couronné, connu de tous sur la
  minicarte, jamais reconstruit ; le perdre élimine le camp, dont tout s'autodétruit sans prime. La dernière équipe
  avec un vaisseau amiral gagne, les stations ne décident rien. L'IA le garde à 900 de sa station, sur le flanc
  opposé aux ennemis, avec une escorte de 2 à 6 vaisseaux ; ses vagues plongent sur un vaisseau amiral ennemi en vue,
  et après 20 min elle y jette tout. Mort subite à 25 min (annoncée 2 min avant) : les vaisseaux amiraux perdent
  leurs boucliers et plus rien ne répare leur coque. Entre IA : 13 parties sur 15 finies en 60 min (médiane 35 min),
  contre 9 avant tir concentré et mort subite ; les nulles restantes opposent Kla'ed et Nautolan.
- Tir concentré (tous modes) : les tourelles d'un vaisseau tirent sur la cible qu'on lui a ordonné d'attaquer dès
  qu'elle est à leur portée, et non plus sur l'ennemi le plus proche.
- Multijoueur en réseau, batailles (la campagne viendra) : ENet de Godot en simple tuyau à paquets (`Game/scripts/Net/`),
  messages en C# pur et testés (`Sim/Net/Protocol.cs` : poignée de main avec versions du jeu, des commandes et des
  instantanés ; salon entier en JSON à chaque changement ; demandes de place ; lancement ; ordres ; instantanés ; refus ;
  pause ; avis ; retour au salon ; départ et renvoi). Trois rôles : l'hôte qui joue (« Héberger une partie »), le
  joueur (« Rejoindre » : parties du réseau local annoncées par diffusion UDP sur le port 24681, ou adresse:port) et le
  serveur dédié (`--server`, sans fenêtre ni place ; le premier arrivé mène le salon avec les droits de l'hôte). Port
  24680 par défaut, paquets compressés (zstd). Toute retouche du salon, même seul, est une demande que
  `Sim/Net/LobbyRoom.cs` applique selon les droits : celui qui mène règle tout, les autres leur place (race, équipe,
  couleur, prêt, ou une place libre) ; « Toi » sur une autre place y déplace le joueur ; un joueur qui part libère sa
  place, celui qu'on en retire est renvoyé. « Toi » est la place de la machine, chez chacun. Le nom du joueur est une
  option (au départ, le nom d'utilisateur du système). En bataille, l'hôte applique les ordres de chaque joueur au
  début du pas suivant et lui renvoie ceux qu'il refuse ; il envoie à chacun 20 instantanés par seconde de ce que voit
  son équipe, moins souvent au-delà de 256 Kio/s par joueur, en alternance d'une image à l'autre. Le joueur ne simule
  rien : sa copie du monde glisse d'un instantané au suivant (50 ms de retard), les tirs filent sur leur vitesse. Vitesse
  ×1 seulement ; la pause est celle de tous, avec qui l'a demandée ; un joueur qui part laisse son camp à l'IA ; si l'hôte
  part, les joueurs reviennent au menu ; la fin ramène tout le monde au salon. Instantanés en version 2 : l'état du mode
  (vagues, balises et scores, échéance de l'assaut, vaisseaux amiraux ; escortes et coques usées restent à l'hôte), ses
  événements, et les vaisseaux amiraux hors de vue par leur place et leur coque. Mesures (tests et parties réelles) :
  duel à 5 min, 20 instantanés/s, 29 Kio/s par joueur (environ 20 compressés), 0,1 ms d'écriture chacun ; bataille de
  mesure (1 180 entités), 5 instantanés/s, 248 Kio/s, 1,9 ms chacun.
- Multijoueur en réseau, campagne galactique (« Héberger une partie », puis la campagne ; protocole en version 2) : l'hôte joue et
  simule tout, la galaxie comme ses batailles commandées ; chaque joueur suit une copie de la galaxie qu'il ne simule jamais.
  Instantanés de galaxie (`Sim/GalaxySnapshot*.cs`, binaire versionné, entiers à chaque fois) de ce que sait l'équipe du joueur : le
  propriétaire de chaque planète partout, ses défenses et ses flottes seulement là où l'équipe a des yeux (et la garde des Errants,
  connue de tous) ; l'économie du joueur entière (crédits, recherche, files et boucles de ses planètes, ralliements, charge de lance),
  rien de celle des autres ; les batailles qui tiennent la galaxie et celles qu'il connaît, avec ses forces et celles d'en face pour les
  siennes ; les trêves, les offres qui le concernent, les événements aléatoires. Cinq par seconde (`GalaxyHost`), les flottes glissent
  entre deux ; 0,9 Ko pour une galaxie moyenne ou grande à 15 min, 1,3 Ko à six camps, 0,1 ms d'écriture. Ses ordres galactiques
  partent à l'hôte, qui les applique au pas suivant (un puits différé par joueur) et renvoie ceux qu'il refuse. La vitesse est celle
  que règle l'hôte, pour tous ; la pause est celle de tous, avec qui l'a demandée ; la galaxie tourne pendant que chacun parcourt ses
  panneaux. Une bataille où se trouve un humain lui pose la question, à chacun s'il y en a deux, et attend leurs deux choix (la
  galaxie aussi, pour tous ; une ligne sous la barre dit qui elle attend). Si l'un la commande, elle se joue dans la vue tactique,
  comme une bataille en réseau servie par l'hôte (à ×1), avec pour joueurs ceux qui l'ont commandée ; l'IA prend le camp de celui
  qui l'a laissée se résoudre seule ; si tous l'ont laissée, elle se résout seule comme avant. Les alliés n'y prennent pas part : une
  bataille reste à deux camps. Une seule à la fois ; pendant ce temps la galaxie attend, une ligne dit laquelle et qui la commande,
  et les autres peuvent la regarder, par les yeux de leur camp, d'un allié qui s'y bat, sinon du défenseur, sans rien ordonner. Chacun
  a sa retraite (l'attaquant) et son « Laisser l'IA finir » (l'IA prend son camp ; la bataille se résout seule quand personne ne la
  commande plus) ; la fin donne à chacun sa victoire ou sa défaite, puis la carte, et la galaxie repart. Une trêve proposée à un
  autre joueur lui arrive en dialogue. Un joueur qui part laisse son camp à l'IA ; si l'hôte part, tous reviennent au menu. L'hôte
  sauvegarde à part (`user://campaign-net.json`), avec le nom de chaque joueur ; « Héberger une partie », puis « Reprendre la
  campagne » rouvre un salon où chacun retrouve sa place en rejoignant sous son nom, à moins que l'hôte ne la donne à un bot ; rien
  d'autre n'y change. La campagne seule et sa sauvegarde restent les mêmes. Pas de campagne sur serveur dédié, ni de campagne
  rejointe en route.
- Fiabilité (batailles et campagne) : un instantané plus gros que le MTU d'ENet part en fragments non fiables, et un seul fragment
  perdu le perdait avec ses événements, une victoire comprise. Les événements qui décident (victoire, fin d'un mode, station
  détruite, vaisseau amiral abattu, raid, vagues, balises, lance stellaire) partent à part sur le canal fiable (`News`), comme tous
  les avis de la galaxie (`Tidings`) et la fin d'une bataille de campagne (`Over`) ; les instantanés restent non fiables.
- Multijoueur, écran unique et serveur en ligne : « Multijoueur » remplace « Héberger une partie » et « Rejoindre ». Une liste de
  toutes les parties à portée (nom, mode, joueurs, type, ping), chaque type dans sa couleur avec une légende : réseau local (annonces
  UDP, qui disent désormais si elles viennent d'un serveur dédié), parties hébergées en ligne, serveurs dédiés (inscrits au serveur en
  ligne avec `--public`, ou trouvés sur le réseau local ; un serveur trouvé des deux façons n'apparaît qu'une fois). Elle se met à jour
  seule (liste redemandée toutes les 4 s) et par « Actualiser » ; on choisit une ligne puis « Rejoindre la partie », ou double clic ;
  « Par adresse » reste, en une ligne ; « Héberger en local » (comme avant) et « Héberger en ligne », allumé quand le serveur en ligne
  répond, ce que dit la ligne du haut. Le serveur en ligne (`--relay`, même exécutable, sans fenêtre ni chargement des sprites) est le
  pont entre ceux qui hébergent et les autres : un hôte y ouvre sa partie et garde la connexion (sa fin la retire de la liste) ; un
  joueur la rejoint par lui, qui relaie chaque message dans les deux sens avec sa fiabilité et son canal ; personne n'ouvre de port.
  Côté jeu, c'est un transport de plus (`RelayTransport`, à côté de `Transport`, sous une interface `ITransport`) : le serveur numérote
  les joueurs de chaque hôte comme ENet (2 et plus), la session ne change pas, batailles et campagne passent telles quelles. Sa logique
  est en C# pur et testée sans réseau (`Sim/Net/Relay.cs` : trames, `Sim/Net/RelayHub.cs` : liste et aiguillage) : un joueur n'atteint
  que l'hôte de sa partie et un hôte que ses joueurs ; limites de parties (32), de joueurs par partie (8), de connexions (512) et de
  débit par partie (2 Mio/s avant compression : au-delà, les instantanés sont écartés, et une partie très au-delà en messages fiables
  est fermée) ; 5 s pour se présenter, 30 s pour une liste inactive ; taille et numéros des messages vérifiés ; une autre version du
  jeu ou du relais est refusée avec la raison. Le serveur en ligne sert sur son propre fil (ENet ne dort pas dans Godot : il regarde
  chaque milliseconde quand des paquets passent, toutes les dix sinon), écrit ouvertures, entrées et départs, et toutes les dix
  secondes sa charge ; Ctrl+C le ferme en prévenant chacun. Un hôte en ligne qui le perd revient au menu en le disant. Quitter le jeu
  quitte proprement la partie en réseau : les autres le savent tout de suite. Adresse par défaut `51.91.214.135:27029`, réglable dans
  les options (« Serveur en ligne ») ou par `--master`. Mesures en boucle locale (`--probe`) : aller-retour médian 0,04 ms en direct,
  2,4 à 3,4 ms par le relais (95 % sous 7 à 14 ms) ; pour une partie en bataille (un hôte, un joueur, 20 à 30 Kio/s par sens), le
  relais prend 5 à 8 % d'un cœur (2 % au repos) et 180 Mo de mémoire. Le reste du chemin est celui du réseau jusqu'au serveur.
- Performances, passe finale (2026-10-30 ; protocole et instantanés de bataille en version 3, de galaxie en version 2) :
  - Instantanés de bataille en deltas : chacun s'écrit contre le dernier que le joueur a acquitté (`Ack`, non fiable). Une entité
    inchangée n'y figure pas ; d'une entité changée ne part que ce qui a bougé (qui elle est, sa place, sa santé, le reste). Les
    pièces d'un vaisseau n'ont plus de place à elles : le lecteur les pose sur leur vaisseau. Un tir qui file droit n'est plus
    réécrit : le lecteur le porte sur sa vitesse. Hôte et joueur gardent les 32 derniers (`Sim/SnapshotFrames.cs`). Un delta contre
    un instantané que le joueur n'a plus ne change rien, le suivant le rattrape ; après une longue perte, l'instantané repart entier.
  - Mesures en réseau réel : en duel, 7 à 12 Kio/s par joueur au lieu de 14 à 28 (6 à 10 compressés au lieu de 9 à 18), deltas de
    0,35 Ko sous le MTU. Pour la bataille de mesure (1 200 entités, `--host --start=stress`), 46 Ko entier, 13 Ko en delta : 16 à 20
    instantanés/s au lieu de 5, dans le même budget de 256 Kio/s par joueur, en direct comme par le serveur en ligne. Écriture
    1,1 ms et 90 Kio au lieu de 1,6 ms et 590 ; lecture 1,1 ms et 60 Kio au lieu de 1,3 ms et 820 (Enum.HasFlag allouait).
  - Galaxie : ce qui ne change jamais (taille, voies, nom, place, revenu et mines des planètes) part une fois, de façon fiable
    (`Chart`), avant le premier instantané ; un instantané arrivé avant la carte attend le suivant. Instantané d'une grande galaxie
    à six camps : 0,65 Ko au lieu de 1,3, sous le MTU ; 0,2 Ko en partie réelle.
  - Simulation : grille spatiale en tableau plat au lieu d'un dictionnaire, même ordre de visite et mêmes parties ; 0,90 → 0,78 ms
    par pas de la bataille de mesure. La vision garde ses ensembles d'une mise à jour à l'autre : 12 → 4 Kio de déchets par pas
    (19 → 5 à six camps), 28 → 20 Kio par image dans le jeu.
  - Serveur dédié : il sautait déjà la forge et le préchauffage (prêt en 0,45 s, 133 Mo). Un seul interrupteur sans fenêtre
    (`Dev.Service` : `--server`, `--relay`, `--probe`) ; 15 images/s entre deux batailles, 60 en bataille.
  - Mesuré et laissé : le rendu (images au plafond de 60 Hz, aucune partie de la vue au-dessus de 0,6 ms) ; la campagne (0,35 ms par
    pas galactique, 7 ms au 99e centile avec des batailles résolues seules ; 60 Kio par pas, surtout le LINQ de l'IA galactique) ;
    le découpage des instantanés sous le MTU : un delta de grosse bataille fait encore une dizaine de fragments, mais un instantané
    perdu ne coûte plus qu'une image glissée et un delta un peu plus gros ensuite.
- Performances, deuxième passe (2026-11-02, après les nouvelles armes et l'IA des vaisseaux), mesurée sur un export release
  (`--start=stress`, que la sonde détaille maintenant aussi en allocations par partie et en pauses du ramasse-miettes) :
  - Mémoire : les sprites ne gardent plus leurs pixels après le chargement (les tailles utiles sont calculées avant) ; la mémoire
    de Godot passe de 160 à 63 Mio au menu.
  - Déchets : 23 → 10,5 Kio par image dans la bataille de mesure, 10 → 6 collectes en 14 s. Les étincelles des touches sont une
    simple liste au lieu de deux fonctions chacune ; le cache des animations a une clé sans chaîne ; deux `StringName` faits une
    fois (« font_color » à chaque ligne des panneaux, « default » à chaque image d'une destruction) ; les boutons de production ne
    changent que si ce qu'ils montrent change, et leurs lignes de prix sont faites une fois.
  - IA : les zones de mines sont calculées une fois par réflexion au lieu d'une fois par champ d'astéroïdes.
  - Fenêtre au second plan : 30 images/s, la simulation garde son pas.
  - Mesuré et laissé : la simulation (1,5 à 1,8 ms par pas, dont 0,45 de ciblage ; ses 5 Kio par pas sont les tirs eux-mêmes) ;
    le menu (0,7 ms par image pour la bataille du fond) ; la campagne (0,3 à 0,4 ms par pas). La taille de la première génération
    du ramasse-miettes ne se règle que par variable d'environnement : `System.GC.Gen0Size` et `GCgen0size` dans la configuration
    du projet restent sans effet. Les pointes d'image isolées suivent surtout la charge de la machine.
- Performances, troisième passe (2026-11-03, après les sprites des stations, la Survie sans fin, la traduction et la
  sécurité), sur un export release :
  - Bataille de mesure : 60 images/s en moyenne, 10 Kio alloués par image (inchangé depuis la deuxième passe),
    3,2 ms de simulation par image ; les lampes des stations coûtent 0,08 ms, l'anglais 0,03 ms.
  - Campagne : 53 → 31 à 34 Kio alloués par pas galactique, 0,66 → 0,37 à 0,42 ms. `DetectConflicts` range les
    flottes par planète en un passage et passe les planètes vides (30 → 9 Kio) ; les délais des héros ne sont plus
    copiés quand il n'y en a pas. Résultat identique, tests de galaxie compris.
  - Serveur en ligne : le relais léger (`godot/Relay/`, .NET seul) tient une partie avec 41 Mo au lieu de 170.
- Performances, quatrième passe (2026-11-03, avant la 1.0.0 : postures, vétérans, terrain, courbes, diplomatie,
  personnalités), sur un export release, machine chargée (charge 5 à 6 sur 4 cœurs) :
  - Bataille de mesure sans terrain : 16,7 ms par image en moyenne (60 images/s), 3,1 ms de simulation, 10,1 Kio
    alloués par image sur le fil principal, comme à la troisième passe. En terrain dense : 17,1 ms, 4,4 ms de simulation,
    9,9 Kio. La sonde distingue maintenant le fil principal : le total du processus (33 Kio par image) compte aussi la
    musique, composée sur son propre fil pendant les premières secondes. Le préchauffage finit avant la mesure (2,2 s).
  - Campagne (banc de 4 IA, galaxie moyenne, 300 s mesurées après 600 s) : le ravitaillement ne fait plus un ensemble de
    planètes par camp et par pas, il suit les couloirs (`GalaxySupply.cs`) ; `WearUnsupplied` passe de 10,8 à 0,2 Kio
    et de 0,010 à 0,001 ms par pas, la réparation des coques vivantes et l'IA en profitent aussi. Total : 34,2 → 29,8 Kio
    par pas (personnalités mêlées), 31,8 → 29,4 (équilibrées), 0,2 à 0,4 ms par pas ; même galaxie à la fin (crédits,
    état des flottes, vaisseaux). Le même banc sur la troisième passe : 32,5 à 33,3 Kio et 0,28 à 0,35 ms par pas.
  - Non mesuré cette fois : démarrage et mémoire au menu, débit réseau par joueur (rien n'a changé dans les
    instantanés v8 ni le chiffrement depuis leurs mesures).
- Équilibrage (2026-11-03, après les nouvelles armes et l'IA des vaisseaux), mesuré en parties entre IA de difficulté normale,
  chaque paire de races dans les deux sens, une heure au plus par partie. Les pourcentages sont les victoires de chaque race.

  | Mesure | Kla'ed | Nairan | Nautolan | Nuls |
  |---|---|---|---|---|
  | Escarmouche, avant les nouvelles armes (36 parties) | 42 % | 33 % | 67 % | 2 |
  | Escarmouche, avant cet équilibrage (36) | 33 % | 42 % | 29 % | 11 |
  | Escarmouche, après (108) | 51 % | 50 % | 47 % | 1 |
  | Zones, avant (108) | 25 % | 67 % | 58 % | 0 |
  | Zones, après (108) | 40 % | 54 % | 56 % | 0 |
  | Régicide, avant (36) | 29 % | 54 % | 50 % | 4 |
  | Régicide, après (108) | 49 % | 46 % | 56 % | 0 |
  | Assaut, avant (36) | 58 % | 46 % | 46 % | 0 (défense 26, attaque 10) |
  | Assaut, après (108) | 58 % | 42 % | 50 % | 0 (défense 63, attaque 45) |

  - Les nuls : tir groupé, repli pour se réparer et distance rendaient une flotte de défense, sous les canons de sa station,
    trop forte pour les petites vagues que l'IA envoyait l'une après l'autre ; deux IA échangeaient alors des vaisseaux une
    heure sans jamais entamer une station. Ni l'antimissile, ni la distance, ni l'esquive, ni le repli, ni les nouvelles armes,
    retirés un à un, n'y changeaient assez. Désormais, passé 30 minutes, l'IA va au tout ou rien (`Ai.AllInTime`) : son armée
    entière dès qu'elle vaut 60 % de la vague attendue, défense comprise, sans rappel à la maison, et ses renforts par
    groupes d'au moins 30 % ; avant, un avantage sur l'ennemi vu n'envoie plus moins de la moitié de cette vague
    (`EdgeShare`, 1 500 crédits dans la version web), sauf pour les assaillants d'un assaut, pressés par l'horloge. Sans ce
    seuil, les Nairan et leurs coques solides gagnaient 68 % des escarmouches (108 parties), les Kla'ed 36 %.
  - L'avantage Nautolan (67 % avant les nouvelles armes) venait de ces dénouements, pas de leur trait : il n'a pas bougé.
  - Les Kla'ed passent à armes +15 % et boucliers −10 % (+10 % et −15 % dans la version web) : ils gagnaient un quart des
    parties de zones et un tiers des régicides, là où il faut tenir. Boucliers seuls à −10 % : 26 % en zones, 36 % en régicide.
  - Les essaims de missiles du Titan passent à 9 dégâts et 4 s de recharge (7 et 5) : à coût égal, des Titans ne faisaient pas
    mieux contre les chasseurs qu'avec les lasers qu'ils remplacent, et bien moins contre le reste. Les autres armes, comparées
    aux anciennes en duels de flottes à coût égal, ne bougent pas : la frégate à rayon continu tient désormais tête aux
    bombardiers (−0,48 → +0,06 sur une échelle de −1 à +1) sans rien écraser ; croiseur, cuirassé et Léviathan restent à moins de
    0,1 de leurs anciens scores.
  - Reste à juger en vraie partie : le tout ou rien à 30 minutes face à un joueur, et l'assaut, où la défense garde l'avantage.
- Assaut rééquilibré (2026-11-04), mêmes parties entre IA de difficulté normale, graines 1 à 18 puis 1 à 36
  (`BALANCE=1 SEEDS=18 dotnet test Sim.Tests --filter Measures_assault`, chaque race dans les deux rôles) :

  | Mesure | Kla'ed | Nairan | Nautolan | Nuls |
  |---|---|---|---|---|
  | Assaut, avant (108) | 63 % | 50 % | 38 % | 0 (défense 81, attaque 27) |
  | Assaut, après (108) | 61 % | 42 % | 47 % | 0 (défense 58, attaque 50) |
  | Assaut, après (216) | 56 % | 42 % | 52 % | 0 (défense 114, attaque 102) |

  - Depuis la mesure du 2026-11-03, prise avant les postures, les vétérans et les personnalités, la défense était passée
    de 58 % à 75 % : chaque vague mourait sous les plateformes et la flotte de la forteresse, qui passait de 10 000 à
    30 000 ou 45 000 cr en 15 min pendant que celle de l'attaque finissait entre 10 000 et 23 000.
  - La flotte de départ de l'attaque passe de 6 à 8 frégates (12 700 → 14 000 cr). Le premier choc décide le plus
    souvent, et il bascule net : 6 frégates et un troisième croiseur (14 700 cr) ne laissent à la défense que 42 parties
    sur 108 ; un quatrième, 14, en 5 min (médiane). Sur les graines 19 à 36, jamais réglées, la défense en gagne 56 sur 108.
  - L'IA d'attaque envoie aussi les vaisseaux encore en route vers son point de ralliement : les grosses coques s'y
    bousculent sans atteindre leur place et, attendues comme oisives, restaient chez elles (une partie suivie : flotte
    immobile dès la 8e minute, 23 000 cr inemployés à la fin). Seule, cette correction change peu (défense 79 sur 108).
  - Sans effet net sur 24 parties : vagues de 2 500 cr ou dès le premier vaisseau prêt, vagues laissées au seul mode
    (pire, 21 défenses sur 24), penchant pour les grosses coques, recherche à chaque occasion, tête de pont rapprochée
    (70 % de la largeur au lieu de 88 %). Le revenu de 30 cr/s n'est pas un levier : un seul chantier ne construit pas
    plus vite, et l'attaque finit avec 8 000 à 24 000 cr inemployés.
  - Reste : 40 des 50 victoires de l'attaque (108 parties) tombent avant la 9e minute, et 50 des 58 défenses finissent
    avec une station intacte ; l'horloge décide peu.
- Succès et statistiques (2026-11-03) : 36 succès, dont 4 cachés, et les statistiques du joueur, en logique pure dans
  `Sim/Profile.cs` (`ProfileTests`), comptés côté jeu par `Core/PlayerProfile.cs` et gardés dans `user://profile.json`
  (écrit par fichier temporaire, relu borné : un fichier cassé ou forgé donne un profil vide ou borné). Seul le joueur
  local compte, à la fin de chaque bataille et de chaque campagne ; jamais une capture (`--shot`, `--snap`, sauf avec
  `--profile=FICHIER`), une rediffusion, le didacticiel, un spectateur ni la bataille du menu. Le temps de jeu est le temps
  réel passé en bataille ou dans la galaxie, quelle que soit la vitesse. Écran « Profil » au menu, notification au déblocage.
- Musique et sons d'interface (2026-11-03, après la 1.0.0) :
  - Cinq morceaux de plus, écrits dans `MusicScore.cs` avec les instruments de `MusicSynth.cs` et un nouveau, `Lead`
    (une voix pour les mélodies) : « drift » au menu (fa lydien), « horizon » (ré dorien) et « front » (do mineur) dans
    la galaxie, « assault » (ré mineur, 126) et « siege » (mi mineur en demi-temps, 88) en bataille, chacun en trois
    couches comme le premier. Les morceaux longs passent deux fois leurs accords : la seconde fois, une mélodie.
  - Un thème joue son premier morceau la première fois (le menu, celui de l'ouverture), puis un autre que le dernier ;
    après 150 s, il passe au suivant en fondu sur les 4 dernières secondes de la boucle (`MusicPlayer.TurnFrom`).
  - Les sept morceaux d'origine sortent identiques à l'octet près (`--music-dump`). Les nouveaux sont amenés au même
    niveau (-18 dB RMS) ; leurs boucles se rejoignent sans saut.
  - Mémoire : Godot garde la musique en QOA (`AudioStreamWav.LoadFromBuffer`, encodé sur le fil de la musique), cinq fois
    moins que les échantillons. Bruit mesuré contre le PCM : 30 dB sous la musique au pire, 39 à 45 dB sur un morceau ;
    jointures de boucle intactes. Les flux tiennent 11,5 Mio pour 57 Mio d'échantillons (18 Mio avant, pour le tiers de
    musique). Les pistes d'un fil partagent leurs tampons, l'écho n'a plus qu'une ligne de retard de sa longueur, et une
    collecte « Aggressive » rend au système ce que .NET avait réservé pendant le rendu. Export release, bataille de
    55 s : 460 Mo en régime contre 576 pour la 1.0.0 ; pic de 633 Mo contre 576 pendant le rendu (18 s, en arrière-plan,
    6 s avant).
  - Interface (`Sfx.Hook`, pour tout contrôle qui entre dans l'arbre) : clic plus franc, son d'action principale
    (`HudKit.Primary`, boutons « MenuPrimary »), cases cochées et décochées, crans des curseurs (hauteur selon la
    valeur), refus sur un bouton désactivé, survol au pointeur dans le menu et les dialogues seulement (`Sfx.HoverSounds`)
    et aux touches partout, souffle à l'ouverture et à la fermeture d'un écran du menu ou d'un dialogue.
- Mixage (2026-11-04) : les sons d'interface et les avis, repris aux niveaux de la version web, passaient 20 à 33 dB sous
  la musique (clic −49 LUFS, survol −59, contre −26 pour la musique du menu, aux volumes par défaut). Chacun est relevé
  après son rendu, sa forme gardée (`Sfx.Lift`, +13 à +23 dB) : clic −29, alerte −27, action principale −27, cases −29 et
  −30, refus −31, ouverture et fermeture −32, crans −34, survol −37. Les sons de bataille gardent leur mixage : une grosse
  bataille (`--start=stress`) tient à −25 LUFS, la musique de la galaxie et du menu à −26, celle de bataille de −31 au
  calme à −27 au plus fort. Un limiteur (`AudioEffectHardLimiter`, plafond −0,3 dB) tient le bus principal : les deux
  volumes au plus fort, une bataille monte à −3 dBFS. Mesure : `tools/loudness.py` (BS.1770 ; musique sur toute sa durée,
  effet sur ses 100 ms les plus fortes), sur `--sfx-dump` et `--music-dump`, ou `--record` sur un `--music-record`. Une
  capture lit les volumes du joueur : la lancer avec `XDG_DATA_HOME` vers un dossier vide pour mesurer aux volumes par défaut.
- Interface, menus et modes (2026-11-04) :
  - Bataille : le bas de l'écran ne prend plus qu'une rangée tant qu'on ne s'en sert pas. La production démarre repliée sur
    ses onglets, chacun avec le compte de sa file (« Bâtiments (1) ») ; un onglet l'ouvre, le même la replie, B aussi
    (`Bindings.Build`, réglable). Repliée, elle garde en vue la file de son dernier onglet quand elle tient quelque chose.
    Le panneau de sélection n'existe que s'il y a une sélection. En haut, un seul bouton « Menu » ouvre le menu Échap, qui
    avait déjà options, commandes, recommencer et quitter ; la campagne garde « Retraite ». Les avis passent en haut à
    gauche (sous la carte du didacticiel quand il est là, `BattleScreen.TutorialBottom`), et un avis déjà affiché se compte
    (« Construction terminée ×2 ») au lieu de s'empiler.
  - Objectif : au départ d'une bataille lancée d'un salon, un bandeau sous le panneau du mode dit comment gagner (en assaut,
    selon le rôle), 9 s puis s'efface ; pas en campagne, au didacticiel, au bac à sable, en rediffusion, en spectateur, ni
    dans une bataille rejointe en cours (`Hud.BuildObjective`).
  - Menu : l'accueil passe à cinq entrées (Continuer et Charger s'il y a une sauvegarde, Nouvelle partie, Multijoueur,
    Profil, Options, Quitter) ; le didacticiel y reste tant qu'aucune bataille n'est jouée, et devient une carte
    « Entraînement » de Nouvelle partie, à côté du bac à sable ; l'escarmouche y prend toute la largeur au-dessus des quatre
    autres modes. Crédits en pied de page. Options en onglets (Général, Affichage, Son, Accessibilité, et au menu
    Commandes), dans le menu comme en bataille (`Options.Page`, la dernière ouverte gardée) ; Profil en deux onglets,
    statistiques et succès, rediffusions (`Menu.Tabs`). Au salon, le type de carte, le terrain et la graine se replient
    sous « Plus de réglages », leurs valeurs affichées à côté.
  - Règles : crédits de départ (1 500, 4 000 ou 10 000 ; escarmouche, zones et régicide seulement : l'assaut a sa
    forteresse, la survie ses records) et force des vagues en survie (70, 100 ou 140 % de leur budget, `Survival.Strength`,
    lue dans `World.Origin`). Les records de survie et le profil (meilleure vague, succès de vagues) ne comptent que les
    vagues normales ou plus fortes. Protocole 10 (les deux règles dans `LobbySettings`) ; rediffusions au format 4, qui
    garde la force des vagues ; les formats 1 à 3 se lisent toujours, aux vagues normales. Le serveur en ligne n'a pas à
    changer : le relais compare la version de l'hôte à celle du joueur, sans en figer aucune.
- Systèmes revus (2026-11-16), d'après un audit en trois parts (commandes, bataille, campagne) ; mesures entre IA,
  aucune face à un humain.
  - Commandes : la Lance stellaire du joueur se vise enfin (la visée s'annulait à l'image suivante : seuls les
    vaisseaux comptaient comme lanceurs). Une pièce détruite ne lève plus d'alerte (un cuirassé en levait neuf en
    mourant). F puis Maj + clic, et Maj + clic droit sur la minicarte, mettent en file. Zoom arrière par la touche à
    côté de celle du zoom avant (`)` en AZERTY, où `-` est la touche du groupe 6). Un ordre sonne, laisse un anneau
    sur la minicarte et la route d'une sélection hors de la vue reste tracée ; Stop a son son et ses anneaux. Sur
    la minicarte, un clic gauche vise pour l'attaque-mouvement, la patrouille, la garde ou une capacité en attente,
    et un clic droit pose le ralliement de la station sélectionnée. Le repli pour réparation se voit (croix claire,
    « Repli pour réparation », hors des inactifs ; instantanés de bataille 9, dans le bit haut de la posture).
    Plusieurs vaisseaux sélectionnés : combien en route, au combat, en repli, inactifs.
  - IA de bataille : un objectif inatteignable cède sa place (blocage des perdants : 142 s → 2 s en médiane) ; une
    épargne à part pour héros, capitaux et grosses dépenses, le chantier produisant pendant ce temps ; un héros compte
    pour son coût. Une menace près de toute structure appelle la flotte sur l'ennemi, si elle vaut au moins 60 % de ce
    qu'elle voit là. Plafond de mines par personnalité (Économe 6, Agressive 3), niveaux de plateforme achetés, coques
    moyennes contrées. Personnalités : 31 / 38 / 50 / 56 / 50 % (équilibrée, agressive, défensive, économe,
    technologique ; 34 points d'écart avant, 25 après).
  - Règles de bataille : une frappe de zone touche un vaisseau une fois, bouclier compris, ses pièces à 25 % et
    seulement bouclier tombé (une station de niveau 2 se dépouillait en une frappe, il en faut trois). Les tourelles
    d'un vaisseau suivent sa provocation si elles portent. Chaque classe s'arrête là où 75 % de son feu porte
    (`Combat.StopRange` : Léviathan 51 → 92 %). Soutiens près des blessés, ralliement par `Reachable`. Amiral 8 000
    cr, Énergie 12 % par niveau (sans la Lance), canon ionique ×3 sur les boucliers, armada d'assaut à 9 frégates
    (défense 54 % sur 108 parties).
  - Niveaux d'IA (`DifficultyDef`) : ils diffèrent par leurs décisions. Facile : réagit en 15 s, défend sa seule
    station, n'adapte rien, attaque sans peser les forces, emploie 40 % de ses capacités. Difficile : tir concentré,
    pièces visées bouclier tombé, raids sur les mines non gardées, rappel d'une vague qui perd, flotte adaptée,
    recherche ×1,75. Revenu en bataille 0,75 / 1 / 1. À revenu égal (`AiLevelsBalanceTests`) : Difficile bat
    Normale 80 % (96 parties), Normale et Difficile battent Facile 97 %. En campagne, revenu 0,75 / 1 / 1,15 (1,35
    avant) : Difficile bat Normale 85 % à revenu égal, Normale bat Facile 75 % (`DifficultyBalanceTests`).
  - Campagne, confort : la galaxie en solo démarre en pause, et le menu, l'aide et les options arrêtent le temps
    (`Dev.RunsClock` le lance pour les vérifications). Maintenir V puis clic droit envoie un seul vaisseau. Avis à
    gauche partagés avec la bataille (`ToastStack`), alertes « Voir » (occupation, attaque, perte), vaisseaux terminés
    regroupés hors du journal, défaite annoncée dès l'élimination (un camp éliminé voit toute la carte). Chaque
    bataille porte son bilan (`BattleToll`). L'invite donne une issue en mots (`Battle.Power`, `Battle.Outlook`, calibrée
    sur 2 211 batailles, remesurée après les règles de bataille : « Victoire presque certaine » 97,6 %, « Indécise » 48 %) ; les victoires écrasantes se
    résolvent seules (Options, réglage `AutoResolveWon`). Un ralliement ne vise jamais une planète tenue par un
    non-allié, et s'annule si elle tombe.
  - Campagne, règles : prix de paix plafonné et proposé, confiance achetable 15 points par 10 min, trêve prolongée une
    fois, deux IA attendent 10 min avant de resigner (nuls des parties de l'audit : 14/40 → 1/40 ; ils venaient des
    miroirs Défensive contre Défensive, que la série des personnalités ne joue pas). L'IA épargne pour la Lance en
    station 4 (Lance de campagne 20 000 cr, 120 s, charge 240 s), achète les niveaux de plateforme et suit les
    spécialités. Épaves et bandes aux frontières une fois les neutres pris ; une tempête coupe une planète frontalière
    3 min ; le ravitaillement se compte depuis les stations, à 2 voies, avec usure et sans réparation au-delà.
  - Protocole 11, instantanés de galaxie 4 (bilan, puissance, occupations, ralliements annulés, prix de paix).
  - Sécurité : un instantané de galaxie borne désormais les niveaux et les nombres d'une planète (64 au plus) ; un hôte
    malveillant pouvait figer la carte d'un joueur avec une station de niveau deux milliards, un repère étant dessiné par
    niveau (défaut antérieur à ce chantier). Les défenses du bilan sont bornées de même, la puissance d'un camp et le prix
    de paix demandé sont lus finis et positifs. Le fuzz couvre les nouveaux événements (bilan, occupation, ralliement
    annulé, vaisseau construit).
  - Performances, banc console en Release, ancien code contre nouveau (temps bruités par une machine chargée, allocations
    exactes) : l'IA de bataille allouait 2,5 Kio par pas au lieu de 1,05 (chaque vaisseau ennemi testé contre chaque
    structure, avec une fermeture par vaisseau) ; ses menaces se cherchent maintenant par la grille autour de chaque
    structure : 1,1 Kio, et 4,3 Kio pour le pas entier (4,4 avant). Un soutien cherche son protégé deux fois par seconde
    au plus, comme un blessé son réparateur. La bataille de mesure est 18 % plus serrée (433 → 512 ennemis à 500 par
    entité armée), ce que le ciblage paie. Campagne : 18 à 33 Kio et 0,25 à 0,9 ms par pas (21 à 30 Kio et 0,5 à 1,3 ms
    avant).
  - Ouvert : le régicide se déséquilibre (Kla'ed 39 → 26 %, Nairan 30 → 45 % sur 108 parties ; la moitié vient de la
    frappe corrigée, dont l'amiral kla'ed profitait). 7 escarmouches sur 72 vont au-delà de 60 min entre IA (aucune
    au-delà de 85). Le tir concentré, les raids et le choix des capacités du Difficile se voient sans gain mesurable ;
    son avance vient du rappel des vagues et de la recherche. Les seuils de temps de `FuzzTests` (1 s par entrée)
    lâchaient sous forte charge (2026-11-19 : une lecture lente est rejouée une fois, seule une seconde lenteur échoue).
- Diplomatie revue (2026-11-16) : un écran dédié (touche T, `GalaxyHud.DiplomacyScreen.cs`) remplace la liste du volet.
  En solo, il arrête le temps. En haut, le tableau des relations de tous les camps, IA entre elles comprises : alliés,
  trêve ou guerre, et commerce, vision, guerre commune, tribut, guerre jurée. À gauche, les camps. À droite, le camp
  choisi : son attitude (confiance et ce qui l'a fait bouger, `TrustCause`), les accords en cours et de quoi les finir,
  ses offres et demandes, une offre à composer (trêve, commerce, vision, tribut, en un bloc : `DealKind.Bundle`) avec la
  réponse que donnerait l'IA, les accords à part (alliance, guerre commune contre chaque tiers, tribut exigé,
  ultimatum), l'historique.
  - Accords nouveaux (`Sim/GalaxyAccords.cs`) : vision partagée, en paix, finie par la guerre ; guerre commune contre un
    tiers (10 min, les deux en paix et tous deux en guerre contre lui ; faire la paix avec la cible pendant ce temps la
    trahit : −40 de confiance chez le partenaire, −20 de réputation) ; ultimatum d'un camp au moins 1,5 fois plus fort,
    en paix : des crédits ou une planète frontalière (pas la dernière, gardée avec ses installations, les flottes de
    l'ancien propriétaire rentrent), 90 s pour répondre, sinon la guerre, annoncée, sans trahison ; demande d'aide d'une
    IA à un joueur (des crédits, ou défendre une planète : une flotte là sous 3 min, +20 de confiance si elle vient,
    −25 sinon) ; offre combinée, acceptée ou refusée d'un bloc, le tribut pouvant acheter la trêve.
  - Réputation de chaque camp (−100 à 100, vue de tous) : trahir une trêve −15, une alliance −30, une guerre commune −20,
    cesser de payer −10, un ultimatum −5, une promesse d'aide manquée −10 ; une trêve tenue jusqu'au bout +2, un tribut
    payé jusqu'au bout +3, une guerre commune menée à son terme +3, une aide venue +8. Elle revient vers 0 de 0,01 par
    seconde ; une IA la pèse pour un quart dans son estime (`Galaxies.Regard`).
  - IA : réponses expliquées (`DealWhy`, `GalaxyAi.Reason`) et prévues (`GalaxyAi.PreviewFor`, calculée par l'hôte et
    envoyée au joueur en réseau) ; contre le camp qui domine (35 % de toute la puissance et 1,4 fois le suivant,
    `GalaxyAi.Leader`), une IA cherche une guerre commune avant de demander la paix, et lui demande 1,5 fois plus pour une
    trêve ; une IA jurée refuse la trêve de sa cible et la vise en priorité ; elle propose la vision à qui elle estime
    (30) ; elle demande de l'aide à un joueur allié ou estimé quand une de ses planètes est attaquée et qu'une flotte de
    ce joueur est à deux voies (une demande toutes les 4 min au plus) ; une IA difficile ou agressive bien plus forte
    qu'un joueur en trêve lui adresse un ultimatum (un toutes les 15 min au plus) ; la difficile prévient un joueur 30 s
    avant de rompre une trêve. Elle parle à un joueur selon sa personnalité (`AiSaid`, `Remarks`) : avertissement,
    menace, remerciements, trahison, bienvenue, coalition, demande d'aide, gratitude, déception, défi.
  - Historique (`Galaxy.Chronicle`, 400 lignes au plus, sauvegardé ; 120 envoyées par instantané : ce qui concerne le
    joueur, et les trêves, alliances et guerres communes de tous). Commande `ProposeAccord` (galaxie, étiquette 20),
    instantanés de galaxie 5, protocole 12 ; les anciennes sauvegardes se chargent. Tests : `AccordsTests`.
  - Mesures entre IA (Release), après : campagnes 36 terminées sur 36, médiane 12 min (15,6 avant), victoires Nairan,
    Kla'ed, Nautolan environ 16, 13, 7 (21, 8, 7 avant) ; personnalités en campagne 56, 63, 50, 25, 56 % (équilibrée,
    agressive, défensive, économe, technologique), aucun nul, médiane 35 min ; niveaux à revenu égal : Difficile bat
    Normale 45 à 3, Normale bat Facile 38 à 10, Difficile bat Facile 42 à 6. Ultimatums et demandes d'aide ne visent
    que les joueurs : ces séries ne les voient pas, ni rien de ce qu'un humain fera de l'écran.
  - Sécurité : une IA qui a cédé à un ultimatum n'y cède plus devant le même camp pendant 15 min (`UltimatumMemory`) ;
    avant, un joueur fort la vidait de ses crédits ultimatum après ultimatum. Une ligne d'historique dont le complément
    ne va pas avec sa sorte (une planète qui n'existe pas, une réplique inconnue) est refusée à la lecture d'un instantané
    et retirée d'une sauvegarde, comme une promesse d'aide, une offre ou une guerre commune qui désigne une planète ou un
    camp absent (`Galaxies.SaneEntry`) : l'écran ou le pas qui les cherchent tombaient. Les causes de confiance sont lues
    finies. Le fuzz couvre `ProposeAccord` et des instantanés avec accords, historique de chaque sorte et promesse.
  - Performances (banc console en Release, avant la diplomatie contre après) : un instantané de galaxie était passé de 7 à
    13,5 Kio à 15 à 34 Kio, la réponse prévue de chaque IA y étant recalculée à chaque envoi. Elle est gardée 3 s par IA
    et par joueur, et recalculée aussitôt après un événement diplomatique (`GalaxyAi.Mind`, `Galaxy.DiploChanges`) ;
    l'écran de l'hôte s'en sert aussi. Le contact entre deux camps (`GalaxyAi.Contact`) parcourait toute la galaxie et
    faisait une liste de voisins par planète : deux passes sur les voies maintenant, sans rien allouer, mêmes réponses
    (les parties du banc finissent aux mêmes instants). Trêves, pactes et tributs se cherchent sans fermeture, et les
    oublis par pas (rancunes, causes, offres, guerres communes, promesses) ne copient plus rien. Après : instantané 6 à
    9,5 Kio, pas de campagne 19 à 29 Kio (21 à 33 avant la diplomatie).
- Campagne qui dure (2026-11-17, à la demande de l'utilisateur, choix faits avec lui) : plus grande galaxie, plus de
  rush, une diplomatie qui sert. Chaque partie neuve en a les règles (`CampaignSetup.Of`, `Sim/GalaxyEndurance.cs`) ; une
  galaxie faite sans (`CampaignSetup.None`, par défaut dans les tests) et les anciennes sauvegardes restent comme avant.
  - Galaxie Immense (`MapSize.Immense`, campagne seulement) : 3400 × 2100, 54 à 64 planètes, en sept à neuf régions
    séparées par une bande vide, reliées par une seule voie entre deux régions voisines, plus d'autres voies au hasard
    pour contourner les goulets (`GalaxyGen.Regions`) ; des noms à elle, deux sites Errants, et la vue s'ouvre zoomée sur
    la capitale du joueur. Les trois autres tailles gardent exactement leurs tirages.
  - Indépendants (camp 7, `Galaxies.Independent` = `Lobby.MaxSide`, pas une faction) : chaque planète libre a une
    garnison qui ne bouge jamais (plateformes, frégates, un soutien, un croiseur pour les plus riches), plus forte loin
    des capitales et sur les planètes riches ou spéciales (`Garrison`, sans tirage : la galaxie classique reste en
    miroir). Vaincue, elle laisse la planète au vainqueur ; ses vaisseaux ont l'allure du peuple de la planète, en gris.
    Elle ne compte ni pour la victoire, ni comme menace pour l'IA. Les lecteurs réseau acceptent le camp 7.
  - Capitales fortifiées : station niveau 2 et deux plateformes au départ ; tant que son camp la tient avec sa station,
    une capitale relève une plateforme perdue toutes les 90 s, jusqu'à deux (`Planet.Home`).
  - Paix de départ (salon : 10 min par défaut, aucune, 5 ou 15) : une trêve inviolable entre grands camps d'équipes
    différentes (`Truce.Opening`) : ni rupture, ni ultimatum, ni confiance gagnée ; une seule annonce à la fin (`PeaceOver`).
  - IA patiente : marge d'attaque de 50 % ramenée à 20 % en dix minutes (40 % à 10 % en six avant), 90 s de regroupement
    entre deux frappes sur une faction rivale (`Regroup`), flottes réparées à 65 % avant de repartir. Elle rassemble aussi
    son offensive sur la planète d'un allié qui touche l'ennemi, et avance de planète amie en planète amie : deux camps
    alliés à un même troisième ne finissaient jamais leur guerre.
  - Lassitude de guerre (`Sim/GalaxyRelations.cs`, 0 à 100) : une bataille contre une autre faction en ajoute 15 × la
    part de sa puissance perdue (10 au plus), une planète perdue 6, la capitale 15, chaque front tenu 0,5 par minute ; elle
    retombe de 2 par minute, de 4 sans guerre en face. Elle coûte jusqu'à 30 % du revenu. Une IA lasse (45) accepte et
    propose la trêve même plus forte, et demande moins pour la paix ; jamais quand il ne reste que deux camps, qui
    signaient trêve sur trêve et ne finissaient plus (première mesure : lassitude à 100 partout, une partie à deux sans fin).
  - Paix durable (`DealKind.Peace`, `Truce.Lasting`) : sans fin, elle ne se rompt pas, on la dénonce (`Denounce`) : 2 min
    de trêve puis la guerre, réputation -3, confiance -10. Une IA l'accepte de qui elle estime, par lassitude ou plus
    faible ; jamais avec le camp qui domine, ni si elle n'aurait plus personne à combattre ; elle la dénonce contre le
    camp qui domine, ou quand plus personne n'est en guerre avec elle. Signée pendant la paix de départ, elle lui succède.
  - Commerce : 1 cr/s, +1 par voie commune (4 au plus), +10 % de la valeur des planètes du plus pauvre des deux, le tout
    de ×0,5 à ×1,5 en 10 min de commerce (`Pact.Since`), 30 au plus.
  - Convaincre les indépendants (`Galaxies.Court`, commande `Court`) : depuis une planète voisine, un à la fois, au prix
    de ce qui la garde plus 300 par point de revenu, moins cher avec une bonne réputation ; 120 s plus tard la planète
    rejoint le camp avec sa garnison et ses plateformes. Le premier qui finit l'emporte, les autres récupèrent la moitié ;
    attaquer la planète met fin à sa propre négociation. L'IA le fait quand ses crédits le permettent, l'agressive rarement.
  - Échanges (`DealKind.Planet`, `DealKind.Tech`), en paix ou en alliance : une planète (jamais une capitale ni la dernière)
    ou un niveau de technologie passe de l'un à l'autre contre des crédits, 0 pour un cadeau qui réchauffe une IA. L'IA
    vend au-dessus de la valeur, achète en dessous, ne vend ni au camp qui domine ni sa technologie à plus fort qu'elle.
    Les recherches d'un camp en paix avec le joueur lui sont connues (instantané).
  - Correctif : une bande errante gagnait sans fin contre des plateformes sans station (la bataille finit aussitôt, les
    plateformes restaient, la suivante repartait au pas d'après : 12 000 batailles dans une partie). Ce qui reste en orbite
    tombe maintenant avec le défenseur vaincu.
  - Réseau : protocole 15 (réglage `Peace`, camp 7), instantanés de galaxie 6, commande `Court`, `ProposeAccord` avec sa
    technologie. Tests : `EnduranceTests`, `RelationsTests`, `GalaxySizesTests`.
- Diplomatie en dialogue (2026-11-19, à la demande de l'utilisateur, maquette validée avec lui) : l'écran (touche T),
  jugé incompréhensible (un mur de boutons, une « réponse prévue » en rouge à côté de presque chacun), devient un
  dialogue avec un camp à la fois. La liste des camps reste à gauche.
  - En haut, le camp choisi : son humeur en un mot (amicale, bienveillante, réservée, méfiante, hostile,
    `GalaxyAi.Attitude` : d'après sa considération, jamais mieux que méfiante tant qu'elle garde rancune), sa raison
    dans ses mots (la cause qui a le plus fait bouger sa confiance dans ce sens ; sinon le camp qui domine, ta
    réputation, son humeur ou sa personnalité) et la jauge de confiance.
  - Cinq intentions : faire la paix (trêve, paix durable), commercer (accord commercial, vision partagée, échange
    d'une planète ou d'une technologie), s'allier (alliance, guerre commune contre chaque tiers), exiger ou menacer
    (tribut, ultimatum en crédits ou pour une planète), et une cinquième nommée d'après la paix en cours (rompre la
    trêve ou l'alliance, dénoncer la paix ; « Déclarer la guerre », éteint en guerre et pendant la paix de départ).
    Chacune porte un indice d'humeur, pas la réponse mot pour mot : ouverte, réticente, hostile, et pour les menaces
    « elle te craint » ou non (`GalaxyAi.LeaningOf`, tiré de `PreviewFor` ; réticente si des crédits ou le temps
    peuvent la décider, hostile pour une rancune, une guerre jurée, le camp qui domine, ou plus de confiance manquante
    qu'un cadeau n'en achète). Elle s'ouvre sur ses formes, chacune avec son indice ou ce qui l'empêche ; un clic propose.
  - À droite, le fil : ce que tu proposes, sa réplique (« D'accord. », « Non. » et sa raison, désormais à la première
    personne, ou une contre-offre), ses offres et demandes (aide, ultimatum, tribut, trêve…) avec leurs boutons tant
    qu'elles tiennent, ses paroles, et ce qui est signé, rompu ou fini, dit comme l'historique, qui te tutoie
    désormais (« Tu romps la trêve »). La vue garde le fil de la partie en cours, 40 lignes par camp ; une offre faite
    avant (une sauvegarde chargée) se répond au bout du fil. Une offre ne fait plus surgir sa fenêtre quand le fil de
    son camp est à l'écran. Au second plan, l'onglet « Relations et historique » : le tableau de tous les camps et
    l'historique avec le camp choisi.
  - Contre-offre (`GalaxyAi.Counter`, appelée par `Galaxies.ProposeDeal` chez l'hôte) : une IA qui refuserait ce qu'un
    joueur propose mais l'accepterait contre des crédits le dit par une offre en retour (`DealOffer.Counter`), au lieu
    d'un refus : une trêve qu'elle se croit trop forte pour signer, à son prix de paix sur une trêve, payé d'un coup ;
    une paix durable, une alliance, le commerce, la vision ou une guerre commune pour lesquels la confiance lui manque,
    au prix de la confiance qu'un cadeau peut encore acheter (40 crédits le point, `CreditsPerTrust`, 15 points au
    plus par trêve comme avant), si cela la décide ; une planète ou une technologie à son prix. Acceptée, le prix se
    paie comme un tribut d'un coup (la trêve vient avec s'ils étaient en guerre, la confiance achetée aussi), puis
    l'accord se conclut. Jamais entre IA, ni malgré une rancune, une guerre jurée ou le camp qui domine. Proposer de
    nouveau le même accord retire la contre-offre, qui aurait sinon été prise pour acceptée ; l'IA répond à neuf.
  - Supprimé : l'offre à composer (trêve, commerce, vision, tribut d'un bloc) et sa réponse prévue, les réponses
    prévues des accords à part, le détail chiffré des causes de confiance, le prix de la paix envoyé au joueur
    (`GalaxyAIState.Asked`, ce que la contre-offre dit maintenant). L'offre combinée (`DealKind.Bundle`) reste dans la
    simulation et le réseau, mais l'interface ne la propose plus.
  - Réseau : protocole 16, instantanés de galaxie 7 (le drapeau de contre-offre des offres et de `DealOffered`, plus de
    prix de paix). Le montant d'une offre et de `DealOffered` se lit fini, de 0 à `MaxTribute` (le prix d'une
    contre-offre s'affiche et se compare aux crédits du joueur), son temps fini et positif ; le fuzz couvre une
    contre-offre dans l'instantané et dans ses événements. Les sauvegardes gardent le drapeau, les anciennes se
    chargent sans. Tests : `AccordsTests` (contre-offres de trêve, d'alliance et de planète, sans les crédits,
    jamais entre IA, retirée par une nouvelle proposition, sur une réplique et dans une sauvegarde ; humeur et indices).
  - Vérification : `--deals` (avec `--seats=3`, deux IA) ouvre l'écran après une trêve et une alliance proposées à une
    IA bien plus forte, chacune contre-offerte ; `--deals=open` avant toute proposition, `--deals=accept` la trêve
    payée, `--deals=relations` l'onglet second. Captures en 1280×800 et 1280×720, en français et en anglais (aucun texte
    affiché sans anglais). Une réplique entre guillemets se traduit dans la vue (`Lang.T`), les guillemets selon la
    langue : un modèle « {0} » ne se traduisait pas, et « contre » d'une contre-offre devenait « versus ».
  - Ouvert : le fil n'est pas sauvegardé (l'historique l'est) ; une contre-offre de trêve à son prix de paix peut
    coûter cher (2 200 crédits contre une IA vingt croiseurs de bataille plus forte) ; aucune mesure d'équilibrage entre
    IA n'a été refaite, rien n'ayant changé de ce qu'elles décident entre elles.
- Campagne, plafond de flotte (2026-11-19, à la demande de l'utilisateur, à la façon d'Empire at War) : au-delà d'un millier de
  vaisseaux, une bataille de planète fatiguait l'ordinateur. Chaque camp aligne au plus N points de flotte à la fois, ses vaisseaux
  sur le terrain et ceux en saut (`World.FleetCap`, `Reserves.Points`) ; le reste attend dans la réserve de la bataille et saute dès
  qu'une place se libère. Points : petit 1 (éclaireur, chasseur, bombardier, corvette), moyen 2, croiseur et rempart 4,
  transporteur et cuirassé 6 (leurs appareils et leurs tourelles), Titan 12, Léviathan et Colosse 20 ; un héros compte comme sa
  coque. Le plafond se règle au salon de la campagne, en solo comme en réseau (l'hôte pour tous) : Faible 150, Normal 300 (par
  défaut, et celui des anciennes sauvegardes), Élevé 500. L'escarmouche et les modes n'en ont pas (0).
  - Ouverture (`Battle.CreatePlanetBattle`, `Battle.Opening`) : le défenseur commence placé, chaque classe en proportion de sa part
    des points de sa flotte (les héros toujours), puis une de plus par classe, de la plus lourde à la plus légère, tant qu'il reste
    de la place ; l'attaquant arrive en hyperespace dès le début, sa part en formation dans sa zone d'arrivée, à droite. Chaque
    camp a un point d'arrivée (`FactionState.Arrival`, à 800 près) où sa réserve peut toujours sauter, même sans rien à lui sur
    le terrain ; son point de ralliement y est mis.
  - La garde est dans `Reserves.CanDeploy` (« Plafond de flotte atteint ») ; `Deploy` n'amène d'une escadrille que ce qui tient.
    L'IA de bataille de la campagne (`Battle.UpdateBattleAI`) appelle sa réserve chaque seconde (`Reserves.CallAll`, qui ne tente
    plus un vaisseau sans place, ni une sorte qui vient de ne trouver aucun point), près de sa flotte sur le terrain (son
    ralliement suit le vaisseau le plus près du centre de la flotte), son point d'arrivée en repli. Un camp qui n'a plus rien sur
    le terrain appelle la sienne de lui-même, même commandé par un joueur : retenue, elle ne faisait que figer la bataille
    jusqu'au repli.
  - Campagnes entre IA : sous le plafond, une planète fortifiée tient mieux, les renforts de l'attaquant arrivant un à un. Grande
    galaxie, deux IA, graine 30 de `GalaxySizesTests` : 37 min sans plafond, 208 min avec des renforts sautant au bord de la carte,
    99 min près de la flotte. Graines 30 à 34 : 99, 54, 28, 50 et 42 min sous le plafond (médiane 50) ; sans lui 37, 27, 139,
    plus de 150 et 73 min (médiane 73) : le hasard des graines pèse plus que le plafond. `GalaxySizesTests` laisse deux heures en
    grande galaxie au lieu d'une.
  - Ce qui n'est jamais entré rentre tel quel sur la carte galactique : `Survivors`, `HeroSurvivors`, `SurvivorCondition`
    (l'usure de la flotte, `ReserveCondition`) et les rangs (`ReserveRanks` : les meilleurs placés d'abord, chacun sortant de la
    réserve avec le sien). Un camp qui a une réserve ou des sauts n'a pas perdu (`Outcome`). `Battle.Power` compte ce qui
    attend comme il entrera (un exemplaire de chaque sorte, dans un monde à part) : la même puissance que la flotte entière posée,
    d'où la même issue prévue. Une bataille résolue seule ne se replie plus au bout de 600 s tant qu'une réserve attend : la plus
    grosse bataille de mesure en dure 644 au plafond Faible (887 avec les renforts au bord de la carte) ; l'impasse (240 s sans
    un tir) reste la garde.
  - Issue prévue remesurée (`OUTLOOK=1`, les batailles tirées au sort sous le plafond Normal, les campagnes entre IA sous le
    leur) : 1 200 et 1 129 batailles, « Victoire presque certaine » gagnée à 98,5 et 100 %, « Favorable » à 88 et 98,6 %,
    « Indécise » à 43 et 47 %, « Défavorable » à 15,7 et 3,8 %, « Défaite presque certaine » à 1 et 0 % (97,6 et 48 % aux
    mots extrêmes et indécis avant) : les seuils restent.
  - HUD : « Flotte 289 / 300 » en tête du panneau de réserve, en orange une fois plein ; une carte sans place est grisée, avec
    ses points et la raison dans la bulle. La vue s'ouvre sur les sauts d'un attaquant encore en hyperespace.
  - Mesures (banc console en Release, `FLEETCAP=1 dotnet test Sim.Tests -c Release --filter Measures_the_fleet_cap`, deux passes,
    machine chargée : charge 10 à 17 sur 4 cœurs) sur `Stress.Planet` : deux flottes de 570 vaisseaux (trois fois celle de la
    bataille de mesure, 1 082 points chacune) au-dessus d'une planète fortifiée, 90 s de bataille, l'IA de la campagne des deux
    côtés. Les entités ne dépendent pas de la machine, les temps si.

    | Plafond | Entités (moyenne, au plus) | ms par pas (moyenne) | ms par pas (médiane) | Résolue seule |
    |---|---|---|---|---|
    | Faible 150 | 410, 459 | 0,58 à 1,06 | 0,35 à 0,56 | défenseur, 644 s |
    | Normal 300 | 868, 946 | 0,92 à 1,44 | 0,85 à 1,37 | attaquant, 324 s |
    | Élevé 500 | 1 470, 1 616 | 2,61 à 4,50 | 2,34 à 4,05 | attaquant, 260 s |
    | Aucun | 1 929, 2 788 | 3,21 à 6,78 | 2,95 à 5,52 | attaquant, 249 s |

    Entités et issues de la dernière passe (renforts près de la flotte), temps des trois. Normal reste sous mille entités, les
    deux tiers de la bataille de mesure (1 300 à 1 500, à 60 images/s aux passes de performance), et coûte quatre à cinq fois
    moins de simulation que la même bataille sans plafond, qui monte à 2 800 entités au premier choc ; Élevé vaut la bataille de
    mesure, pour une machine qui la tient ; Faible en garde le tiers. Avec les renforts au bord de la carte, Élevé donnait la
    bataille au défenseur, et Normal la gagnait en 367 s. Dans le jeu (`--start=stress --fleet-cap=normal`, gamescope sans écran,
    même machine chargée, avant les renforts près de la flotte) : 764 entités à 15 s, 16,9 ms par image en médiane (23,8 en
    moyenne), 5,5 ms de simulation.
  - Réseau : protocole 17 (réglage `FleetCap` du salon), instantanés de bataille 12 : le plafond, le point d'arrivée du joueur,
    et sa réserve en séries d'une même sorte (3 000 chasseurs tiennent en quelques octets, au lieu de deux par vaisseau). Bornes à
    la lecture : plafond et réserve à 65 536 au plus, séries d'au moins un vaisseau, point d'arrivée fini, ramené dans la carte.
    Sauvegardes : champ `fleetCap`, Normal s'il manque ; un nom inconnu refuse le fichier. Tests : `FleetCapTests`, `FuzzTests`.
- Sécurité (2026-11-03) : ce qu'un inconnu peut envoyer au serveur en ligne, à un hôte ou au jeu, et les fichiers du joueur.
  Chaque lecteur exposé passe un test de robustesse à graine fixe (`FuzzTests` : trames du relais, messages du jeu, ordres,
  instantanés de bataille entiers et en parties, galaxie, rediffusions, sauvegardes), et chaque correction a son test
  (`SecurityTests`).
  - Faille principale : un nombre à 7 bits qui ne finit jamais (cinq octets à 0xFF) levait `FormatException`, que nul
    lecteur n'attendait. Une trame de sept octets arrêtait le serveur en ligne (exception sur son fil), un message cassé de
    même l'hôte ou un joueur. Tous les lecteurs lisent désormais par `Wire.ReadVarint`, `ReadVarint64` et `ReadText`
    (`Sim/Wire.cs`), qui refusent avec `InvalidDataException` ; les textes ont une longueur bornée, rien n'est alloué pour une
    longueur forgée. Les deux serveurs en ligne consignent en plus toute erreur imprévue d'un message et ferment la seule
    connexion qui l'a envoyé.
  - Serveur en ligne : 16 connexions et 4 parties par adresse ; 4 000 trames par seconde et par connexion, au-delà écartées ;
    le débit d'un joueur vers son hôte se compte à part (256 Kio/s) : un joueur qui inonde est renvoyé, et ne peut plus faire
    fermer la partie de son hôte. Le serveur léger borne aussi ENet : aucun paquet au-delà d'1 Mio n'est assemblé (32 Mio par
    défaut).
  - Hôte : les ordres d'un joueur sont pris à 20 par seconde (40 d'un coup), la conduite à la main à part, 256 au plus en
    attente ; au-delà, ils sont écartés et le joueur est prévenu une fois. Les demandes du salon, renvoyées à tous les
    membres, sont prises à 10 par seconde (20 d'un coup). Les ordres eux-mêmes étaient déjà vérifiés (camp, unités, valeurs
    finies, énumérations, listes bornées), les spectateurs déjà sans ordres.
  - Fichiers : une sauvegarde (de campagne ou réseau) dont un champ attendu est nul, à n'importe quelle profondeur, ou dont
    une énumération n'existe pas, est refusée dès sa lecture : elle faisait planter la liste des sauvegardes et le
    chargement. Les énumérations ne se lisent plus que par leur nom. Les réglages lus sont remis dans leurs bornes (nom,
    adresses, touches, volumes, échelle, taille de fenêtre, images par seconde). Les noms de sauvegarde restaient sûrs
    (lettres, chiffres et tirets seulement), les rediffusions et les annonces du réseau local déjà refusées proprement.
  - CI : actions épinglées par leur commit, butler par sa version et son empreinte, ENet par son empreinte.
  - Liaison directe sur accord (relais 3) : l'hôte la propose au salon (éteinte par défaut, jamais gardée d'une partie à
    l'autre, seul l'hôte la change), le joueur l'accepte sur l'écran Multijoueur (case gardée en mémoire seulement). Le
    serveur en ligne (`RelayHub.Introduce`) ne présente l'un à l'autre que si les deux l'ont acceptée, tous deux en relais 3 ;
    sinon aucune trame qu'il envoie ne porte d'adresse (`RelayMeetTests`). Côté jeu, une présentation non acceptée (d'un
    serveur qui ne s'y tiendrait pas) est ignorée. Un hôte qui la propose en cours de salon présente les joueurs déjà là
    qui l'acceptent ; la retirer ne reprend pas les adresses déjà données.
  - Valeurs reçues vérifiées à la lecture (`Wire.ReadEnum`, `ReadSide`) : race, camp, classe, sorte d'entité et type,
    rôle de pièce, arme, capacité, recherche, mode, héros, dans les instantanés, leurs parties, les événements, la galaxie,
    le salon et les ordres ; une valeur inconnue rejette le message (`ValuesTests`, mêmes mutations que `FuzzTests`). Un
    spectateur ne fait plus aucune demande au salon : il pouvait s'asseoir sur une place libre et la garder.
  - Chiffrement de bout en bout (protocole 5, `Sim/Net/Secure.cs`) entre l'hôte et chaque joueur, direct ou relayé, serveur
    dédié compris : poignée de main par connexion (offre de l'hôte, réponse et preuve du joueur, preuve de l'hôte),
    ECDH P-256 éphémère, HKDF-SHA256 salé par la clé du mot de passe (PBKDF2-SHA256, 600 000 tours, sel tiré par partie ;
    zéros pour une partie ouverte), preuves HMAC de l'échange, puis AES-GCM, nonce = qui scelle, canal, compteur ; fenêtre
    de 64 contre le rejeu, par canal. Compression Brotli avant scellement (bornée à la lecture). Le défi et la clé du Hello
    disparaissent. Les deux serveurs en ligne ne déchiffrent rien et n'ont pas changé (relais 3 seulement pour l'accord).
    Mesuré sur la bataille de Stress (1 248 entités, parties d'instantané d'1 Kio) : 5 % d'octets de plus sur le fil que le
    zstd d'ENet, scellé deux fois plus lent que zstd, ouverture aussi rapide ; poignée de main 1 à 2 ms ; PBKDF2 0,8 s sur
    une machine chargée (une fois par partie chez l'hôte, une fois par connexion chez le joueur). Tests : `SecureTests`,
    `FuzzTests`.
  - Ouvert : une partie ouverte ne tient pas contre un serveur en ligne qui se mettrait au milieu (rien ne distingue alors
    l'hôte d'un autre) ; au milieu d'une partie protégée, la preuve d'un joueur permet d'essayer des mots de passe hors
    ligne (un échange de clés authentifié par mot de passe, PAKE, l'empêcherait : .NET n'en fournit pas) ; la taille et le
    rythme des paquets restent visibles ; un hôte malveillant reste cru de ses joueurs sur ce qu'il leur montre, seules les
    lectures sont bornées.
- Sécurité, passe finale (2026-11-03, avant la 1.0.0) : ce qui s'est ajouté depuis (postures, patrouilles, gardes, files,
  diplomatie, personnalités, courbes, terrain, rediffusions 3, profil, records, alerte de mise à jour). Chaque faille a
  son test, qui échouait avant (`FinalSecurityTests`, `GalaxyNetTests`) :
  - Point de ralliement fini mais hors carte (1e300) : gardé tel quel, les vaisseaux construits y partaient et
    l'instantané l'écrivait en infini. Ramené dans la carte.
  - Apparitions du bac à sable hors carte, rejouées par une rediffusion forgée : la grille spatiale plantait au premier
    pas. Ramenées dans la carte.
  - Offres de la campagne en réseau : refusée puis refaite, une offre inondait sa cible (216 en 10 s). L'hôte en prend 4
    d'un coup puis une toutes les 5 s par joueur.
  - Rediffusions dont les réglages n'existent dans aucun salon (deux places sur un camp, camp hors limites, Errants
    jouables, crédits NaN ou négatifs, score de zones nul) : lues, elles faisaient lever `CreateSkirmish` à l'ouverture.
    Refusées à la lecture (`Scenario.Check`), avec les camps du joueur, du vainqueur et des ordres.
  - Alerte de mise à jour : « v9.9.9\n » ou des chiffres d'une autre écriture passaient pour une version, dont l'adresse
    ouverte était faite. Chiffres ASCII jusqu'au bout (`Sim/Releases.cs`).
  - Ordres de galaxie : les camps se lisent bornés (`ReadSide`).
  - Déjà tenus, vérifiés : patrouilles de 15 points et files de 32 ordres bornées par l'hôte, coordonnées finies et
    ramenées dans la carte, gardes d'un allié seulement, attaque d'une cible vue ; tributs finis dans leurs bornes, pas
    d'accord avec soi-même ni un camp éliminé ; `SetPersonality` réservé au meneur, sur un bot ; `Record` borné ; terrain
    des instantanés fini ; profil, records de Survie et réglages bornés.
  - Robustesse étendue (`FuzzTests`, `ValuesTests`) : ordres de galaxie, attaque en file, instantanés en terrain dense
    avec patrouilles, gardes, postures et vétérans, galaxie et sauvegarde avec accords, tributs, offres et rangs,
    rediffusions jouées, records de Survie.
  - CI : actions épinglées par commit (vérifiés contre leurs tags), Godot, appimagetool, runtime, Inno Setup et butler par
    empreinte, permissions en lecture sauf la publication. `dotnet list package --vulnerable` : rien sur Sim, Sim.Tests,
    Game, Relay. `--outdated` : seuls les paquets de test (xunit, son lanceur, Test.Sdk), jamais livrés.
  - Ouvert : les flottants d'un instantané restent crus du joueur (un hôte malveillant reste cru) ; les crédits du bac à
    sable ne sont pas bornés (solo seulement).

## Ce qui change de technique

| Web | Godot |
|---|---|
| Phaser (sprites, caméra, Graphics) | Node2D, Sprite2D, Camera2D, `_Draw()` |
| HUD en HTML/CSS | nœuds Control + Theme |
| Web Audio | AudioStreamPlayer (sons générés ou pré-rendus) |
| localStorage | fichiers dans `user://` |
| Vite, npm, vitest | Godot, dotnet, xUnit |
