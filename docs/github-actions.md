# GitHub Actions

## Template NuGet

[nuget-tests.yml](../templates/github/nuget-tests.yml) est le template le plus
simple : la version de LabTesting vit uniquement dans le `PackageReference` du
projet de tests, il n'y a ni archive à télécharger ni SHA-256 à tenir à jour.
Copier le fichier en `.github/workflows/tests.yml` et renseigner `TEST_PROJECT`.
Le job installe les prérequis natifs et le serveur 996560, puis appelle
`dotnet build -t:LabTesting` avec les propriétés `LabTestingServer`,
`LabTestingReports`, `LabTestingWork` et `LabTestingTimeout`.

## Template public (archive autonome)

Copier [labtesting.yml](../templates/github/labtesting.yml) et
[labtesting-reusable.yml](../.github/workflows/labtesting-reusable.yml) dans
`.github/workflows/` du dépôt consommateur. Le premier déclenche la suite sur
push, pull_request et workflow_dispatch. Le second expose `workflow_call` :
checkout, .NET 10, prérequis natifs, SteamCMD 996560, distribution LabTesting,
compilation, exécution bornée et publication des résultats.

Remplacer :

| Entrée | Valeur |
|---|---|
| release-repository | propriétaire/dépôt public qui publie LabTesting |
| release-version | version exacte sans v, par exemple 1.0.0 |
| archive-sha256 | SHA-256 de l'archive Linux de cette version |
| config | chemin du JSON, défaut examples/labtesting.json |
| artifact-name | préfixe unique par appel parallèle du workflow, défaut labtesting |
| build-command | commande de compilation du plugin et de ses tests |
| dependency-build-command | commande optionnelle pour dépendances publiques ou privées |

Le template n'a aucun secret obligatoire pour une release et des dépendances
publiques. La release doit être publiée, pas en brouillon. Vérifier la somme de
l'archive évite de faire confiance à une modification ultérieure du tag.

`SL_REFERENCES` et `EXILED_REFERENCES` pointent vers les assemblies fraîchement
installées. Le build reçoit le harnais de la distribution via `LabTestingPath`.
Adapter les références propres au plugin dans sa commande de build. Fixer les
versions NuGet et les refs des dépendances ; SteamCMD installe la version publique
courante du jeu, dont la version réelle apparaît dans le rapport.

Pour plusieurs appels parallèles dans un même workflow, donner un artifact-name
distinct à chacun. Les suites partageant une même machine doivent aussi utiliser
des ports distincts dans leurs configurations.

Le runner a un timeout de 600 secondes ; l'étape et le job ont des limites
supérieures pour laisser au runner le temps de terminer et écrire ses rapports.
Les logs et JUnit sont téléversés avec `if: always()`, le Markdown est ajouté
au résumé GitHub, et un dernier nettoyage récupère les copies marquées.
Une extinction brutale de la VM ne peut pas garantir le téléversement des fichiers.

Le workflow réutilisable peut aussi être appelé depuis un dépôt central :
`YOUR-ORG/LabTesting/.github/workflows/labtesting-reusable.yml@COMMIT_SHA`.
Fixer ce commit et autoriser les reusable workflows dans les paramètres Actions.

## Dépendances privées

Utiliser [private-dependencies.yml](../templates/github/private-dependencies.yml).
Renseigner `dependency-repository`, `dependency-ref` (SHA complet) et
`dependency-build-command`. Déclarer ensuite les DLL résultantes dans le JSON,
par exemple `../.dependencies/source/src/RequiredPlugin/bin/Release/net48/RequiredPlugin.dll`
dans `plugins`. Une bibliothèque sans Plugin va dans `dependencies`.

Secret requis : **DEPENDENCY_TOKEN**, jeton avec lecture du dépôt privé concerné.
Le checkout ne conserve pas les credentials. Le secret est transmis explicitement
au workflow appelé et seulement à l'étape qui récupère la dépendance.
La commande de build n'en a pas besoin.

**PR de fork** : GitHub ne transmet pas les secrets ordinaires. La suite privée
échoue explicitement avec un message d'accès indisponible ; elle n'est pas déclarée
réussie ou remplacée par les tests internes. Ne pas utiliser `pull_request_target`
pour exécuter du code de fork avec les secrets. Après revue du code, un mainteneur
peut reprendre les changements sur une branche de confiance du dépôt principal.
Pour conserver une couverture sur les forks, ajouter une suite publique indépendante.

## Check obligatoire avant fusion

Exécuter une première PR pour faire apparaître le check, puis dans les règles de
branche / ruleset activer « Require status checks to pass » et sélectionner le
check de la suite (`integration / suite`, nom à confirmer dans la première PR).
Exiger ce check sur les branches protégées ; ne pas ajouter de
`continue-on-error`. Avec des dépendances privées, une PR de fork reste bloquée
jusqu'à l'exécution de confiance réussie.

## CI du framework

`runner.yml` compile et exécute les vérifications sans jeu sur Ubuntu et Windows.
`server-validation.yml` lance le même plugin d'exemple sur les deux systèmes
avec une installation SteamCMD réelle. Il est manuel et réutilisable.
`release.yml` prépare les archives versionnées et crée un **brouillon** sur tag ;
publier le brouillon après examen des résultats. `nuget-release.yml` construit le
package, le valide sur Windows et Ubuntu avec un serveur réel, puis le publie sur
NuGet.org et GitHub Packages quand la release est publiée. NuGet.org utilise le
trusted publishing OIDC : pas de clé d'API stockée, seulement le secret
**NUGET_USER** et une policy déclarée sur nuget.org. Son `workflow_dispatch` avec
`publish: false` fait tourner la validation seule.

Sources : [workflows réutilisables](https://docs.github.com/en/actions/reference/workflows-and-actions/reusing-workflow-configurations),
[artefacts](https://docs.github.com/en/actions/tutorials/store-and-share-data),
[secrets et forks](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository).
