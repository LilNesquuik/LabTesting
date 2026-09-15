# Distributions et compatibilité

## Package NuGet

C'est le mode d'installation recommandé : un seul `PackageReference` apporte le
harnais net48, Harmony, le runner portable .NET 10 et les cibles MSBuild.

```powershell
pwsh -File scripts/pack-nuget.ps1 -Version 1.0.0 -Managed C:/scpsl/SCPSL_Data/Managed
```

Le script compile le harnais, publie le runner en framework-dependent, lance
`--selftest`, écrit `manifest.json` puis appelle `dotnet pack`. Le contenu est une
liste autorisée fichier par fichier : jamais un glob sur un dossier `bin`.
`scripts/verify-nuget.ps1` contrôle ensuite le contenu de l'archive, et le
`.nupkg.sha256` est écrit à côté du package. Il refuse d'écraser un package
existant : choisir un nouveau `-Output`.

`scripts/test-nuget-consumer.ps1` valide le package comme un vrai consommateur :
cache NuGet vierge, restauration depuis un dossier local, compilation de
`examples/NuGetPlugin.Tests`, `-t:LabTestingList` puis `-t:LabTesting` avec
`-RunServer` sur un serveur réel. Aucun chemin vers les sources de LabTesting
n'est utilisé.

Le workflow `.github/workflows/nuget-release.yml` rejoue tout cela quand une
release est publiée, préversion comprise : packaging sous Windows, validation sur
`windows-latest` et `ubuntu-24.04` avec un serveur SteamCMD réel, puis publication
sur NuGet.org et GitHub Packages et ajout du `.nupkg` et de son SHA-256 à la
release. Secret requis : **NUGET_API_KEY**. Un `workflow_dispatch` avec
`publish: false` exécute la validation sans rien publier.

## Archives autonomes

```powershell
pwsh -File scripts/package.ps1 -Version 1.0.0 -Managed C:/scpsl/SCPSL_Data/Managed
```

Sous Ubuntu, utiliser le chemin du dossier Managed installé par SteamCMD.
Le script demande le réseau NuGet pour restaurer les runtimes .NET autonomes.
Il refuse d'écraser un dossier de distribution existant ; choisir un nouveau
`-Output` pour une nouvelle tentative.

Résultats : `LabTesting-<version>-linux-x64.zip`,
`LabTesting-<version>-win-x64.zip` et `SHA256SUMS`.

Chaque archive contient :

- `harness/LabTesting.dll` et `harness/0Harmony.dll`.
- `runner/` : exécutable et runtime .NET autonome de la plateforme.
- `tools/` : installation serveur, vérification et récupération du nettoyage.
- `manifest.json` : version, cible, dépendances, SHA-256 de chaque fichier du payload.
- Notices des composants tiers.

Le manifeste exclut sa propre somme ; `SHA256SUMS` protège les archives complètes.
`pwsh -File scripts/verify-release.ps1 -Directory .labtesting` vérifie les fichiers
après extraction. Sous Linux, appliquer `chmod +x .labtesting/runner/labtest`
après extraction du ZIP.

**Aucune assembly SCP:SL, LabAPI, Unity ou assembly publicisée du jeu n'est
embarquée.** Le packaging utilise une liste autorisée pour le harnais, jamais
un glob sur son dossier bin. Les références du jeu viennent du serveur installé.

Le workflow de release publie d'abord des artefacts puis crée un brouillon de
release pour un tag `v<version>`. Lancer la validation serveur Windows/Ubuntu
avant publication. Publier le brouillon déclenche ensuite `nuget-release.yml`.
Les releases de cette session n'ont pas été envoyées à GitHub.

## Matrice de compatibilité

| Composant | Référence de cette version |
|---|---|
| Runner | .NET 10, Windows/Linux x64 |
| Harnais et tests | net48, chargés par le Mono du serveur |
| SCP:SL validé localement | 14.2.7 Windows et Ubuntu 24.04 / WSL2 |
| LabAPI utilisé pour compiler et tester | 1.1.7 |
| Harmony | 2.3.6 |

La version minimale déclarée par le plugin est LabAPI 1.1.0, mais cela ne constitue
pas une validation de toutes les versions 1.1.x. Le harnais utilise des détails
internes du jeu via publicisation : recompiler et relancer la suite après chaque
mise à jour SCP:SL/LabAPI. Un échec sur une version nouvelle ne doit pas être
contourné en acceptant un verdict incomplet.

.NET 10 est une version LTS, voir la
[politique officielle .NET](https://dotnet.microsoft.com/en-us/platform/support/policy).
Les chemins d'isolation reposent sur la politique gamedir de
[PathManager LabAPI](https://github.com/northwood-studios/LabAPI/blob/master/LabApi/Loader/Features/Paths/PathManager.cs).
