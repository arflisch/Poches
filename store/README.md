# Publier Poches sur l'App Store

Tout ce qui pouvait être préparé en local l'est. Il reste les étapes qui se font avec ton compte dans App Store Connect (https://appstoreconnect.apple.com).

## Ce qui est prêt

| Élément | Où |
|---|---|
| Build signé pour l'App Store (version 1.0, build 1) | `artifacts/appstore/Poches.ipa` (non versionné, à reconstruire si besoin, voir plus bas) |
| Textes de la fiche en français, anglais et néerlandais | `store/app-store-listing.md` |
| Captures iPhone 6,9" (1320 × 2868), en français | `store/screenshots/iphone-6.9-fr/` |
| Captures iPad 13" (2064 × 2752), en français | `store/screenshots/ipad-13-fr/` |
| Capture pour la vérification de l'achat Poches Pro | `store/screenshots/review-pro-page.png` |
| Politique de confidentialité et page d'assistance (FR / EN / NL) | `docs/privacy.html`, `docs/support.html` |
| Manifeste de confidentialité Apple (aucun suivi, aucune donnée collectée) | `src/Poches/Platforms/iOS/Resources/PrivacyInfo.xcprivacy` |

Le build App Store ne contient **pas** le mode test de Poches Pro : il n'est compilé qu'en Debug ou avec `-p:ProTesting=true`.

## 1. Accords, banque et impôts (une seule fois, obligatoire pour vendre Poches Pro)

App Store Connect › **Accords, impôts et banques** :
1. Accepter l'**accord pour les apps payantes** (Paid Apps Agreement).
2. Renseigner le **compte bancaire** qui recevra les ventes.
3. Remplir les **formulaires fiscaux** (formulaire W-8BEN pour un résident hors États-Unis).

Tant que l'accord n'est pas « Actif », l'achat intégré ne peut pas être vendu.

## 2. Mettre en ligne la politique de confidentialité

Apple exige une URL publique. Le plus simple : activer GitHub Pages sur le dépôt.
GitHub › arflisch/Poches › Settings › Pages › Source : branche `main`, dossier `/docs`.
Les pages seront alors à :
- https://arflisch.github.io/Poches/privacy.html
- https://arflisch.github.io/Poches/support.html

## 3. Créer l'app

App Store Connect › **Apps** › **+** › Nouvelle app :
- Plateforme : iOS
- Nom : `Poches – Budget et épargne` (si le nom est déjà pris, essayer `Poches : budget et épargne`)
- Langue principale : Français
- Identifiant de bundle : `com.arflisch.poches`
- SKU : `poches-ios`
- Accès : complet

## 4. Créer l'achat intégré Poches Pro

Dans l'app › **Monétisation › Achats intégrés** › **+** :
- Type : **Non consommable**
- Nom de référence : `Poches Pro`
- Identifiant de produit : `com.arflisch.poches.pro` (exactement, il est dans le code)
- Prix, noms et descriptions : voir `store/app-store-listing.md`
- Capture pour la vérification : `store/screenshots/review-pro-page.png`

Le premier achat intégré doit être soumis **en même temps** que la version de l'app (case à cocher sur la page de la version).

## 5. Remplir la fiche

- **Informations sur l'app** : catégorie Finances (secondaire : Productivité), URL de confidentialité.
- **Prix et disponibilité** : gratuite, tous les pays.
- **Confidentialité de l'app** : « Nous ne collectons aucune donnée de cette app ».
- **Classification par âge** : répondre « Aucun » / « Non » partout → 4+.
- **Version 1.0** : textes et mots-clés (`app-store-listing.md`), captures iPhone 6,9" et iPad 13", URL d'assistance, copyright, notes pour la vérification (déjà rédigées en anglais), connexion requise : non.
- Ajouter les localisations **anglais (Royaume-Uni)** et **néerlandais** avec leurs textes.

## 6. Envoyer le build

Avec l'app **Transporter** (gratuite sur le Mac App Store) : connecte-toi, glisse `artifacts/appstore/Poches.ipa`, puis **Livrer**. Le build apparaît dans App Store Connect après 10 à 30 minutes de traitement.

Question sur le chiffrement, posée pour chaque build (« Conformité à l'exportation ») :
- Poches chiffre les sauvegardes (AES-256-GCM, PBKDF2) avec les fonctions cryptographiques **d'iOS**, que .NET utilise sur iPhone.
- Réponse correspondante : l'app n'utilise que le chiffrement fourni par le système d'Apple → choisir **« Aucun des algorithmes mentionnés ci-dessus »**. Aucun document n'est alors demandé.
- C'est ta déclaration : si tu es d'accord avec cette lecture, on peut ajouter `ITSAppUsesNonExemptEncryption = false` dans l'Info.plist pour ne plus avoir la question.

## 7. Tester avant de soumettre (conseillé)

Dans **TestFlight**, ajoute-toi comme testeur interne et installe le build sur ton iPhone. Vérifie :
- que l'achat de Poches Pro fonctionne (en TestFlight, il est simulé et gratuit) ;
- que « Restaurer mes achats » le retrouve.

## 8. Soumettre

Sur la page de la version 1.0 : choisir le build, cocher l'achat intégré Poches Pro, puis **Ajouter pour vérification** › **Soumettre**. La vérification prend en général 1 à 3 jours.

## Reconstruire le build App Store

À chaque nouvelle version, augmenter `ApplicationVersion` (numéro de build) dans `src/Poches/Poches.csproj`, et `ApplicationDisplayVersion` pour une nouvelle version visible (1.1…). Puis :

```bash
dotnet publish src/Poches -f net10.0-ios -c Release -r ios-arm64 -p:ArchiveOnBuild=true -p:ValidateXcodeVersion=false
```

```bash
xcodebuild -exportArchive -archivePath "$(ls -td ~/Library/Developer/Xcode/Archives/*/Poches*.xcarchive | head -1)" -exportOptionsPlist store/ExportOptions.plist -exportPath artifacts/appstore -allowProvisioningUpdates
```

Xcode signe alors l'archive pour l'App Store, avec le certificat de distribution géré par Apple.

`-p:ValidateXcodeVersion=false` est nécessaire tant que le SDK .NET pour iOS installé (26.5) ne reconnaît pas officiellement Xcode 27. Dès qu'une mise à jour le prend en charge (`dotnet workload update`), on peut l'enlever.

## Plus tard

- **Mac** : la version Mac Catalyst peut être publiée séparément sur le Mac App Store. Elle a besoin du même correctif de scènes qu'iOS.
- **Android** : un compte Google Play Console (25 $, une fois) et un fichier .aab signé avec ta clé d'upload.
