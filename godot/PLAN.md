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
- Succès et statistiques (2026-11-03) : 36 succès, dont 4 cachés, et les statistiques du joueur, en logique pure dans
  `Sim/Profile.cs` (`ProfileTests`), comptés côté jeu par `Core/PlayerProfile.cs` et gardés dans `user://profile.json`
  (écrit par fichier temporaire, relu borné : un fichier cassé ou forgé donne un profil vide ou borné). Seul le joueur
  local compte, à la fin de chaque bataille et de chaque campagne ; jamais une capture (`--shot`, `--snap`, sauf avec
  `--profile=FICHIER`), une rediffusion, le didacticiel, un spectateur ni la bataille du menu. Le temps de jeu est le temps
  réel passé en bataille ou dans la galaxie, quelle que soit la vitesse. Écran « Profil » au menu, notification au déblocage.
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

## Ce qui change de technique

| Web | Godot |
|---|---|
| Phaser (sprites, caméra, Graphics) | Node2D, Sprite2D, Camera2D, `_Draw()` |
| HUD en HTML/CSS | nœuds Control + Theme |
| Web Audio | AudioStreamPlayer (sons générés ou pré-rendus) |
| localStorage | fichiers dans `user://` |
| Vite, npm, vitest | Godot, dotnet, xUnit |
