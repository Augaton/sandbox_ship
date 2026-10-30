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

Les batailles se jouent à plusieurs (la campagne pas encore), en réseau local ou par Internet.

- **Héberger** : menu principal, « Héberger une partie », puis le mode. Le salon s'ouvre avec toi et une
  place « Joueur distant · libre » ; il donne l'adresse à communiquer. Tu règles tout ; chaque joueur règle sa
  place et se dit prêt ; « Lancer la partie » s'allume quand tout le monde l'est.
- **Rejoindre** : menu principal, « Rejoindre ». Les parties du réseau local s'affichent d'elles-mêmes ;
  sinon, saisir `adresse:port` (port 24680 par défaut). Ton nom se change dans les options.
- Sur Internet, l'hôte ouvre le port **24680 en UDP** vers sa machine (box, pare-feu). Les annonces du réseau
  local passent par le port 24681 en UDP (diffusion) : si un pare-feu les bloque, rejoindre par adresse marche
  toujours.

**Serveur dédié**, sans fenêtre ni joueur à lui ; le premier joueur arrivé mène le salon (réglages, places,
lancement), le suivant s'il part :

```bash
godot --headless --path godot/Game -- --server --port=24680 --name="Mon serveur" --mode=zones
```

`--mode` (`survival`, `zones`, `assault`, `regicide` ; une escarmouche sans lui) fixe le mode du salon.
Le serveur écrit dans la console qui arrive et part, les lancements, et toutes les dix secondes ce que coûte
la bataille (instantanés par seconde et par joueur, temps d'écriture, débit avant et après compression).
Ctrl+C l'arrête.

Pour vérifier sans clic, après `--` : `--host` (héberge un salon de `--mode` avec une place libre, et lance
trois secondes après que tout le monde est prêt), `--join=ADRESSE:PORT` (rejoint, et se dit prêt une fois
assis), `--name=NOM`, `--port=N`, `--order=SECONDES` (les vaisseaux du joueur, construits au départ, partent en
attaque-mouvement vers le centre à cet âge de la bataille), `--pause=SECONDES` (le joueur met alors la partie
en pause pour tous), `--menu=join` (l'écran « Rejoindre »). Par exemple, un hôte et un joueur sans fenêtre :

```bash
cd godot/Game
gamescope --backend headless -W 1280 -H 800 -w 1280 -h 800 -- godot --path . --disable-vsync --max-fps 60 -- --host --order=30 --frames=3000 --shot=hote.png &
gamescope --backend headless -W 1280 -H 800 -w 1280 -h 800 -- godot --path . --disable-vsync --max-fps 60 -- --join=127.0.0.1:24680 --name=Bob --order=30 --frames=2400 --shot=joueur.png
```

## Exporter

Les modèles d'export 4.7.2 .NET sont dans `~/.local/share/godot/export_templates`.

```bash
cd godot/Game
godot --headless --export-release "Linux" ../build/linux/SandboxShip.x86_64
godot --headless --export-release "Windows" ../build/windows/SandboxShip.exe
```

Chaque export donne l'exécutable et un dossier `data_Game_*` à garder à côté de lui.
