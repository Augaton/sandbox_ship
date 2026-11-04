<p align="center">
  <img src="docs/images/banner.png" alt="Sandbox Ship" width="100%">
</p>

<p align="center"><a href="README.en.md">English</a> · Français</p>

**Sandbox Ship** est un jeu de stratégie spatiale en temps réel, en pixel art. On bâtit une
station, on la défend, on lance des flottes et on conquiert une galaxie planète après planète,
seul contre l'IA ou jusqu'à six joueurs en réseau, par équipes.

**[Télécharger la dernière version](../../releases/latest)** pour Linux ou Windows.

| | |
|---|---|
| ![Le menu principal, une bataille se joue derrière](docs/images/menu.webp) | ![Une grande bataille à trois camps](docs/images/bataille.webp) |
| ![La carte de la campagne galactique](docs/images/campagne.webp) | ![Le salon d'une escarmouche à quatre, en deux équipes](docs/images/salon.webp) |

## Le jeu

- **Campagne galactique** : des planètes reliées par des routes, sur une galaxie petite, moyenne
  ou grande. Revenus, stations, mines et plateformes, recherche par paliers, trêves entre
  camps, flottes qui s'interceptent sur les routes. Chaque rencontre se commande en bataille ou
  se résout seule.
- **Escarmouche**, et ses variantes : Survie (des vagues d'Errants), Contrôle de zone (des
  balises à tenir), Assaut (une forteresse à prendre à temps), Régicide (chacun son vaisseau
  amiral).
- **Bac à sable** : IA coupée, crédits en abondance, on pose ce qu'on veut où on veut.
- **Trois races** jouables, chacune avec son trait et trois héros : les Kla'ed (armes +15 %),
  les Nairan (boucliers +40 %), les Nautolan (coques qui se régénèrent). Les Errants, neutres,
  gardent les sites de raid.
- **Treize classes de vaisseaux**, de l'éclaireur au Colosse. Les gros ont des pièces
  destructibles : tourelles, réacteurs, générateurs de bouclier, hangars. Balles, lasers,
  torpilles, rayons, faisceaux continus, rafales à recharge, essaims de missiles et canons à
  ions ; la flak abat les missiles en vol. En fin de partie, la Lance stellaire frappe n'importe
  où sur la carte.
- **Une IA** qui s'adapte à ce qu'elle voit, et des vaisseaux qui choisissent leurs cibles,
  tiennent leurs distances, esquivent les frappes annoncées et se replient pour se faire réparer.
- **En réseau**, jusqu'à six joueurs, campagne comprise : en réseau local (les parties
  s'affichent d'elles-mêmes), par le serveur en ligne (rien à ouvrir chez soi) ou sur un
  serveur dédié.
- **Une musique écrite et jouée par le jeu**, sans aucun fichier son : deux thèmes au menu,
  trois dans la galaxie, trois en bataille, dont les couches montent avec les combats.
- **En français ou en anglais**, au choix dans les options.

## Jouer

Les fichiers de chaque version sont sur la page [Releases](../../releases). Les pré-releases
`main-build` et `dev-build` portent le dernier état de chaque branche, refait à chaque push.

- **Linux, AppImage** : télécharger `SandboxShip-<version>-linux-x86_64.AppImage`, le rendre
  exécutable (`chmod +x SandboxShip-*.AppImage`, ou Propriétés, « Exécutable »), puis le lancer.
  Il faut FUSE (paquet `fuse3` ou `fuse`), présent sur la plupart des distributions ; sans lui,
  lancer l'AppImage avec `--appimage-extract-and-run`. Les sauvegardes restent dans
  `~/.local/share/godot/app_userdata/Sandbox Ship`, hors de l'AppImage : une version plus
  récente les retrouve.
- **Windows, installeur** : lancer `SandboxShip-<version>-windows-x86_64-setup.exe`. Il installe
  le jeu pour l'utilisateur seul, sans droits d'administrateur, dans
  `%LOCALAPPDATA%\Programs\Sandbox Ship`, avec un raccourci au menu Démarrer ; une version plus
  récente s'installe par-dessus. Pour désinstaller : Paramètres, Applications, Sandbox Ship. Les
  sauvegardes, les réglages et le profil (`%APPDATA%\Godot\app_userdata\Sandbox Ship`) restent,
  sauf si l'on choisit de les effacer. Sans fenêtre : `SandboxShip-…-setup.exe /VERYSILENT`, et
  `unins000.exe /VERYSILENT` dans le dossier du jeu, qui garde alors les sauvegardes.
- **Archives** : extraire l'archive Linux, puis lancer `./SandboxShip.x86_64` ; ou le zip
  Windows, puis lancer `SandboxShip.exe`. L'exécutable et son dossier `data_Game_*` restent côte
  à côte.

Ni l'installeur ni l'exécutable Windows ne sont signés : si Windows affiche « Windows a protégé
votre ordinateur », choisir « Informations complémentaires », puis « Exécuter quand même ».

Les commandes principales en bataille ; toutes sont dans le jeu, Options, onglet « Commandes » :

| Touche | Effet |
|---|---|
| Clic gauche, glisser | Sélectionner |
| Clic droit | Déplacer, ou attaquer la cible visée |
| F puis clic | Attaque-mouvement : engage tout ce qui croise la route |
| E | Capacité des vaisseaux sélectionnés |
| P | Piloter le vaisseau sélectionné |
| B | Ouvrir ou replier la production |
| Ctrl + 1 à 9, puis 1 à 9 | Créer un groupe, puis le rappeler |
| ZQSD ou flèches, molette | Déplacer la vue, zoomer |
| Espace | Pause |

## Compiler

Il faut le SDK .NET 10 et Godot 4.7.2 .NET, avec ses modèles d'export pour exporter.

```bash
dotnet test godot/Sim.Tests       # tests de la simulation, sans Godot
godot --path godot/Game           # lance le jeu
godot -e --path godot/Game        # ouvre l'éditeur

cd godot/Game                     # exports
mkdir -p ../build/linux ../build/windows
godot --headless --export-release "Linux" ../build/linux/SandboxShip.x86_64
godot --headless --export-release "Windows" ../build/windows/SandboxShip.exe
```

L'AppImage se fait à partir de l'export Linux par `godot/packaging/appimage.sh`, l'installeur
Windows par Inno Setup avec `godot/packaging/SandboxShip.iss` ; les versions des outils et leur
usage sont dans la CI.

Les options de lancement, le jeu en réseau et les serveurs sont décrits dans
[godot/README.md](godot/README.md) ; l'avancement et les règles propres à cette version, dans
[godot/PLAN.md](godot/PLAN.md).

## Intégration continue et versions

La CI GitHub ([.github/workflows/build.yml](.github/workflows/build.yml)) lance les tests, puis
les exports Linux et Windows, à chaque push sur `main` ou `dev` et à chaque pull request, et
garde dans le run les archives, l'AppImage et l'installeur Windows. Elle les publie ensuite :

- un push sur `main` ou `dev` refait la pré-release de la branche (`main-build`, `dev-build`).
  Ses fichiers gardent le même nom d'un commit à l'autre, donc la même adresse de
  téléchargement, par exemple `releases/download/dev-build/SandboxShip-dev-linux-x86_64.tar.gz` ;
- un tag `vX.Y.Z` publie la release de la version, après avoir vérifié que le jeu porte bien ce
  numéro ;
- une pull request ne publie rien.

Avant toute publication, les deux exports sont lancés sans fenêtre, chacun sur son système
(Linux, puis Windows sur une machine Windows de GitHub) : une bataille et la campagne doivent
tourner quelques centaines d'images et s'arrêter d'elles-mêmes. L'AppImage lance de même une
bataille. Sur la machine Windows, l'installeur s'installe en silence, le jeu installé lance une
bataille, puis le désinstalleur l'enlève en gardant les fichiers du joueur. Les paquets sont
faits par appimagetool et son runtime, et par Inno Setup, chacun téléchargé à une version et
une empreinte figées ([godot/packaging/](godot/packaging/)).

Une version part aussi sur itch.io si le dépôt nomme la page du jeu dans la variable
`ITCH_GAME` (par exemple `augaton/sandbox-ship`, dans Settings, Secrets and variables, Actions,
onglet Variables) et que la clé de butler est dans le secret `BUTLER_API_KEY` (créée sur
<https://itch.io/user/settings/api-keys>). Les canaux sont `linux` et `windows`.

`dev` reçoit le travail en cours, `main` les versions publiées. Pour publier une version :

1. Sur `dev`, mettre le numéro dans `godot/Game/project.godot` (`config/version`),
   `godot/Game/scripts/Ui/Menu.cs` (`Version`) et `godot/Game/export_presets.cfg`
   (`file_version` et `product_version`, avec un `.0` de plus), puis commiter.
2. Avancer `main` sur `dev`, poser le tag et pousser :

```bash
git push . dev:main
git tag v1.1.0 main
git push origin main dev v1.1.0
```

## Le dépôt

- `godot/` : le jeu, en Godot 4.7 et C#.
  - `Sim/` : la simulation, en C# pur, déterministe et à pas fixe.
  - `Sim.Tests/` : ses tests (xUnit).
  - `Game/` : le projet Godot, avec le rendu, l'interface et le réseau.
- `src/` et `tests/` : la [version web](docs/version-web.md), en TypeScript et Phaser, gelée ;
  elle sert de référence aux tests de la simulation.
- `docs/` : les images de ce README et la documentation de la version web.

## Crédits et licence

Sprites : les packs Void de Foozle et Space Breaker de zintoki, en CC0. Logo de Godot Engine :
Andrea Calabró, en CC BY 4.0. Le détail est dans [CREDITS.md](CREDITS.md).

Le code du jeu est sous licence [MIT](LICENSE). Les œuvres citées ci-dessus gardent la leur.
