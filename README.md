# Poches

Application mobile de budget par « poches » (vacances, épargne, investissement…), en **.NET MAUI 10** pour **iPhone et Android**.

| Accueil | Détail d'une poche | Mode sombre | Premier lancement |
|---|---|---|---|
| ![](docs/01-main-light.png) | ![](docs/03-detail-light.png) | ![](docs/01-main-dark.png) | ![](docs/00-empty-light.png) |

## Fonctionnalités

- **Accueil** : solde total (centimes en plus petit), variation du mois, graphique en anneau de la répartition, liste des poches avec montant, part du total ou progression vers l'objectif.
- **Poches** : nom, emoji, couleur, objectif d'épargne facultatif, montant de départ.
- **Mouvements** : ajout, retrait, **transfert entre poches**, note et date, avec un pavé numérique intégré. Un retrait ne peut pas dépasser le solde.
- **Détail d'une poche** : courbe d'évolution du solde, barre de progression vers l'objectif, historique groupé par mois (appui sur une ligne pour la supprimer).
- **Mode discret** (icône œil) : masque tous les montants, utile en public.
- **Réglages** (bouton en haut à droite) : devise d'affichage (€, CHF, $, £), mode discret, sauvegarde, restauration et « Tout effacer ».
- **Sauvegarde / restauration** : exporte toutes les poches dans un fichier JSON via la feuille de partage (iCloud Drive, mail, AirDrop…) et le restaure sur n'importe quel téléphone, iPhone ou Android. Un rappel apparaît sur l'accueil si la dernière sauvegarde date de plus de 30 jours.
- **Thème clair / sombre** automatique.
- **Exemples** : sur l'écran vide, « Explorer avec des exemples » crée 5 poches de démonstration.

## Persistance

**SQLite** local via `sqlite-net-pcl`, dans le dossier privé de l'app. Aucune connexion réseau n'est demandée (pas de permission INTERNET sur Android).

- Les montants sont stockés en **centimes (`long`)** pour éviter les erreurs d'arrondi.
- Le solde d'une poche n'est **jamais stocké** : c'est toujours la somme de ses mouvements, il ne peut donc pas se désynchroniser.
- Un transfert crée deux mouvements liés, supprimés ensemble.
- La sauvegarde est un fichier JSON versionné (`format: "poches-backup"`), indépendant du schéma SQLite. La restauration est atomique : un fichier incohérent est refusé sans toucher aux données.

## Architecture

```
Poches.slnx
├── src/Poches.Core          net10.0 — modèles, BudgetStore (SQLite), formatage/parsing des montants
├── src/Poches               app .NET MAUI (MVVM avec CommunityToolkit.Mvvm)
│   ├── Views/               pages XAML (bindings compilés)
│   ├── ViewModels/          un ViewModel par page
│   ├── Controls/            dessin vectoriel maison : anneau, courbe, barre de progression, icônes
│   ├── Services/            réglages, dialogues, palette, routes
│   └── Resources/Styles/    couleurs et styles (clair/sombre)
└── tests/Poches.Core.Tests  tests xUnit de la logique métier (SQLite réel en fichier temporaire)
```

Les graphiques et icônes sont dessinés avec `Microsoft.Maui.Graphics` : aucune bibliothèque de graphiques tierce, rendu identique sur iOS et Android.

## Lancer l'app

Prérequis communs : SDK .NET 10 et workload MAUI (`dotnet workload install maui`).

### iPhone (simulateur ou appareil)

1. Dans Xcode, installer la plateforme iOS : **Xcode › Settings › Components › iOS**.
2. Faire pointer les outils en ligne de commande vers Xcode (une seule fois) :
   ```bash
   sudo xcode-select -s /Applications/Xcode.app
   ```
3. Lancer depuis Rider (cible `net10.0-ios`) ou :
   ```bash
   dotnet build src/Poches -t:Run -f net10.0-ios
   ```
   Sur un vrai iPhone, un compte Apple Developer est nécessaire pour signer l'app.

### Android

1. Installer le SDK Android et un JDK 21 (le plus simple : via Android Studio, ou Rider › Settings › Environment › Android).
2. Démarrer un émulateur ou brancher un téléphone, puis lancer depuis Rider (cible `net10.0-android`) ou :
   ```bash
   dotnet build src/Poches -t:Run -f net10.0-android
   ```

### Sur le Mac (pour tester rapidement l'interface)

```bash
dotnet build src/Poches -t:Run -f net10.0-maccatalyst
```

### Tests

```bash
dotnet test tests/Poches.Core.Tests
```

## Idées pour la suite

- Virements récurrents automatiques (ex. +200 € chaque mois sur « Épargne »).
- Verrouillage par Face ID / empreinte.
- Synchronisation automatique entre appareils (CloudKit, nécessite un compte Apple Developer payant).
- Suivi de la valeur des investissements (plus-values) en plus des versements.
- Statistiques mensuelles (entrées/sorties par mois) et date cible pour les objectifs.
- Widget d'écran d'accueil avec le solde total.
