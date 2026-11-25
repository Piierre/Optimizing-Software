# Optimizing Software

Application Windows qui trouve ce qui ralentit un PC et aide à le nettoyer, sans jamais rien casser.

## Où en est le projet

Objectif (recentré le 25 novembre 2026) : garder le PC propre dans la durée en retirant ce qui s'accumule, pour tous les jeux et applications.

| Étape | Contenu | État |
| --- | --- | --- |
| 1 | Cadrage | Fait |
| 2 | Diagnostic performances et enregistrement en jeu | Fait |
| 3 | Corbeille interne + fichiers temporaires et caches | Fait |
| 4 | Vieux téléchargements, gros fichiers, doublons, corbeille de Windows, dossiers système | Fait |
| 5 | Applications installées et restes de désinstallation | En cours |
| 6 | Programmes au démarrage et processus | À venir |
| 7 | Finitions et installeur | À venir |

## Nettoyage

L'onglet « Nettoyage » analyse au lancement ce qui peut être retiré, sans droits administrateur :

| Catégorie | Cochée par défaut |
| --- | --- |
| Fichiers temporaires (plus d'un jour) | Oui |
| Rapports de plantage et vidages mémoire | Oui |
| Caches de Chrome, Edge, Brave, Opera GX, Firefox | Oui |
| Cache de Discord, cache web de Steam | Oui |
| Caches de shaders de la carte graphique | Non : les jeux saccadent le temps de les reconstruire |

Rien n'est supprimé directement. Après confirmation, les fichiers sont déplacés dans une corbeille interne (`%LOCALAPPDATA%\OptimizingSoftware\Corbeille`) avec leur emplacement d'origine. Chaque nettoyage peut être restauré pendant 7 jours, puis il est supprimé définitivement. On peut aussi vider la corbeille pour libérer la place tout de suite.

Garde-fous : l'application ne touche qu'aux dossiers listés ci-dessus, ignore les liens symboliques et jonctions, ignore les fichiers en cours d'utilisation, et ne remplace jamais un fichier existant lors d'une restauration.

## Diagnostic performances

Au lancement, l'application prend une photo du PC (lecture seule, sans droits administrateur) et vérifie :

| Vérification | Ce qui est signalé |
| --- | --- |
| Plantages de la carte graphique | LiveKernelEvent 141/117 et redémarrages du pilote (4101) des 30 derniers jours |
| Fréquence des écrans | Écran sous sa fréquence max, ou écrans à fréquences différentes |
| Pilote graphique | Plus de 3 mois (à surveiller), plus de 6 mois (à corriger) |
| Planification GPU | Désactivée |
| Barrettes de RAM | Une seule barrette (single channel) ou barrettes dépareillées |
| Vitesse de la RAM | Sous sa vitesse annoncée ou à une vitesse par défaut (XMP/EXPO probablement inactif) |
| Mémoire utilisée | Plus de 80 % / 90 % |
| Disque de Windows | Disque dur au lieu d'un SSD, moins de 15 % / 10 % libres |
| Mode d'alimentation | Économie d'énergie, ou Utilisation normale pour jouer |
| Processeur | Moins de 8 threads |

## Fichiers personnels

L'onglet « Fichiers » propose trois analyses, lancées à la demande :

| Analyse | Où | Coché par défaut |
| --- | --- | --- |
| Vieux téléchargements | Dossier Téléchargements, fichiers non modifiés depuis 3 mois | Installateurs (.exe, .msi…) et images disque (.iso) |
| Gros fichiers | Dossiers personnels et autres disques, fichiers de plus de 500 Mo | Rien |
| Doublons | Mêmes dossiers, fichiers de plus de 1 Mo au contenu identique (SHA-256) | Les copies ; l'original est gardé |

Les jeux installés (Steam, Epic, Xbox, Riot…), `Program Files`, `AppData` et `Windows` ne sont jamais parcourus. L'appli refuse de retirer tous les exemplaires d'un même doublon. Les fichiers d'un autre disque vont dans un dossier `OptimizingSoftware.Corbeille` à la racine de ce disque, pour ne pas remplir le disque de Windows.

Avec les droits administrateur (bouton « Relancer en administrateur »), l'onglet Nettoyage ajoute `C:\Windows\Temp`, les téléchargements de Windows Update et les anciens journaux d'installation. La corbeille de Windows peut aussi être vidée, après une confirmation qui précise que c'est définitif.

## Applications

L'onglet « Applications » (bouton « Analyser ») a trois parties :

| Partie | Ce qu'on y fait | Coché par défaut |
| --- | --- | --- |
| Installées | Liste de « Applications installées » de Windows, triée par taille, avec recherche. « Désinstaller » lance le désinstallateur de l'application, puis cherche ce qu'elle a laissé | — |
| Dossiers restants | Sous-dossiers directs de Program Files, AppData (Local, LocalLow, Roaming) et ProgramData qui ne correspondent à aucune application installée et n'ont pas bougé depuis 2 mois | Dossiers vides, dossiers de programmes sans plus aucun .exe, dossiers de l'application qu'on vient de désinstaller |
| Entrées cassées | Applications encore listées alors que leur désinstallateur et leur dossier ont disparu | Toutes |

Les dossiers partagés ou sensibles ne sont jamais proposés (Microsoft, Windows, Common Files, pilotes, launchers de jeux et sauvegardes de jeux…). Les dossiers vont dans la corbeille interne comme les autres fichiers. Chaque entrée du registre est d'abord exportée dans un fichier `.reg` dans la corbeille ; si l'export échoue, l'entrée n'est pas supprimée. « Restaurer » réimporte ces fichiers. Les dossiers de Program Files et ProgramData et les entrées installées pour tout le PC demandent les droits administrateur.

Les applications du Microsoft Store ne sont pas encore listées.

## Tests

`tests/OptimizingSoftware.Core.Tests` teste les règles (analyse, corbeille, doublons, restes…) sur des dossiers temporaires. `tests/OptimizingSoftware.Windows.Tests` lance les vraies lectures Windows (WMI, registre, dossiers, corbeille de Windows, export et réimport d'une clé de test) sur la machine Windows de GitHub à chaque modification ; ces tests sont ignorés hors Windows.

## Enregistrement en jeu

Onglet « Enregistrement en jeu » : on démarre, on joue 2 à 10 minutes, on arrête. L'application mesure chaque seconde le processeur (total et cœur le plus chargé), la carte graphique, la RAM, le disque et la température de la carte graphique (NVIDIA), puis dit ce qui limite :

| Verdict | Règle (pendant au moins 30 % du temps en jeu) |
| --- | --- |
| Carte graphique | Utilisation ≥ 95 % |
| Processeur | ≥ 90 % (ou un cœur ≥ 95 %) alors que la carte graphique est sous 85 % |
| RAM | ≥ 90 % utilisée |
| Disque | Occupé ≥ 95 % |

Les moments au bureau ou dans les menus (processeur et carte graphique sous 50 %) sont ignorés. L'analyse donne ensuite des conseils d'upgrade adaptés à la configuration et signale une carte graphique au-dessus de 83 °C.

La température du processeur n'est pas encore mesurée : Windows ne la donne pas sans pilote spécial et droits administrateur.

## Organisation du code

| Projet | Rôle |
| --- | --- |
| `src/OptimizingSoftware.Core` | Modèles et règles de diagnostic. Aucun appel à Windows, donc testable partout. |
| `src/OptimizingSoftware.Windows` | Lecture des infos du PC : WMI, registre, journaux d'événements, API d'affichage et d'alimentation, compteurs de performance, NVML (NVIDIA). |
| `src/OptimizingSoftware.App` | Interface WPF (.NET 10, thème Fluent). |
| `tests/OptimizingSoftware.Core.Tests` | Tests des règles et de l'analyse en jeu, dont une photo du PC de Pierre qui doit reproduire le diagnostic fait à la main. |

## Lancer l'application

Prérequis : Windows 10 (1809 ou plus) ou Windows 11, et le [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet run --project src/OptimizingSoftware.App
```

Ou avec Visual Studio 2026 : ouvrir `OptimizingSoftware.slnx`, choisir `OptimizingSoftware.App` comme projet de démarrage, puis F5.

Sans rien installer : chaque build GitHub Actions publie un exécutable `OptimizingSoftware-win-x64` à télécharger dans l'onglet **Actions**.

## Tests

```powershell
dotnet test
```

## Règles de sécurité

L'application observe par défaut. Toute modification future passera par une confirmation explicite et pourra être annulée (point de restauration, corbeille interne, sauvegarde du registre). Aucune donnée ne quitte le PC.
