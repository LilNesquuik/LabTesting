# Relevé de validation — 15 septembre 2026

## Compilation et distributions

- Harnais net48 et plugin d'exemple compilés sous Windows avec le SDK .NET 10.
- Runner net10.0 compilé et publié en autonome pour win-x64 et linux-x64.
- Archives finales générées dans `dist/final/` (ignoré par Git).
- Les deux manifestes vérifiés : 198 fichiers par plateforme, SHA-256 conformes.
- Assemblies du jeu exclues des distributions ; harnais et Harmony copiés par
  liste autorisée. Le serveur utilisé est SCP:SL 14.2.7, LabAPI 1.1.7, Harmony 2.3.6.
- Les sources net48 n'ont pas été recompilées dans Ubuntu pendant cette session :
  les mêmes DLL compilées sous Windows ont été exécutées dans les deux jeux.

## Package NuGet

`LabTesting.1.0.0.nupkg` construit par `scripts/pack-nuget.ps1` : 895 521 octets,
16 entrées, contenu vérifié fichier par fichier par `scripts/verify-nuget.ps1`,
SHA-256 écrit à côté. Payload : harnais net48 en `lib/net48` et `tools/harness`,
Harmony 2.3.6, runner `labtest.dll` framework-dependent .NET 10, cibles MSBuild,
`cleanup.py`, `manifest.json` et notices tierces. Aucune assembly du jeu, Unity
ou LabAPI.

Validation consommateur réelle sur les deux systèmes via
`scripts/test-nuget-consumer.ps1`, avec un cache NuGet vierge à chaque lancement
et sans aucun chemin vers les sources de LabTesting :

| Étape | Windows | Ubuntu 24.04, WSL2 |
|---|---|---|
| Restauration du package depuis un dossier local | succès | succès |
| Compilation de `examples/NuGetPlugin.Tests` en net48 | succès | succès |
| `-t:LabTestingSelfTest` | succès | succès |
| `-t:LabTestingList` | 4 découverts, sortie 0 | 4 découverts, sortie 0 |
| `-t:LabTesting` sur serveur réel 14.2.7 | **4/4 réussis** | **4/4 réussis** |

Rapports : `TestResults/nuget/20260915-160006-…` (Windows) et
`20260915-160309-…` (Ubuntu). Les deux rapportent LabAPI 1.1.7 et Harmony 2.3.6,
identiques au flux par archive. Sous Linux, le SDK .NET 10 et PowerShell 7.4.6
portables ont été installés dans `~/.cache/`, sans droits root.

`verify-nuget.ps1` a effectivement bloqué un package incomplet pendant cette
session : `tools/cleanup.py`, requis par la cible `LabTestingCleanup`, manquait
dans un package construit avant l'ajout de ce fichier. Le contrôle a échoué avant
toute exécution de tests.

**Non validé** : la matrice `nuget-release.yml` sur des runners GitHub réels, faute
de dépôt distant.

## Exécutions réelles locales

| Validation | Windows | Ubuntu 24.04.4, WSL2 x64 |
|---|---|---|
| Plugin externe + assembly de tests explicite | 4/4, sortie 0 | 4/4, sortie 0 |
| Tests internes du framework | 12/12, sortie 0 | 12/12, sortie 0 |
| Runner autonome, tests sans jeu | succès | succès |
| Arrêt d'un processus et de son enfant (selftest) | succès | succès |
| Mode list sur le plugin d'exemple | 4 tests, aucun corps invoqué, sortie 0 | non exécuté séparément |
| Timeout réel de 1 seconde | refus, rapports partiels conservés | non exécuté séparément |
| Collision avec un port UDP occupé | refus avant lancement | non exécuté séparément |
| Suite volontairement fautive | 7 résultats conformes | 7 résultats conformes |

Les tests du plugin couvrent une assertion synchrone, la restauration d'un état
statique par IAsyncLifetime, un invariant asynchrone, un joueur factice et un
événement LabAPI. Aucun plugin personnel n'est déployé dans les copies.

Rapports locaux conservés sous `TestResults/` :

| Suite | Identifiant de lancement |
|---|---|
| Framework Windows | 20260915-033237-0894885299e1477da91469d3755fe22f |
| Plugin Windows | 20260915-035229-7353965d028f4bed9868956c580c150a |
| List Windows corrigé | 20260915-034949-4456b37d696a4c479f800be98a68ca54 |
| Plugin Ubuntu | 20260915-143101-95377ef1e00047729a459f7b75953d82 |
| Framework Ubuntu | 20260915-144025-9416a24c9b204e19af98abb5d7e48a35 |

Le premier essai de list avait révélé un arrêt trop précoce pendant FastMenu.
Le harnais attend maintenant le chargement du lobby avant de quitter en mode list.
Le nettoyage tolère également les verrous transitoires des DLL après la mort du
processus et garde son marqueur de récupération jusqu'à la fin.

La suite `tests/FailureSuite` a été compilée puis exécutée sur les deux systèmes.
Elle vérifie une assertion fausse, une exception du corps, un skip, une exception
avalée, un setup qui lève, un DisposeAsync qui lève et un Dispose qui lève.
Résultat attendu et observé : **0 succès, 4 échecs, 1 ignoré, 2 erreurs**.
Le code de sortie **2** a été confirmé explicitement sous Ubuntu avec l'archive
finale (lancement 20260915-145227-2f117421cf7b4e71913ba4272d91a061).
Le script `scripts/check-failure-suite.ps1` contrôle chaque identifiant et son
outcome ; la CI valide aussi le code de sortie non nul attendu.

## Vérifications sans jeu

`labtest --selftest` vérifie le parseur JSON, les échappements, les compteurs,
le plan obligatoire, les résultats manquants ou dupliqués, le résumé final unique,
le JSON tronqué, le plan vide, le harnessError, fail-on-skipped, list, les chemins
de traversée refusés et les erreurs JUnit d'une exécution partielle.
Il lance aussi un processus avec un enfant pour vérifier leur arrêt réel.
Les archives finales passent cette commande sous Windows et Ubuntu.

Les six workflows/templates YAML ont été parsés avec PyYAML 6.0.1. Le script
`scripts/check-workflows.py` vérifie aussi les entrées des workflow_call locaux.
Cette vérification syntaxique ne remplace pas une exécution GitHub Actions.

## GitHub Actions et publication

**Non exécutés sur GitHub pendant cette session.** Le dépôt local n'a aucun remote
configuré et aucun commit. Les workflows réutilisables, la validation réelle
Windows/Ubuntu, la création d'un brouillon de release et la publication NuGet sont
livrés, mais aucune release n'a été publiée, aucun package n'a été envoyé à
NuGet.org et aucun check de branche n'a été configuré. Les huit fichiers YAML sont
seulement parsés localement par `scripts/check-workflows.py`.

Le critère « dépôt vierge obtenant un résultat sur un runner Ubuntu GitHub »
reste donc à confirmer de bout en bout. Étapes restantes :

1. Fournir le dépôt GitHub cible et y déposer les sources/workflows.
2. Déclencher Real server validation (Windows et Ubuntu).
3. Lancer `nuget-release.yml` en `workflow_dispatch` avec `publish: false` pour
   valider le package sur les deux systèmes sans rien publier.
4. Déclarer la policy de trusted publishing sur nuget.org et le secret NUGET_USER.
5. Créer un tag de release, examiner puis publier le brouillon généré ; la
   publication déclenche la publication du package.
6. Copier `nuget-tests.yml` dans un dépôt consommateur, renseigner le projet de
   tests et la version du package, puis vérifier une PR et rendre le check obligatoire.
7. Tester l'exemple privé avec un vrai dépôt dépendant et une PR de fork.

## Limites connues

- SIGKILL/panne de machine ne permet pas un finally ni un téléversement garanti ;
  utiliser le nettoyage de secours et des runners CI éphémères.
- Les plugins qui écrivent sur des chemins absolus ou lancent des démons détachés
  exigent une isolation système complémentaire.
- Dirty.Process ne recrée pas encore un serveur par test ; séparer ces tests en
  invocations distinctes.
- La référence AccessSubclasses 53 + 12 fournie dans la demande n'a pas été rejouée :
  les tests externes de cette validation sont ceux du nouveau SamplePlugin.
