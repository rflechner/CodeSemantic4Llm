# Indexer une copie de travail avant le démarrage de MCP

[English](indexing.md) · **Français**

Pour distribuer des descriptions via Git, exporter le Markdown et vérifier le dépôt en continu, consultez les [résumés partagés](shared-summaries.fr.md).

Depuis le dépôt CodeSemantic4Llm :

```sh
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- index /path/to/repository
```

Les chemins relatifs partent du répertoire courant. Entourez de guillemets les chemins contenant des espaces. Dans Rider, passez `index /path/to/repository` comme arguments du programme. `index --help` affiche l’aide sans accéder au dépôt ni au cache.

La cible doit être la racine d’une copie Git sur une branche nommée et contenir `codesemantic4llm.yaml`. Une branche avant son premier commit est acceptée avec une révision nulle. Un HEAD détaché ou un sous-répertoire du dépôt est refusé. Si le YAML manque, lancez d’abord `init <repository-path>`.

## Portée et progression

L’indexation traite **tous** les fichiers sélectionnés par le YAML, sans la limite de 200 chemins de l’aperçu. Priorité des analyseurs, extensions et exclusions sont communes aux deux commandes. Les exclusions intégrées s’appliquent toujours. Les sous-modules sont inclus sauf exclusion. Les liens symboliques vers des fichiers ou répertoires sont ignorés et chaque chemin est contrôlé avant et après la lecture. Aucun build, restauration de paquet ou exécution du projet cible.

`dotnet-csharp` extrait par les arbres syntaxiques Roslyn le langage, la catégorie, les noms, emplacements et types contenants immédiats des déclarations. Pendant l’indexation, `CSharpCompilation` et `SemanticModel` résolvent les références aux méthodes et types indexés entre les fichiers C# sélectionnés. Seuls les emplacements de cible et d’usage sont enregistrés. Les arbres Roslyn et sources restent en mémoire pendant cette passe ; le pic mémoire dépend de la taille de la compilation. Les références aux déclarations du framework ou externes sont omises.

Les méthodes, constructeurs et types C# conservent le texte lisible complet de leur commentaire XML `<summary>` même sans LLM. `semanticIndexing.llm` active, par dépôt, la génération de descriptions pour méthodes et types via un serveur compatible OpenAI. Elle est désactivée par défaut. Les déclarations sans documentation exploitable sont envoyées au modèle ; `semanticIndexing.llm.forceGeneration: true` inclut aussi les déclarations documentées. Le modèle reçoit les déclarations entières, documentation XML comprise. Seuls les résumés générés sont limités à dix lignes. Si la réponse ne contient aucun texte exploitable, un avertissement va sur stderr, l’indexation continue et toute documentation existante est préservée. La recherche vectorielle reste prévue. [Configuration](repository-configuration.fr.md#résumés-facultatifs-par-llm)

**Limite actuelle.** La passe de références traite tous les fichiers sélectionnés comme un assembly, avec les seules références du runtime. Elle n’évalue ni frontières `.csproj`, ni frameworks cibles, symboles de compilation conditionnelle, assemblies personnalisés ou graphes NuGet/projets. Roslyn résout notamment les déclarations entre fichiers, les surcharges, les types partiels et les méthodes d’extension réduites. Les liaisons incomplètes ou ambiguës sont omises. Dans un dépôt multiprojet, zéro résultat ne démontre donc pas l’absence d’appelants.

Un terminal interactif affiche la progression Spectre pour la découverte (total inconnu), l’indexation et la vérification (nombre de fichiers et pourcentages), puis la publication. Le bilan indique le chemin absolu de la base SQLite, la branche, le HEAD enregistré, les comptes de fichiers/déclarations, le nombre de fichiers analysés/réutilisés et l’identifiant d’instantané. Une sortie redirigée ou une console IDE non interactive reçoit ce bilan en texte simple.

Code de sortie : 0 pour succès ou aide, 1 pour entrée invalide ou échec, 130 pour annulation. Ctrl+C annule l’opération. La fin n’est annoncée qu’après publication.

## Cache et isolation

Par défaut, le cache réside sous le dossier .NET `LocalApplicationData` de la plateforme :

```text
CodeSemantic4Llm/indexes/<sha256-of-canonical-checkout-root>/index.sqlite
```

Pour le déplacer, définissez `CODESEMANTIC4LLM_INDEX_ROOT` sur un chemin absolu hors du dépôt. Utilisez le même réglage pour `index` et MCP :

```sh
export CODESEMANTIC4LLM_INDEX_ROOT=/absolute/path/to/index-cache
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- index /path/to/repository
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- serve /path/to/repository
```

Chaque copie de travail, y compris un worktree Git séparé, possède sa propre base. Chaque branche nommée conserve un instantané achevé. Changer de branche ne fusionne aucune donnée. Les noms de branches sont stockés dans SQLite et ne composent aucun chemin. Déplacer ou renommer la copie sélectionne un autre cache. Aucun YAML ni fichier source n’est copié.

Le schéma versionné stocke déclarations dérivées, références et métadonnées de recherche/invalidation. Ce cache est reconstructible et ne constitue ni un format d’échange stable ni une sauvegarde. Le schéma 2 introduit l’appartenance aux types, le 3 les références indexées par position cible, le 4 les résumés facultatifs par symbole et le 5 la recherche FTS5 sur noms et résumés. `index` met à niveau les caches existants. La reconstruction du plein texte à partir des déclarations enregistrées ne demande ni LLM ni lecture des sources. L’instantané précédent reste intact jusqu’à la publication atomique. Une ancienne branche peut encore renvoyer ses déclarations mais nécessiter une mise à niveau ou une réindexation pour `find_usages` ou `search_code`. Une interruption préserve le dernier instantané publié. Les lectures MCP ne migrent jamais le stockage.

Un schéma non pris en charge provoque un échec sans modification. Sélectionnez un nouveau répertoire de cache puis relancez `index`. Supprimer un cache inutilisé suffit à éliminer ses données dérivées.

## Réindexation et publication

Depuis la racine du dépôt, lancez `codesemantic4llm index . --folder src/XXX.Domain` pour indexer uniquement ce dossier et ses sous-dossiers. Indiquez son chemin relatif à la racine, entre guillemets s’il contient des espaces. Les exclusions, la configuration des analyseurs et les contrôles des liens symboliques s’appliquent. Un dossier vide ou exclu indique zéro fichier correspondant. Ajoutez `--force` pour réanalyser les fichiers sélectionnés.

La commande s’arrête avec un index partiel consultable dans le tableau de bord. MCP continue d’utiliser le dernier instantané publié, s’il existe. En lançant ensuite `codesemantic4llm index .`, vous réutilisez les fichiers inchangés, calculez les références entre dossiers et publiez l’instantané complet de la branche.

Relancez la même commande après modification. Empreintes de contenu, identité/version de l’analyseur et empreinte de configuration déterminent la réutilisation des déclarations. Fichiers nouveaux ou modifiés sont analysés. Les chemins supprimés ou renommés disparaissent à la publication. Un changement d’analyseurs, d’extensions, d’exclusions, de langue ou de réglages LLM invalide la réutilisation. Les fichiers inchangés réutilisent leurs résumés sans appel au LLM. Tous les fichiers sélectionnés sont néanmoins lus et hachés.

Les références sont globales. Si un fichier sélectionné apparaît, change ou disparaît, toutes les références C# sont résolues sur la nouvelle compilation. Seul un ensemble identique entièrement réutilisé peut reprendre toutes les lignes de référence précédentes. Un appelant inchangé ne conserve donc pas un lien périmé vers une cible modifiée.

Relancez `index .` après une interruption pour réutiliser les fichiers terminés des tentatives précédentes sur la même branche, résumés compris. Le chemin, l’empreinte du contenu, l’analyseur, la configuration et l’empreinte du résumé partagé doivent toujours correspondre. Utilisez `index . --force` pour réanalyser chaque fichier et recalculer les références. La politique configurée pour importer les résumés partagés continue de s’appliquer.

SQLite enregistre chaque fichier terminé avec ses déclarations dans un index partiel consultable depuis le tableau de bord. La publication bascule l’instantané de la branche et retire l’ancienne génération atomiquement. Les autres branches conservent leurs instantanés. Un échec (y compris d’une requête LLM) ou une annulation laisse les fichiers terminés consultables dans le tableau de bord. MCP utilise le dernier instantané publié, s’il existe. SQLite sérialise les écritures concurrentes et échoue si le délai de verrouillage est dépassé.

Avant le commit, la commande recalcule les empreintes, vérifie les chemins découverts et relit l’état Git et le YAML. Un changement détecté empêche la publication. Cette vérification reste imparfaite : évitez les éditions et changements de branche pendant l’indexation. Le HEAD enregistré identifie l’historique observé, sans prouver que la copie correspond au commit ni qu’elle est restée inchangée.

## Réindexation continue et sécurisée avec `watch`

Lancez le surveillant dans un terminal séparé depuis la copie de travail du dépôt :

```sh
codesemantic4llm watch .
```

Le délai vient du paramètre facultatif `watch.debounceSeconds` du YAML
(**15 secondes** par défaut). Il s'applique après le **dernier événement du système de
fichiers**, pas la première sauvegarde. Toute nouvelle modification remet ce délai à zéro.
`--debounce-seconds 5` surcharge le YAML pour une exécution. Les valeurs entières
de 1 à 3600 secondes sont acceptées ; redémarrez `watch` après une modification YAML. Une vérification initiale
se lance au démarrage. Une réconciliation toutes les 30 secondes récupère aussi les
événements perdus, les débordements de surveillance et les changements Git des worktrees liés.

Avant de préparer une génération, `watch` vérifie la syntaxe des sources configurées avec
leur analyseur. Pour le C#, Roslyn détecte les erreurs **de syntaxe** ; il ne compile
pas le projet et ne bloque pas sur les erreurs de typage ou les symboles introuvables.
Les avertissements sont autorisés. Dès qu'un fichier contient une erreur syntaxique,
`watch` signale le fichier et son premier diagnostic et **ne publie pas
d'instantané incomplet**. Il réessaie après une sauvegarde ou lors du contrôle périodique.
L'ancien index publié reste consultable via MCP, même s'il ne reflète plus la copie actuelle.

Après la première vérification, seuls les contenus dont l'empreinte change sont
réanalysés syntaxiquement. Les déclarations et résumés inchangés sont réutilisés depuis
SQLite : modifier un fichier ne relance pas le LLM sur tous les autres. La publication
reste atomique et concerne toute la branche : les références entre fichiers sont
recalculées en cas de changement. Créations, suppressions, renommages, changements YAML
et Markdown de résumés partagés passent par le même pipeline, avec les exclusions YAML.
La commande `index` garde son fonctionnement explicite et peut toujours indexer
des sources dont la syntaxe est incomplète.

`watch` attend la fin des opérations Git, s'arrête avec Ctrl+C et ne prépare
ni commit ni fichier de résumé partagé. Consultez le [guide des résumés partagés](shared-summaries.fr.md).

## Rechercher avec FTS5

`search_code` interroge les noms simples, qualifiés et les résumés facultatifs stockés pour la branche active. Exemple :

```json
{"query":"where is the repository index published?","kind":"method","maxResults":10}
```

Omettez `kind` ou envoyez la valeur JSON `null` pour toutes les catégories. Un filtre concret est exact : `"method"` exclut les classes. Pour les clients qui transforment un argument facultatif absent en texte, MCP accepte aussi la chaîne `"null"` indépendamment de la casse et des espaces aux extrémités. Les autres valeurs restent des filtres.

SQLite **FTS5** indexe ces champs et **BM25** classe les résultats en donnant plus de poids aux noms simples et qualifiés. Un ensemble borné de mots vides anglais et français est écarté. Les autres mots deviennent des préfixes littéraux Unicode ; `index` peut donc trouver `indexing`. Guillemets, ponctuation, deux-points et opérateurs booléens sont neutralisés avant de construire l’expression FTS. Les valeurs sont liées comme paramètres SQL.

La pertinence est **lexicale**. Un synonyme absent du résumé peut échapper à la recherche. Noms et documentation XML restent interrogeables sans génération LLM, mais les résumés facilitent la recherche par intention.

Les requêtes lisent uniquement l’instantané SQLite publié de la branche. Aucun appel au LLM, envoi réseau ou accès aux sources. La limite est de 200 résultats avec `hasMore` ; l’emplacement départage les égalités de manière déterministe. Si le cache est antérieur au schéma 5, lancez `index`. Après modification, la réindexation met à jour ou retire les entrées FTS5 dans la même transaction que les déclarations.

## Interroger l’index via MCP

Le démarrage MCP n’indexe rien. `serve [repository-path]` sélectionne un répertoire existant, absolu ou relatif au répertoire courant, et utilise le cache de la CLI. Sans argument, `CODESEMANTIC4LLM_REPOSITORY_ROOT` sélectionne la copie. [Exemple de client MCP](../README.fr.md#configurer-un-client-mcp-avec-une-compilation-locale)

### Interface de code par défaut

`serve` par défaut et le lancement sans argument masquent configuration, aperçu, état et détails de l’index. Descriptions, paramètres et réponses omettent identifiants d’instantané, détails SQLite, empreintes et instructions de réindexation.

- `find_symbols` renvoie `symbols` et `hasMore`. Chaque déclaration fournit nom, nom qualifié, catégorie, résumé facultatif et emplacement, sans jeton temporaire.
- `search_code` prend `query`, un `kind` exact facultatif (`method`, `class`…) et `maxResults` (10 par défaut, 200 maximum). Il classe les déclarations enregistrées et renvoie `hasMore` sans lire les sources ni appeler le LLM. Les deux modes masquent l’instantané.
- `get_type_members`, `find_usages`, `get_method`, `get_method_body`, `validate_method_body`, `replace_method` et `replace_method_body` sélectionnent par `relativePath` et `name` simple ou qualifié. Ajoutez `line` et `column` de l’identificateur pour lever une ambiguïté. `CliApplication.RunAsync` peut désigner `TinyHarness.Cli.CliApplication.RunAsync`. Le suffixe doit être exact, sensible à la casse et commencer à une frontière de segment. Un qualifiant incorrect n’est jamais ignoré, `%` et `_` ne sont pas des jokers. Toutes les surcharges et tous les espaces de noms du fichier participent au contrôle d’ambiguïté, puis la position réduit ce même ensemble.
- Les requêtes de membres acceptent aussi `kind`. La navigation prend `maxResults` et `offset`, puis renvoie déclaration, membres/usages et pagination. Recopiez `nextOffset` tel quel. Un changement de génération invalide la suite.
- `get_method_body` lit directement le corps sans index ni recherche préalable. Il accepte `maxCharacters` et `offset` et renvoie `method`, `body` et une pagination par caractères. Le serveur conserve la version pour la pagination. `get_method` lit attributs, signature et corps avec les mêmes règles, hors commentaires environnants.
- `validate_method_body` prend `newBody` avec ou sans accolades et pagine les diagnostics pour une validation sans écriture.
- `replace_method_body` prend `newBody` et `replace_method` une déclaration complète `newMethod`. Ils sélectionnent la source actuelle, vérifient les changements survenus pendant l’appel, n’exigent pas de lecture préalable et renvoient seulement `methodLocation` en cas de succès. Aucun outil MCP n’accepte ni ne renvoie d’empreinte de fichier ou de méthode.
- L’écriture inclut la validation. Une syntaxe invalide laisse le fichier intact et produit une erreur MCP avec dix diagnostics au maximum et le nombre restant. Chaque diagnostic contient identifiant, message et position à partir de 1 dans le fichier proposé après ajustement d’indentation. Corrigez le code et réessayez le même outil. Les déclarations mal formées conservent les erreurs Roslyn, dont `CS1010` pour une chaîne non terminée. `CS4L0002` explique en plus une forme incorrecte (classe ou plusieurs membres).
- Les diagnostics de fichier, type ou méthode gardent les arguments de chemin/position du fichier actuel. Commencez à `offset: 0`, puis recopiez `nextOffset`. Chaque continuation conserve requête, version du fichier et, en validation, identité du corps proposé. Changer le code ou la portée impose de recommencer. Navigation, corps et diagnostics conservent chacun au plus 4 096 continuations, sans texte source.

Les adresses de méthodes survivent aux redémarrages sans historique de recherche ou de lecture. Une écriture relit le fichier, résout la déclaration et vérifie branche, révision et octets pendant l’appel. Une lecture antérieure n’autorise ni n’invalide l’écriture. Des écritures successives n’exigent ni lecture ni réindexation intermédiaire. Une modification antérieure à l’appel est acceptée même si le modèle avait vu une autre version. Chaque pagination garde ses métadonnées bornées et refuse les changements entre pages.

La navigation exploite les données dérivées et signale brièvement les données manquantes ou périmées pour intervention de l’opérateur. Opérations directes sur méthodes et diagnostics restent indépendants. Les erreurs techniques vont sur stderr et éventuellement dans le [journal fichier](logging.fr.md). Relancez `index` après modification de la source ou de la configuration pour actualiser la navigation.

### Administration facultative de l’index

`serve /path/to/repository --expose-indexing` ajoute les outils de configuration, d’aperçu et d’état indépendamment de `--allow-write`. Ni les arguments MCP ni le YAML ne peuvent accorder ces capacités. Les outils de code gardent les mêmes arguments source et réponses compactes sans empreinte ni identifiant d’instantané.

- `get_repository_configuration` lit le YAML de la copie choisie.
- `preview_repository_index` renvoie un aperçu borné des chemins candidats en lecture seule.
- `get_repository_index_status` renvoie l’état Git observé et le dernier instantané de cette branche, ou `null` en son absence. La consultation ne crée aucun cache. Seule cette réponse d’administration expose les métadonnées de l’instantané.

Cette interface remplace les anciens contrats détaillés. Actualisez la liste d’outils et sélectionnez le code par chemin et nom au lieu de `snapshotId` ou `expectedContentHash`. Les API d’application fondées sur les instantanés restent disponibles aux appelants directs sans être exposées via MCP.

La navigation peut devenir obsolète après édition, même si HEAD est inchangé. Relancez `index`. Les opérations sur méthodes et les diagnostics syntaxiques utilisent la source courante sans index.

## Lister les méthodes d’une classe

Pour `Worker`, demandez à l’agent de chercher la classe puis ses méthodes. Appelez `find_symbols` avec :

```json
{"name": "Worker", "maxResults": 20}
```

Puis `get_type_members` avec le chemin et le nom de la classe retenue :

```json
{
  "relativePath": "src/Worker.cs",
  "name": "Worker",
  "kind": "method",
  "maxResults": 50,
  "offset": 0
}
```

La réponse contient le type, les membres correspondants et la pagination. Les emplacements distinguent les surcharges. Les méthodes héritées ou appartenant aux classes imbriquées sont exclues. Utilisez `kind: "constructor"` pour les constructeurs. Membres générés, opérateurs et accesseurs ne sont pas extraits comme méthodes ordinaires. Aucun corps source n’est renvoyé ni stocké par cette requête.

La sélection par emplacement exact sépare les types homonymes et génériques. Pour un type partiel, interrogez chaque partie voulue renvoyée par `find_symbols`. La réunion des parties, l’héritage et les signatures complètes nécessitent une analyse sémantique ultérieure.

## Trouver les usages d’une méthode ou d’un type

Appelez `find_symbols` avec `{"name":"Run"}`. Choisissez dans `symbols` la méthode voulue grâce à son nom qualifié, son type contenant et son emplacement. Appelez ensuite `find_usages`, avec `line` et `column` si nécessaire :

```json
{
  "relativePath": "src/Worker.cs",
  "name": "Worker.Run",
  "maxResults": 50,
  "offset": 0
}
```

La réponse fournit `target`, `usages` (chemin relatif, ligne et colonne), `hasMore` et `nextOffset`. Pour une classe, les usages incluent les noms de type écrits explicitement : `new Worker()`, annotations, classes de base et arguments de type résolus par Roslyn. Surcharges et appels de méthodes d’extension sont reliés à la méthode déclarée. Les usages non résolus, dynamiques ou par réflexion ne sont pas inférés. `find_usages` ne lit aucun corps source. Réindexez après modification.

## Vérifier la syntaxe du code actuel

Quatre outils en lecture seule utilisent Roslyn sans lancer MSBuild, exécuter le projet ni écrire de fichier :

- `get_file_diagnostics` prend uniquement `relativePath` et vérifie **tout le fichier actuel**, même si une déclaration mal formée ne peut être sélectionnée. Utile après une modification lorsque la position de l’erreur est inconnue, même sans index.
- `get_type_diagnostics` prend `relativePath`, `line`, `column` et `expectedTypeName` de l’identificateur. Il vérifie une déclaration de classe, record, structure, interface, enum ou délégué, membres et types imbriqués compris. Les autres parties d’un type partiel et les déclarations voisines sont exclues. Nom et position doivent correspondre. Si l’édition a déplacé ou endommagé le type, utilisez les diagnostics de fichier.
- `validate_method_body` prend `relativePath`, `name` et `newBody`, bloc complet ou instructions internes. La validation se fait **en mémoire** dans le contexte de la méthode, sans lecture préalable ni version à fournir. Pour écrire, appelez directement `replace_method_body` qui applique la même validation. Un bloc incomplet ou invalide produit `CS4L0001`. Les accolades externes omises sont ajoutées et les diagnostics portent sur le fichier proposé normalisé. Les deux modes utilisent les mêmes arguments.
- `get_method_diagnostics` prend `relativePath`, `line`, `column` et `expectedMethodName` issus par exemple d’une recherche antérieure. Il analyse le fichier **actuellement sur disque**, sans instantané. Utilisez-le juste après `replace_method_body` ou une édition externe. Il refuse de sélectionner une autre méthode si le nom ou la position ont changé.

Les quatre renvoient `isValid`, `diagnostics` (`id`, `severity`, `message`, `location` à partir de 1), `totalDiagnostics`, `hasMore` et `nextOffset`. Chaque page contient 50 diagnostics par défaut, 200 au maximum. `isValid` tient compte de **tous** les diagnostics. Un outil ciblant une méthode ne peut conclure sur la syntaxe du reste du fichier.

Les diagnostics fichier/type ajoutent `relativePath` et, pour le type, `typeLocation`. Le sérialiseur omet les champs nuls, dont `nextOffset` en dernière page. Le YAML détermine l’analyseur disponible et les extensions. Un langage non pris en charge est refusé. Seul le chemin demandé est lu, sans découverte ni SQLite : les exclusions de découverte ne bloquent pas une demande de diagnostic explicite. Le chemin doit rester dans le dépôt et ne peut traverser de lien symbolique. La version est suivie en interne, sans retour ni stockage de texte source.

Suivez `nextOffset` sans métadonnées de version, même avec `--expose-indexing`. Si le fichier change, recommencez à 0. Exemple pour `get_file_diagnostics` :

```json
{"relativePath":"src/Worker.cs","maxDiagnostics":50}
```

Conservez chemin et portée pour la page suivante. Les positions physiques commencent à 1, même avec des directives `#line`. Les plages suivent l’arbre de récupération Roslyn : une accolade manquante peut englober du code ultérieur. En cas de structure endommagée, vérifiez le fichier entier.

Il s’agit de **validation syntaxique uniquement**. `return Unknown.Customer.Name;` peut être syntaxiquement correct alors que des noms manquent ou que le type de retour est incompatible. Ce contrôle exige une future compilation tenant compte des projets. L’indexeur utilise les options C# par défaut de Roslyn sans évaluer version de langage du projet ni symboles de compilation conditionnelle. Les régions inactives ne sont pas analysées comme code actif.

Parcours conseillé pour l’agent :

```text
get_method ou get_method_body (inspection et recherche facultatives)
    → replace_method_body ou replace_method (uniquement avec serve --allow-write)
    → get_file_diagnostics (syntaxe du fichier actuel)
    → relire git diff → index (actualiser la navigation)
```

## Lire et remplacer le corps d’une méthode

Le serveur est en lecture seule par défaut. Pour autoriser explicitement un client à modifier le code :

```sh
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- \
  serve /absolute/path/to/repository --allow-write
```

`--allow-write` est une **permission au démarrage**. Ni `codesemantic4llm.yaml` ni un appel d’outil ne l’accordent. Sans elle, `replace_method_body` est absent de la découverte. Le serveur lancé sans argument reste en lecture seule. `get_method_body` fonctionne dans les deux modes.

1. Sélectionnez la méthode par `relativePath` et `name`. Ajoutez `line` et `column` de l’identificateur pour les surcharges. Si l’adresse est connue, la recherche est facultative.
2. Au besoin, lisez le corps avec `get_method_body` ou la déclaration entière avec `get_method`. Une lecture de corps contient `body.text`, `totalCharacters` et `nextOffset`. Paginer permet de lire les longs corps.
3. Appelez `replace_method_body` avec la même adresse et un `newBody` contenant un bloc C# ou ses instructions. Les accolades externes omises sont ajoutées. L’outil ajuste l’indentation, y compris pour des commentaires avant une accolade ouvrante explicite. Conservez l’indentation relative interne ; nul besoin de reproduire celle du type ou de l’espace de noms. Un bloc initial explicite doit être complet. Accolades déséquilibrées et flux de contrôle invalide sont refusés. Une méthode à corps expression peut devenir un bloc. L’outil ne réécrit ni signature, attributs ou voisines, ni n’ajoute d’implémentation aux méthodes abstraites ou d’interface. En cas d’échec, aucune écriture et au plus dix erreurs syntaxiques identifiées et localisées dans le fichier proposé. Corrigez et réessayez sans validation séparée.
4. Vérifiez le fichier par `get_file_diagnostics` (ou la méthode par `get_method_diagnostics`), relisez le diff Git et relancez `index`. L’écriture laisse l’ancien index intact. La réponse de succès contient uniquement le nouvel emplacement de méthode.

Arguments d’édition :

```json
{
  "relativePath": "src/Worker.cs",
  "name": "Worker.Run",
  "newBody": "{ return 42; }"
}
```

`"newBody": "return 42;"` est également valide et sera entouré d’accolades avant validation et écriture. Fournissez uniquement le corps, sans signature.

Le serveur déduit l’indentation de la déclaration et de ses instructions ou des déclarations englobantes pour un corps vide ou expression. À défaut de style déductible, il utilise quatre espaces. L’indentation relative, les lignes vides et les accolades en ligne sont préservées. L’outil ne lit pas `.editorconfig`. Contenu des chaînes et commentaires multilignes et texte inactif du préprocesseur restent intacts. Les autres fins de ligne suivent le fichier. Le validateur applique le même ajustement pour que ses positions correspondent à l’édition proposée.

L’application résout la déclaration Roslyn actuelle pendant l’appel puis vérifie branche, révision et octets du fichier. Le client n’a besoin ni d’une lecture préalable, ni d’une empreinte, ni d’un identifiant d’instantané. Aucune comparaison n’est faite avec une ancienne lecture du modèle. L’adaptateur refuse remontées de chemins, liens symboliques et sources non UTF-8. Il écrit un fichier temporaire voisin puis remplace l’original en préservant le BOM UTF-8 et le texte hors du corps. L’écriture ne compile pas, ne teste pas, n’exécute aucun code et ne prépare ni commit ni fichiers pour Git.

Un dernier contrôle d’empreinte détecte les éditions concurrentes ordinaires sans garantir un échange atomique entre processus. Évitez plusieurs écrivains sur la même copie, relisez les changements et utilisez Git pour annuler une modification indésirable.

### Chemins source dans les réponses MCP

Tout objet de réponse avec `relativePath` inclut aussi `absolutePath`, y compris emplacements imbriqués, références, diagnostics, lectures/éditions et candidats d’aperçu. Le chemin absolu se résout depuis la racine configurée au format du serveur. Les arguments d’entrée restent relatifs au dépôt. Les chemins absolus sont ajoutés à la présentation JSON de MCP ; les données persistées conservent les chemins relatifs.
