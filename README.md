# Optimizing Software

Application Windows qui trouve ce qui ralentit un PC et aide à le nettoyer, sans jamais rien casser.

## Où en est le projet

| Étape | Contenu | État |
| --- | --- | --- |
| 1 | Cadrage | Fait |
| 2 | Socle + Diagnostic performances | En cours |
| 3 | Analyse des processus et programmes au démarrage | À venir |
| 4 | Analyse des fichiers et applications, installeur | À venir |

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
