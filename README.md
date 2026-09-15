# LabTesting

Tests de plugins LabAPI dans un vrai serveur SCP:SL, pilotés par un runner autonome.
Le harnais tourne dans le jeu en **net48** ; `labtest` tourne en **.NET 10** sur
Windows et Linux x64.

Un seul `PackageReference` dans le projet de tests net48 :

```xml
<PackageReference Include="LabTesting" Version="1.0.0" PrivateAssets="all" />
```

```sh
dotnet build tests/MonPlugin.Tests -c Release -t:LabTesting
```

La restauration NuGet installe le harnais, Harmony, le runner et les cibles
MSBuild à la même version. Les archives autonomes restent disponibles pour une
installation sans NuGet : `.labtesting/runner/labtest run --config examples/labtesting.json`.

- [Intégration depuis un dépôt vierge](docs/integration.md)
- [Configuration, isolation et protocole](docs/runner.md)
- [GitHub Actions et dépendances privées](docs/github-actions.md)
- [Distributions et compatibilité](docs/releases.md)
- [État des validations et limites](docs/validation.md)
- [Plugin minimal](examples/SamplePlugin/Plugin.cs) et [tests](examples/SamplePlugin.Tests/CounterTests.cs)

## Depuis les sources

Installer le SDK .NET 10 et le serveur dédié Steam 996560, puis :

```powershell
$env:SL_REFERENCES = 'C:/chemin/serveur/SCPSL_Data/Managed'
$env:EXILED_REFERENCES = $env:SL_REFERENCES
dotnet build src/LabTesting -c Release
dotnet run --project src/LabTesting.Runner -c Release -- --selftest
dotnet run --project src/LabTesting.Runner -c Release -- run --config examples/framework.json --server C:/chemin/serveur
```

Les tests internes nécessitent `frameworkTests: true`. Une suite externe vide
échoue même si les tests internes sont activés. Les rapports restent dans
`TestResults/<identifiant-unique>/`, y compris après une erreur.

**État** : compilation, archives autonomes et exécutions réelles Windows et Ubuntu
24.04 (WSL2) validées : 4 tests du plugin d'exemple et 12 tests internes sur chaque
système. Le package NuGet est validé sous Windows depuis un cache vierge
(restauration, compilation, découverte et 4/4 tests réels). L'exécution GitHub
Actions reste à confirmer ; voir le relevé détaillé.

## Licence

[MIT](LICENSE). Les composants tiers redistribués sont listés dans
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
