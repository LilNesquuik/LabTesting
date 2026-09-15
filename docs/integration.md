# Intégrer un plugin

## 1. Prérequis et installation locale

Installer le SDK .NET 10 pour compiler et exécuter le runner. Le serveur utilise
son propre Mono pour le harnais net48. Le package NuGet livre un runner portable
qui s'appuie sur ce SDK ; les archives autonomes embarquent leur propre runtime.

**Windows** : installer le serveur « SCP Secret Laboratory Dedicated Server » dans
Steam, ou extraire SteamCMD puis exécuter :

```powershell
./steamcmd.exe +force_install_dir C:/scpsl +login anonymous +app_update 996560 validate +quit
```

Installer les prérequis natifs du serveur (notamment Visual C++ Redistributable).
Les chemins avec espaces sont acceptés.

**Ubuntu x64** : le template utilise Ubuntu 24.04, validé localement via WSL2 avec
SCP:SL 14.2.7. Installer les dépendances puis
utiliser le script livré :

```bash
sudo apt-get update
sudo apt-get install -y lib32gcc-s1 lib32stdc++6 libatomic1 libgomp1 libglu1-mesa libxcursor1 libxrandr2 libxi6 libasound2t64 util-linux
bash scripts/install-server.sh "$PWD/.server" "$HOME/steamcmd-labtesting"
```

`install-server.sh` est livré dans le dépôt et dans les archives autonomes
(`.labtesting/tools/`). Après extraction d'une archive, appliquer aussi
`chmod +x .labtesting/runner/labtest` ; le runner du package NuGet n'en a pas besoin.

Le téléchargement anonyme SteamCMD utilise l'application **996560**.
Le serveur et sa copie isolée occupent chacun plusieurs Go : prévoir de la place
pour chaque worker parallèle. Consulter les prérequis actualisés du
[serveur dédié](https://techwiki.scpslgame.com/books/server-guides/page/1-how-to-create-a-dedicated-server).

## 2. Démarrer depuis un dépôt vierge

1. Créer le dépôt de plugin.
2. Copier les dossiers `examples/SamplePlugin` et `examples/SamplePlugin.Tests`,
   ainsi que `examples/labtesting.json` et `global.json`.
3. Ajouter le package au projet de tests net48, avec une **version publiée fixée** :

   ```xml
   <PackageReference Include="LabTesting" Version="1.0.0" PrivateAssets="all" />
   ```

4. Installer le serveur dans `.server/`, ou passer son chemin via
   `-p:LabTestingServer=`.
5. Compiler et exécuter :

   ```bash
   export SL_REFERENCES="$PWD/.server/SCPSL_Data/Managed"
   export EXILED_REFERENCES="$SL_REFERENCES"
   dotnet build tests/MonPlugin.Tests -c Release -t:LabTestingList
   dotnet build tests/MonPlugin.Tests -c Release -t:LabTesting
   ```

Sous PowerShell, les mêmes commandes avec `$env:SL_REFERENCES`. Le build normal
restaure et compile sans lancer le serveur ; seules les cibles `LabTesting` et
`LabTestingList` le démarrent. [examples/NuGetPlugin.Tests](../examples/NuGetPlugin.Tests)
est un consommateur complet du package : csproj, `labtesting.json` et tests partagés
avec `SamplePlugin.Tests`. Les propriétés disponibles sont listées dans
[packaging/README.md](../packaging/README.md).

### Sans NuGet : archive autonome

Télécharger une **release publiée et fixée**, vérifier son archive avec
`SHA256SUMS`, puis l'extraire dans `.labtesting/` à la racine. Ne pas utiliser un
lien `latest`. Le harnais est alors passé au build à la main et le runner est
invoqué directement :

```bash
dotnet build examples/SamplePlugin.Tests -c Release -p:LabTestingPath="$PWD/.labtesting/harness/LabTesting.dll"
.labtesting/runner/labtest list --config examples/labtesting.json
.labtesting/runner/labtest run --config examples/labtesting.json --fail-on-skipped
```

`SamplePlugin.Tests` est une bibliothèque net48, pas un projet `dotnet test`.
Elle référence LabTesting et le plugin. Les attributs sont dans `LabTesting`,
pas dans xUnit. Adapter noms, chemins et assertions à votre plugin.

Pour utiliser les sources sans release : compiler `src/LabTesting`, puis pointer
`harness` et la dépendance Harmony vers `src/LabTesting/bin/Release/net48`.
Le projet de tests utilise ce chemin par défaut quand `LabTestingPath` est absent.

## 3. Écrire les tests

Le [projet complet](../examples/SamplePlugin.Tests/CounterTests.cs) contient quatre
tests exécutables : synchrone, invariant asynchrone, joueur factice et événement.

```csharp
[Fact]
public void Ajouter_un_point()
{
    CounterPlugin.Increment();
    Assert.Equal(1, CounterPlugin.Count);
}

[Fact]
public async Task Un_joueur_est_pret()
{
    var player = await World.Spawn(RoleTypeId.ClassD, "Test");
    Assert.True(player.Hub.IsDummy);
    await Expect.Eventually(() => player.Hub.IsDummy, 60.Ticks());
    await Swallowed.AssertNone();
}
```

Les attentes utilisent les ticks du jeu. Éviter `Thread.Sleep` et les boucles qui
bloquent le thread Unity. `Expect.Tick`, `Frame`, `Eventually`, `Always` et
`Never` permettent de laisser avancer le jeu. `[Timeout(600)]` borne le corps
du test en ticks ; le timeout du runner borne toute l'exécution, y compris
démarrage et fixtures bloquées.

Souscrire **avant** l'action qui produit l'événement :

```csharp
var changed = Expect.Event<PlayerChangedRoleEventArgs>(120.Ticks());
await World.Spawn(RoleTypeId.ClassD, "Event");
Assert.NotNull(await changed);
```

Importer `LabApi.Events.Arguments.PlayerEvents`. Utiliser
`Expect.Events<T1,T2>()` dans un `using` pour observer une séquence.
Certains événements du catalogue ne sont pas produits naturellement par le jeu :
voir `EventCatalog.NeverRaisedByGame`.

Les assertions, exceptions normales, exceptions avalées par LabAPI et erreurs
de teardown influencent toutes le résultat. Les détails et piles d'appel
sont conservés dans le JSONL et dans le JUnit.

## 4. Nettoyer les registres statiques

Le nettoyage du monde ne connaît pas les dictionnaires de votre plugin.
Implémenter `IAsyncLifetime`, sauvegarder l'état avant le test, restaurer après,
désabonner les événements et annuler les tâches démarrées :

```csharp
public Task InitializeAsync()
{
    previous = CounterPlugin.Count;
    CounterPlugin.Count = 0;
    return Task.CompletedTask;
}
public Task DisposeAsync()
{
    CounterPlugin.Count = previous;
    return Task.CompletedTask;
}
```

Les fixtures sont recréées pour chaque cas. `DisposeAsync` est tenté même après
un échec de setup. Ses erreurs font échouer le test.
Les tests d'une suite s'exécutent séquentiellement ; une collection est un groupe
de sélection, pas une fixture partagée.

`Dirty.Dummies` nettoie les joueurs factices ; `Dirty.Round` redémarre la partie.
**`Dirty.Process` reste actuellement une dégradation vers Round**, pas un nouveau
processus pour chaque test. Pour isoler un registre impossible à restaurer,
lancer des suites séparées avec `--test` et un processus serveur par suite.

## 5. Dépendances et données

Déclarer dans `plugins` le plugin testé **et chaque plugin requis**. Le harnais
vérifie qu'ils sont activés. Les bibliothèques sans classe Plugin vont dans
`dependencies`, par exemple `0Harmony.dll`. Les tests vont dans `tests` et
sont chargés explicitement après le chargement LabAPI.

Une DLL ne doit figurer qu'une fois, même si plusieurs plugins en dépendent.
Ne pas copier tout le dossier `bin/` : il peut contenir des assemblies du jeu,
Unity ou LabAPI. Elles sont fournies par le serveur.

```json
{
  "files": [
    { "source": "fixtures/config.yml", "root": "labapiConfig", "target": "MonPlugin/config.yml" },
    { "source": "fixtures/scenario.json", "root": "data", "target": "scenario.json" }
  ],
  "serverSettings": { "max_players": "8" }
}
```

Adapter le sous-dossier de configuration au nom réellement utilisé par le plugin.
Les données sont accessibles via `Path.Combine(Environment.CurrentDirectory,
"test-data", "scenario.json")`. Une configuration qui référence un service réseau
ou un chemin absolu externe reste sous la responsabilité du plugin.

## 6. Dépannage

| Symptôme | Vérification |
|---|---|
| DLL manquante / ReflectionTypeLoadException | Lire les LoaderExceptions dans JSONL/JUnit. Ajouter la DLL à dependencies, ou le plugin requis à plugins. Vérifier les versions ; ne pas copier les assemblies du jeu depuis un autre serveur. |
| Aucun test découvert | Utiliser list, vérifier tests, les attributs LabTesting.Fact/Theory, les filtres exacts et la cible net48. Les tests internes ne compensent pas une assembly externe vide. |
| Port occupé | Choisir un autre --port. Le verrou LabTesting et les binds UDP/TCP refusent les collisions. |
| Serveur bloqué | Lire stdout.log/stderr.log, vérifier l'installation native et utiliser --keep pour inspecter la copie. Le timeout produit un échec avec rapports conservés. |
| Verdict absent | Le harnais n'a pas pu s'armer : rechercher erreur de chargement, dépendance, version LabAPI ou refus de sentinelle. |
| Résumé présent mais workflow rouge | Vérifier sortie serveur, résultats manquants, doublons, erreurs de teardown et fail-on-skipped. Un résumé seul n'autorise pas le succès. |
| Permission denied sous Linux | chmod +x sur le runner extrait ; vérifier les droits de l'exécutable du serveur source. Le runner conserve les modes Unix lors de la copie. |
| Nettoyage refusé sous Windows | Un processus ou un antivirus garde un fichier ouvert. Les rapports indiquent le dossier conservé ; vérifier les processus avant de supprimer uniquement la copie labtest concernée. |
| Le plugin lit encore un fichier personnel | Vérifier qu'il utilise les chemins LabAPI standard. Les chemins absolus codés dans le plugin ne peuvent pas être redirigés automatiquement. |
