# Sandbox Ship — version Godot

Version native du jeu, en Godot 4.7 et C# : voir [PLAN.md](PLAN.md) pour l'avancement et les
règles propres à cette version.

## Outils installés

- SDK .NET 10 dans `~/.dotnet`, commande `dotnet` (lien dans `~/.local/bin`).
- Godot 4.7.2 .NET dans `~/.local/opt/Godot_v4.7.2-stable_mono_linux_x86_64`, commande
  `godot` (lien dans `~/.local/bin`).

Pour couper la télémétrie de .NET, ajouter à `~/.bashrc` :
`export DOTNET_CLI_TELEMETRY_OPTOUT=1`.

## Lancer

Depuis la racine du dépôt :

```bash
godot --path godot/Game                     # lance le jeu
godot -e --path godot/Game                  # ouvre l'éditeur (F5 lance le jeu)
dotnet test godot/SandboxShip.slnx          # tests de la simulation, sans Godot
# mesures longues, à la demande : BALANCE=1 (escarmouches) ou CAMPAIGN=1 (campagnes entre IA)
# dotnet test godot/Sim.Tests --filter Balance --logger "console;verbosity=detailed"
npm run fixtures                            # références et apparences tirées de la version web
```

À la première ouverture, l'éditeur importe le projet ; s'il signale l'assemblage C#
manquant, le bouton « Build » (marteau, en haut à droite) le compile.

Options pour vérifier un écran sans passer par le menu, après `--` :
`--start=menu|battle|sandbox|campaign|gallery` (`gallery` : chaque classe de vaisseau et chaque
niveau de plateforme, en pause), `--skip=SECONDES` (partie jouée d'avance par l'IA), `--zoom=Z`,
`--at=X,Y` (centre la vue de bataille), `--menu=home|new|controls|credits|options|lobby-MODE` (`new` : les
modes ; `lobby-skirmish`, `lobby-campaign`, `lobby-assault`… : le salon de ce mode, que `--seats=N` remplit
d'IA et où `--teams=1,2,…` place les équipes dans l'ordre des places),
`--size=small|medium|large` (carte ou galaxie de `--start`), `--mode=survival|zones|assault|regicide`
(règles de `--start=battle` ; en assaut, le joueur défend), `--shot=image.png --frames=N`
(capture puis quitte ; les images se comptent une fois le chargement fini), `--dump=dossier`
(sprites composés, et `boot.png`, le logo de l'écran de démarrage). Ajouter `--disable-vsync`
avant `--` si la fenêtre est cachée : sinon le compositeur la ralentit. Avec `--shot`, la
fenêtre ne prend pas le clavier, et ni les réglages ni la sauvegarde de campagne ne sont touchés.
Pour une capture sans aucune fenêtre à l'écran (gamescope, déjà installé) :
`gamescope --backend headless -W 1280 -H 800 -w 1280 -h 800 -- godot --path godot/Game --disable-vsync -- --shot=image.png`.

## Jouer en réseau

Les batailles et la campagne se jouent à plusieurs, en réseau local ou par Internet. Tout passe par l'écran
« Multijoueur » du menu principal.

- **La liste** : toutes les parties à portée, une ligne chacune (nom, mode, joueurs, type, ping), dans la couleur
  de son type : vert pour le réseau local (annonces UDP), bleu pour les parties hébergées en ligne (par le serveur
  en ligne), violet pour les serveurs dédiés (inscrits au serveur en ligne ou trouvés sur le réseau local). Elle se
  met à jour d'elle-même ; « Actualiser » redemande la liste au serveur en ligne. La ligne du haut dit s'il répond.
- **Rejoindre** : choisir une ligne, puis « Rejoindre la partie » (ou double clic, ou Entrée sur la ligne choisie).
  « Par adresse » rejoint un hôte à son `adresse:port` (port 24680 par défaut). Ton nom se change dans les options.
  Une partie protégée porte un cadenas : son mot de passe se tape sous l'adresse, il ne part jamais tel quel (l'hôte
  donne un défi, le jeu répond par une clé) et n'est jamais enregistré. « Regarder » entre sans place, au salon ou en
  pleine bataille, si l'hôte prend des spectateurs ; une barre choisit alors la vue (tout, sans brouillard, ou un camp).
  Coupé d'une partie en cours, « Revenir dans … » rend sa place sous le même nom : l'IA la tenait en attendant.
- **Discussion** : un panneau dans le salon ; en bataille et sur la galaxie, Entrée ouvre la ligne à écrire. L'hôte
  relaie chaque ligne à tous (texte brut, 160 lettres, quatre d'affilée puis une toutes les 1,5 s).
- **Liaison directe** : en ligne, le serveur présente l'hôte et chaque joueur l'un à l'autre ; s'ils se joignent
  directement (la plupart des box le permettent), leurs messages ne passent plus par lui, sinon tout reste relayé. Le
  salon dit laquelle est en place. Le serveur en ligne doit être de cette version (relais 2) ; il sert aussi les jeux
  de la précédente (relais 1), sans liaison directe.
- **Héberger en local** : puis le mode. Le salon s'ouvre avec toi et une place « Joueur distant · libre » ; il
  donne l'adresse à communiquer. Tu règles tout ; chaque joueur règle sa place et se dit prêt ; « Lancer la partie »
  s'allume quand tout le monde l'est. Sur Internet, l'hôte ouvre le port **24680 en UDP** vers sa machine (box,
  pare-feu). Les annonces du réseau local passent par le port 24681 en UDP (diffusion) : si un pare-feu les bloque,
  rejoindre par adresse marche toujours.
- **Héberger en ligne** (allumé quand le serveur en ligne répond) : la même chose, mais la partie s'inscrit au
  serveur en ligne et tout passe par lui ; personne n'ouvre de port. Elle s'affiche dans la liste de chacun tant
  que l'hôte reste, et disparaît quand il part.
- **Campagne** : héberger, puis « Campagne galactique ». L'hôte règle la vitesse pour tous, chacun peut mettre en
  pause. Elle se sauvegarde chez l'hôte (`user://campaign-net.json`) ; pour la reprendre, héberger (en local ou en
  ligne), puis « Reprendre la campagne » : chaque joueur retrouve sa place en rejoignant sous le même nom (son nom se
  règle dans les options), ou l'hôte la donne à un bot. Pas de campagne sur serveur dédié.
- **Serveur en ligne** : `51.91.214.135:27029` par défaut, à changer dans les options (« Serveur en ligne »).

**Serveur dédié**, sans fenêtre ni joueur à lui ; le premier joueur arrivé mène le salon (réglages, places,
lancement), le suivant s'il part :

```bash
godot --headless --path godot/Game -- --server --port=24680 --name="Mon serveur" --mode=zones
```

`--mode` (`survival`, `zones`, `assault`, `regicide` ; une escarmouche sans lui) fixe le mode du salon. La campagne n'y est pas.
Le serveur écrit dans la console qui arrive et part, les lancements, et toutes les dix secondes ce que coûte
la bataille (instantanés par seconde et par joueur, temps d'écriture, débit avant et après compression).
Ctrl+C l'arrête. Avec `--public`, il s'inscrit aussi au serveur en ligne (`--master`, sinon celui des options) et
s'affiche dans la liste de tous, à l'adresse d'où le serveur en ligne le voit et sur son `--port`
(`--public=ADRESSE` pour en donner une autre) ; les joueurs le rejoignent directement : son port doit être ouvert.

**Serveur en ligne** (le pont entre ceux qui hébergent et ceux qui rejoignent) : il tient la liste des parties et
relaie celles hébergées en ligne, entre chaque joueur et son hôte, avec la fiabilité et le canal de chaque
message. Ce n'est pas un serveur de jeu : il ne simule rien. Même exécutable que le jeu, sans fenêtre :

```bash
godot --headless --path godot/Game -- --relay --port=27029
# exporté : ./SandboxShip.x86_64 --headless -- --relay --port=27029
```

Il n'ouvre que ce port (UDP). Sa ligne « à l'écoute sur le port » dit qu'il est prêt ; il écrit chaque partie ouverte
ou fermée, chaque joueur qui entre ou part, et toutes les dix secondes sa charge : parties, joueurs, débit avant et
après compression, paquets écartés, processeur. Limites : 32 parties, 8 joueurs par partie, 2 Mio/s par partie avant
compression (au-delà, les instantanés sont écartés ; une partie qui dépasse de loin en messages fiables est fermée),
512 connexions, 5 s pour se présenter, messages d'1 Mio au plus. Un joueur n'atteint que l'hôte de sa partie, un
hôte que ses joueurs ; une autre version du jeu est refusée avec la raison. Ctrl+C l'arrête après avoir prévenu
chacun. Sur Pelican, l'egg `hosting/pelican/egg-sandbox-ship.yaml` le lance avec le rôle `relay`.

Pour vérifier sans clic, après `--` : `--host` (héberge un salon de `--mode` avec une place libre, et lance
trois secondes après que tout le monde est prêt), `--join=ADRESSE:PORT` (rejoint, et se dit prêt une fois
assis), `--name=NOM`, `--port=N`, `--order=SECONDES` (les vaisseaux du joueur, construits au départ, partent en
attaque-mouvement vers le centre à cet âge de la bataille), `--pause=SECONDES` (le joueur met alors la partie
en pause pour tous), `--start=stress` (avec `--host` : la bataille de mesure, servie au joueur), `--menu=multiplayer` (l'écran « Multijoueur »), `--master=ADRESSE:PORT` (le serveur en ligne, à la
place de celui des options), `--online` (avec `--host` : héberge en ligne), `--join=online` (choisit dans la liste la
première partie hébergée en ligne, puis la rejoint par le serveur en ligne), `--beacon=PORT` (les annonces du réseau
local sur un autre port que 24681), `--probe` (mesure l'aller-retour d'un message, directement sur `--port` puis par
`--master`, et s'arrête), `--password=MOT` (la partie hébergée, ou le serveur dédié, en demande un ; celui avec
lequel `--join` répond), `--spectate` (avec `--join` : entre en spectateur), `--say=TEXTE` (une ligne dans la
discussion, une fois assis), `--no-direct` (en ligne, jamais de liaison directe : tout reste relayé). En campagne (`--host --mode=campaign`) : `--speed=N` (vitesse de la galaxie,
chez l'hôte), `--attack=S,…` et `--raid=S,…` (à ces âges de la galaxie, les flottes du joueur partent vers la planète la plus proche
d'un autre joueur, ou vers celle des Errants), `--build=S,…` (des frégates à chaque planète, autant que les crédits le permettent),
`--choose=command|auto,…` (réponses aux invites de bataille, dans l'ordre, la dernière répétée), `--truce=S` (une trêve proposée à
l'autre joueur), `--answer=yes|no` (réponse aux offres), `--watch` (regarde une bataille commandée par d'autres), `--pause=S` (pause
pour tous à cet âge, reprise 4 s après), `--save=FICHIER` (la sauvegarde de l'hôte, même avec `--shot` ; ce que lit `--resume`),
`--resume` (avec `--host` : le salon de la campagne sauvegardée), `--snap=DOSSIER` (captures numérotées aux moments clés : invites,
batailles et leur fin, offres, salon, départ). Par exemple, un hôte et un joueur sans fenêtre :

```bash
cd godot/Game
gamescope --backend headless -W 1280 -H 800 -w 1280 -h 800 -- godot --path . --disable-vsync --max-fps 60 -- --host --order=30 --frames=3000 --shot=hote.png &
gamescope --backend headless -W 1280 -H 800 -w 1280 -h 800 -- godot --path . --disable-vsync --max-fps 60 -- --join=127.0.0.1:24680 --name=Bob --order=30 --frames=2400 --shot=joueur.png
```

Et une campagne : Bob attaque Alice, tous deux commandent la bataille, puis Bob propose une trêve qu'Alice accepte ; l'hôte
sauvegarde dans `/tmp/net.json`, que `--resume` reprend ensuite, Bob rejoignant sa place :

```bash
gamescope --backend headless -W 1280 -H 800 -w 1280 -h 800 -- godot --path . --disable-vsync --max-fps 60 -- --host --mode=campaign --size=small --name=Alice --speed=4 --choose=command --answer=yes --order=5 --save=/tmp/net.json --snap=/tmp/alice --frames=13000 --shot=alice.png &
gamescope --backend headless -W 1280 -H 800 -w 1280 -h 800 -- godot --path . --disable-vsync --max-fps 60 -- --join=127.0.0.1:24680 --name=Bob --attack=20 --truce=90 --choose=command --order=5 --snap=/tmp/bob --frames=12800 --shot=bob.png
```

Par un serveur en ligne local, sur un port d'essai : Alice héberge en ligne, Bob la trouve dans la liste et la rejoint
par le relais, sans aucun port de jeu ouvert entre eux (`--mode=campaign` chez Alice pour une campagne) :

```bash
godot --headless --path . -- --relay --port=27129 &
gamescope --backend headless -W 1280 -H 800 -w 1280 -h 800 -- godot --path . --disable-vsync --max-fps 60 -- --host --online --master=127.0.0.1:27129 --name=Alice --order=30 --frames=5400 --shot=alice.png &
gamescope --backend headless -W 1280 -H 800 -w 1280 -h 800 -- godot --path . --disable-vsync --max-fps 60 -- --join=online --master=127.0.0.1:27129 --name=Bob --order=30 --snap=/tmp/bob --frames=6600 --shot=bob.png
```

## Traduction

Le code écrit en français ; l'anglais est dans `Game/lang/en.po`, avec pour clé le texte
français, et `{0}`, `{1}`… pour les valeurs d'un texte composé (`$"Coque {a} / {b}"` devient
« Coque {0} / {1} »). Le jeu traduit ce qu'il affiche au moment où il l'affiche
(`Game/scripts/Core/Lang.cs`) : les contrôles de Godot le font d'eux-mêmes, le texte dessiné à la
main passe par `Lang.T`. Côté anglais, `{1:s}` écrit « s » si la valeur 1 n'est pas vide (un
pluriel français construit par le code), `{1:one|many}` choisit l'une des deux formes. La
simulation garde son français : ses textes sont traduits à l'affichage, comme le reste.

```bash
python3 godot/tools/lang.py --check   # textes du code sans anglais (sortie 1 s'il en manque)
python3 godot/tools/lang.py --add     # les ajoute à en.po, à traduire
python3 godot/tools/lang.py --prune   # retire les entrées vides que le code n'affiche plus
godot/tools/lang-check.sh "godot --path godot/Game" manquants.txt captures/   # textes vus en jeu sans anglais
```

`--lang=fr|en` choisit la langue d'un lancement, `--lang-report=FICHIER` y écrit chaque texte
affiché sans anglais.

## Exporter

Les modèles d'export 4.7.2 .NET sont dans `~/.local/share/godot/export_templates`.

```bash
cd godot/Game
mkdir -p ../build/linux ../build/windows   # Godot ne crée pas le dossier d'export
godot --headless --export-release "Linux" ../build/linux/SandboxShip.x86_64
godot --headless --export-release "Windows" ../build/windows/SandboxShip.exe
```

Chaque export donne l'exécutable et un dossier `data_Game_*` à garder à côté de lui. La CI
(`.github/workflows/build.yml`) fait les mêmes exports depuis un clone neuf, après
`godot --headless --import` ; un tag `vX.Y.Z` les publie en release (voir le README à la racine).
