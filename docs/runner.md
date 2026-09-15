# Runner : configuration, isolation et résultats

## Interface

```text
labtest run|list --config labtesting.json
  --server DIR              installation source
  --server-executable FILE  natif explicite (relatif à server, ou absolu)
  --port N                  UDP/TCP, 1..65535
  --timeout N               secondes, 1..86400
  --reports DIR             parent des rapports persistants
  --work DIR                parent des copies temporaires
  --assembly NAME           filtre exact, répétable
  --collection NAME         filtre exact, répétable
  --test ID                 filtre exact, répétable
  --framework-tests         ajoute les tests internes
  --fail-on-skipped
  --keep                    conserve la copie après exécution
labtest --selftest
```

`--plugin LabTesting.dll` est conservé pour les suites internes lancées sans JSON.
`--force` a été supprimé : aucun fichier existant ne doit être écrasé.
Codes : **0** succès ; **1** assertion, exception de test ou skip refusé ;
**2** erreur de runner, harnais, protocole, timeout, crash ou verdict incomplet.
Le code de sortie du serveur ne suffit jamais à conclure au succès.

## Configuration JSON

Voir [l'exemple complet](../examples/labtesting.json).
Les chemins dans le JSON sont relatifs au **fichier JSON** ; les chemins CLI
`--server`, `--reports` et `--work` sont relatifs au terminal.
`serverExecutable` est relatif au dossier serveur.
Les propriétés inconnues sont rejetées pour détecter les fautes de frappe.
Les listes contiennent des fichiers explicites, sans glob implicite.

| Champ | Utilisation |
|---|---|
| server, serverExecutable | Installation et exécutable natif |
| harness | LabTesting.dll |
| tests | Bibliothèques de tests |
| plugins | Plugin testé et plugins requis |
| dependencies | Bibliothèques partagées |
| files | Source, root et target relatifs |
| serverSettings | Paires texte pour config_gameplay.txt |
| assemblies | Noms simples d'assemblies sélectionnées |
| collections | Noms exacts de collections |
| testNames | Identifiants complets affichés par list |
| frameworkTests | false par défaut |
| failOnSkipped | false par défaut ; true recommandé en CI |
| port, timeoutSeconds, tickrate | 7777, 600, 60 par défaut |
| reports, work, keep | Stockage et conservation |

Les filtres se combinent par intersection ; plusieurs valeurs d'un même filtre
forment une union. Un filtre inexistant échoue. Chaque assembly externe
sélectionnée doit produire au moins un test. Pour sélectionner une seule
assembly d'un ensemble, renseigner également `assemblies`.

Les identifiants ont la forme `Assembly:Namespace.Fixture.Methode`.
Les lignes de théorie ont un suffixe `(index)`. Les collisions d'identifiant,
de destination (y compris différences de casse) et de nom simple d'assembly
sont rejetées. Les chemins absolus, traversées `..` et DLL dissimulées dans
`files` sont interdits. Les fichiers d'infrastructure, la sentinelle et
`config_sharing.txt` sont réservés.

## Isolation de chaque lancement

Le runner crée un enfant unique `labtest-<date>-<uuid>` du dossier de travail.
Il copie les ressources natives du serveur et ses répertoires de runtime,
sans reprendre AppData ni la politique locale. Il refuse les liens symboliques
dans l'installation source. Il lance le natif depuis **cette installation copiée** :
`ConfigTemplates/` y est donc disponible.

`hoster_policy.txt` active `gamedir_for_configs: true`. LabAPI utilise alors :

```text
<copie>/AppData/SCP Secret Laboratory/LabAPI/
  plugins/<port>/       harnais et plugins déclarés
  dependencies/<port>/ dépendances déclarées
  configs/<port>/      configurations déclarées
```

Le serveur reçoit un `-configpath` séparé, créé avant lancement, contenant
`online_mode: false` et `LABTESTING_ENABLED`. L'ordre `-stdout` puis `-port`
est conservé. Les variables usuelles de profil sont également redirigées vers
la copie. Cela isole les chemins standard ; ce n'est **pas une sandbox de sécurité**
contre un plugin qui écrit dans des chemins absolus ou contacte le réseau.

Un verrou exclusif par port coordonne les runners LabTesting ; un bind UDP et
TCP détecte un port déjà utilisé. Un programme tiers peut toujours prendre le
port entre la vérification et le bind du jeu : le verdict absent/incomplet fait
alors échouer le lancement. Chaque worker doit utiliser son propre port.

L'arrêt normal, le timeout, Ctrl+C et SIGTERM provoquent l'arrêt des descendants.
Windows utilise un Job Object avec kill-on-close ; Linux un groupe de processus
créé par `setsid`, en complément de l'arrêt de l'arbre. `-id<PID>` relie aussi
le serveur au runner. Un enfant qui se détache volontairement peut sortir du
groupe Linux : utiliser une machine ou un conteneur éphémère pour les plugins
qui lancent des démons.

Aucun programme ne peut exécuter son `finally` après SIGKILL, panne de machine ou
arrêt forcé du système. Le workflow possède un nettoyage `always()` de secours ;
un runner GitHub hébergé est éphémère. Sur un agent Linux persistant,
`python3 scripts/cleanup.py /chemin/.labtest-work` récupère les copies marquées
abandonnées ; **ne pas lancer ce récupérateur sur des suites actives**.
Sous Windows, le Job Object tue l'arbre à la fermeture forcée du runner, mais
la copie disque doit être supprimée après avoir vérifié son marqueur.
Les rapports ne sont jamais supprimés par le nettoyage des copies.

## Découverte et protocole

`list` démarre le serveur, charge les plugins, découvre les tests et quitte
sans démarrer la partie ni invoquer les fixtures. Il nécessite donc l'installation
du jeu, comme `run`.

Le harnais écrit et flush immédiatement :

1. Un objet `kind: plan` contenant tous les tests, collections, assemblies et versions.
2. Un résultat par test : outcome, durées en ticks et millisecondes, isolation,
   assertions, exceptions avalées et erreur éventuelle du harnais.
3. Un unique `kind: summary` final.

Le runner utilise un parseur JSON strict et recalcule les compteurs à partir des
résultats. Il exige l'égalité entre plan et résultats, l'absence de doublons,
un résumé final cohérent et une sortie serveur nulle.
En mode list, le plan doit être non vide et le résumé doit compter zéro résultat.

## Rapports

Chaque lancement conserve `stdout.log`, `stderr.log`, `deployment.json`,
`labtesting-results.jsonl`, `junit.xml` et `summary.md` dans son dossier unique.
Un échec avant le lancement peut ne produire que JUnit et Markdown ;
un crash avant l'armement ne produit pas de JSONL.
Le manifeste de déploiement contient identités et SHA-256 des fichiers copiés.

JUnit conserve les détails JSON de chaque résultat et crée des erreurs explicites
pour les cas absents et les erreurs d'infrastructure. Il reste exploitable après
un crash ou une fin de ligne JSON tronquée. Le Markdown contient les résultats,
durées, collections, détails d'échec et versions.
