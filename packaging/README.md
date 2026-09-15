# LabTesting

Un seul package pour tester un plugin LabAPI dans un vrai serveur SCP:SL sous
Windows et Linux. Il contient le harnais net48, Harmony 2.3.6, le runner portable
.NET 10 et les cibles MSBuild. Installer le SDK .NET 10 et le serveur dédié
SteamCMD 996560 séparément.

Dans le projet de tests net48 :

```xml
<PackageReference Include="LabTesting" Version="0.1.0" PrivateAssets="all" />
```

Utiliser la version publiée choisie. La restauration NuGet installe tout le
framework et garde harnais/runner à la même version. Aucun EXE à copier,
aucun dotnet-tools.json à maintenir.

Placer labtesting.json à la racine du dépôt (ou près du projet de tests) :

```json
{
  "server": ".server",
  "plugins": ["src/MonPlugin/bin/Release/net48/MonPlugin.dll"],
  "dependencies": [],
  "reports": "TestResults",
  "work": ".labtest-work",
  "port": 7799
}
```

Le package fournit automatiquement le harnais, Harmony et l'assembly du projet
de tests. Ne pas les redéclarer dans le JSON. Les plugins requis et leurs autres
dépendances restent explicites. Les chemins JSON sont relatifs au fichier JSON.

```sh
dotnet build tests/MonPlugin.Tests -c Release
dotnet build tests/MonPlugin.Tests -c Release -t:LabTestingList
dotnet build tests/MonPlugin.Tests -c Release -t:LabTesting
```

Le build normal restaure et compile ; les tests serveur ne se lancent que sur
demande. Pour les lancer après chaque build, ajouter
`<LabTestingRunOnBuild>true</LabTestingRunOnBuild>` au projet de tests.

Propriétés optionnelles : LabTestingConfig (relatif au projet), LabTestingServer,
LabTestingReports, LabTestingWork, LabTestingPort, LabTestingTimeout.
LabTestingFailOnSkipped vaut true par défaut. Les paramètres de chemin CLI/MSBuild
sont relatifs au projet de tests ; privilégier les chemins absolus en CI.

Les assertions et attributs sont dans le namespace LabTesting. Les fixtures
peuvent implémenter IAsyncLifetime pour restaurer leur état statique.
Les rapports JSONL, JUnit, Markdown et les logs restent disponibles après un échec.
Une suite vide ou un verdict incomplet fait échouer le build.

Le package n'inclut aucune assembly du jeu, Unity ou LabAPI. Ajouter les références
du plugin et du serveur correspondant aux types utilisés par les tests.
PrivateAssets="all" évite de propager ce framework de tests dans vos packages.
