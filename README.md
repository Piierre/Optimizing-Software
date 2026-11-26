# Optimizing Software

Application gratuite pour Windows 10 et 11 qui garde le PC propre et rapide : elle trouve ce qui s'accumule avec le temps (fichiers temporaires, vieux téléchargements, gros fichiers, doublons, restes de programmes désinstallés, programmes inutiles au démarrage) et aide à le retirer, sans jamais rien casser.

- **Simple** : un bouton « Nettoyer » sur l'accueil, le reste dans un menu à gauche.
- **Sans risque** : rien n'est supprimé directement. Tout passe par une corbeille interne et peut être restauré pendant 7 jours.
- **Privé** : aucune donnée ne quitte le PC. La seule connexion sert à vérifier s'il existe une nouvelle version.
- **Sans droits administrateur** pour l'essentiel ; un bouton « Relancer en administrateur » débloque le reste.

## Fonctionnalités

### Accueil : Analyser puis Nettoyer

Au lancement, l'appli analyse le PC et résume ce qu'elle a trouvé :

| Ligne | Ce qui est compté |
| --- | --- |
| Fichiers inutiles | Fichiers temporaires, rapports de plantage, caches des navigateurs, de Discord et de Steam |
| Vieux installateurs dans Téléchargements | Installateurs (.exe, .msi…) et images disque de plus de 3 mois |
| Restes d'applications désinstallées | Dossiers laissés par des programmes qui ne sont plus là |
| Programmes lancés au démarrage | Programmes qu'on peut désactiver sans risque |

On coche ce qu'on veut, on clique sur **Nettoyer**. Le bouton « Voir » de chaque ligne ouvre le détail dans la page concernée.

### Menu à gauche

| Page | Ce qu'on y fait |
| --- | --- |
| **Libérer de la place** | Fichiers temporaires, rapports de plantage, caches (Chrome, Edge, Brave, Opera GX, Firefox, Discord, Steam). Les caches de shaders sont proposés mais décochés : les jeux saccaderaient le temps de les reconstruire. En administrateur : `C:\Windows\Temp`, téléchargements de Windows Update, anciens journaux d'installation. On y trouve aussi la corbeille interne (restaurer, supprimer, vider) et la corbeille de Windows. |
| **Gros fichiers et doublons** | Vieux téléchargements (plus de 3 mois), fichiers de plus de 500 Mo, doublons de plus de 1 Mo (contenu identique, SHA-256). On choisit les disques à analyser. L'original d'un doublon est toujours gardé. |
| **Jeux et logiciels** | Liste des applications installées et des jeux Steam, Epic Games et Xbox (EA, Ubisoft, Battle.net, GOG et Riot sont reconnus), avec taille, disque et launcher. « Désinstaller » lance le désinstallateur officiel, ou passe par le launcher (Steam, Riot…) quand c'est lui qui gère le jeu, puis cherche ce qui reste. Deux sous-onglets : **Dossiers restants** (Program Files, AppData, ProgramData) et **Entrées cassées** (applications encore listées alors qu'elles ont disparu, sauvegardées en `.reg` avant d'être retirées). |
| **Démarrage plus rapide** | Programmes lancés au démarrage, avec un conseil (à garder, à désactiver, à voir, cassé). La désactivation utilise les mêmes réglages que le Gestionnaire des tâches et se réactive d'un clic. Sous-onglet **Processus** : programmes en cours, mémoire, processeur, signature, avec les suspects et les gourmands mis en avant. |

**Plus d'outils**

| Page | Ce qu'on y fait |
| --- | --- |
| **Vérifier le PC** | Plantages de la carte graphique, fréquence des écrans, âge du pilote graphique, planification GPU, barrettes et vitesse de la RAM (XMP/EXPO), mémoire utilisée, disque de Windows (SSD, place libre), mode d'alimentation, processeur. |
| **Analyser une partie** | On joue 2 à 10 minutes : l'appli mesure processeur, carte graphique, RAM, disque et température GPU (NVIDIA), dit ce qui limite et donne des conseils. |

### Garde-fous

- Corbeille interne dans `%LOCALAPPDATA%\OptimizingSoftware\Corbeille` (ou `OptimizingSoftware.Corbeille` à la racine d'un autre disque), restauration pendant 7 jours, jamais d'écrasement d'un fichier existant.
- Les dossiers de Windows et les dossiers partagés ou sensibles (Microsoft, Common Files, pilotes, launchers de jeux, sauvegardes de jeux…) ne sont jamais proposés. Les dossiers des jeux installés ne sont jamais parcourus.
- Liens symboliques, jonctions et fichiers en cours d'utilisation ignorés.
- Chaque entrée du registre est exportée en `.reg` avant d'être retirée ; si l'export échoue, rien n'est retiré.
- Vider la corbeille de Windows demande une confirmation qui précise que c'est définitif. « Fermer » est refusé pour les processus indispensables de Windows.
- En cas d'erreur inattendue, l'appli ne se ferme pas et note l'erreur dans `%LOCALAPPDATA%\OptimizingSoftware\erreurs.log`.

Pas encore couvert : les applications du Microsoft Store, la température du processeur (Windows ne la donne pas sans pilote spécial).

## Installation

1. Ouvrir la page [Releases](https://github.com/Piierre/Optimizing-Software/releases) et télécharger `OptimizingSoftware-Setup-x.y.z.exe`.
2. Lancer l'installeur. Il installe l'appli pour ton compte, sans droits administrateur, dans `%LOCALAPPDATA%\Programs\Optimizing Software`, avec un raccourci dans le menu Démarrer (et sur le Bureau si tu le coches).
3. Pour désinstaller : Paramètres > Applications > Optimizing Software. La corbeille interne est gardée, au cas où il resterait des fichiers à restaurer.

Au lancement, l'appli vérifie s'il existe une version plus récente et affiche un bandeau avec un bouton de téléchargement.

Sans installeur : chaque build de l'onglet **Actions** publie aussi l'exécutable seul (artefact `OptimizingSoftware-win-x64`).

## Compiler depuis le code

Prérequis : Windows 10 (1809 ou plus) ou Windows 11, et le [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/Piierre/Optimizing-Software.git
cd Optimizing-Software
dotnet run --project src/OptimizingSoftware.App
```

Avec Visual Studio 2026 : ouvrir `OptimizingSoftware.slnx`, choisir `OptimizingSoftware.App` comme projet de démarrage, puis F5.

Lancer les tests :

```powershell
dotnet test
```

Créer l'exécutable seul, comme la CI :

```powershell
dotnet publish src/OptimizingSoftware.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

## Organisation du code

```
Optimizing-Software/
├── src/
│   ├── OptimizingSoftware.Core/      Règles et modèles, sans appel à Windows (testable partout)
│   │   ├── Apps/                     Jeux et logiciels, restes de désinstallation
│   │   ├── Cleanup/                  Règles de nettoyage, corbeille interne, sécurité des chemins
│   │   ├── Diagnostics/              Vérifications du PC (Checks/) et moteur de diagnostic
│   │   ├── Files/                    Vieux téléchargements, gros fichiers, doublons, choix des disques
│   │   ├── Processes/                Classement des processus
│   │   ├── Recording/                Analyse d'une partie et conseils d'upgrade
│   │   ├── Startup/                  Programmes au démarrage
│   │   └── Updates/                  Recherche de mise à jour
│   ├── OptimizingSoftware.Windows/   Lectures et actions Windows : WMI, registre, journaux, NVML, launchers…
│   └── OptimizingSoftware.App/       Interface WPF (.NET 10, thème Fluent) : fenêtres et ViewModels/
├── tests/
│   ├── OptimizingSoftware.Core.Tests/     Tests des règles sur des dossiers temporaires
│   └── OptimizingSoftware.Windows.Tests/  Vraies lectures Windows, lancées sur la machine Windows de GitHub (ignorées ailleurs)
├── installer/                        Script Inno Setup de l'installeur
├── .github/workflows/                build.yml (compilation, tests, installeur) et release.yml (publier une version)
├── Directory.Build.props             Réglages communs et numéro de version
└── OptimizingSoftware.slnx           Solution
```

## Intégration continue et versions

- À chaque modification, GitHub Actions compile sur Windows, lance les tests, ouvre l'appli publiée avec `--smoke-test` (toutes les analyses d'ouverture, échec au moindre plantage) et crée l'installeur.
- La version est dans `Directory.Build.props` et s'affiche dans le titre de la fenêtre.
- Pour publier une version : onglet **Actions** > « Publier une version » > **Run workflow**. Ça crée la release `vX.Y.Z` avec l'installeur, que l'appli propose ensuite comme mise à jour.

## Licence

Code distribué sous licence [MIT](LICENSE) : libre de le réutiliser, à condition de garder la mention de l'auteur.
