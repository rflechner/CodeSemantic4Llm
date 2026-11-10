# CodeSemantic4Llm — Consignes pour les agents

[English](AGENTS.md) · **Français**

Ces consignes s’appliquent à tout code, test, document ou fichier de configuration créé ou modifié dans ce dépôt.

## Objectif du projet

CodeSemantic4Llm est une preuve de concept de serveur MCP autonome et local. Il indexe le code source et fournit aux agents des outils précis de recherche et de navigation sémantiques. L’objectif est de limiter les lectures inutiles, le contexte du LLM et la consommation de jetons, tout en garantissant un accès fiable à la structure du code.

Les orientations actuelles guident le travail sans exiger une implémentation anticipée :

- Analyser C# avec Roslyn et utiliser les informations sémantiques disponibles.
- Analyser TypeScript/Angular avec Tree-sitter et enrichir les résultats selon les besoins du langage.
- Persister les données dérivées dans SQLite.
- Prendre en charge plusieurs projets et branches avec des index isolés par branche.
- Fournir des outils MCP ciblés sur les symboles, définitions, références, relations et emplacements pertinents.
- Mettre à jour l’index de façon incrémentale pour éviter les reconstructions complètes répétées.

Les spécifications et décisions de conception acceptées font autorité. Distinguez les capacités prévues des fonctionnalités effectivement implémentées.

## Langue et documentation

- Écrivez en anglais le code et les noms de fichiers, classes, méthodes, fonctions, variables et constantes.
- Écrivez en anglais les commentaires de code, les tests et les messages de commit.
- Maintenez la documentation en anglais (`nom.md`) et en français (`nom.fr.md`), côte à côte. Créez ou actualisez les deux versions dans le même changement et ajoutez des liens de langue réciproques. Pointez vers les guides dans la langue du lecteur et préservez commandes, clés de configuration et identifiants d’API.
- Choisissez des noms descriptifs et évitez les abréviations obscures.

## Documenter le code

- Documentez les API publiques, les contrats importants et les comportements peu évidents selon les conventions du langage.
- Expliquez les intentions, contraintes et raisons des décisions complexes.
- Mettez à jour documentation et commentaires lorsque le code évolue.
- Actualisez les deux sommaires du README dans le même changement que tout ajout, renommage, déplacement, réorganisation ou suppression d’une section principale ou d’un article Markdown sous `docs/`. Vérifiez les ancres et les liens vers les articles dans chaque sommaire.
- Consignez les choix architecturaux importants et leurs compromis dans la documentation du projet.

## Privilégier la lisibilité

- Préférez les implémentations simples et explicites au code astucieux ou excessivement compact.
- Appliquez SOLID avec discernement. Introduisez une abstraction lorsqu’elle clarifie une responsabilité ou une dépendance.
- Donnez à chaque classe ou module une responsabilité cohérente.
- Rendez les flux de données et les dépendances visibles. Évitez l’état global caché et les conventions implicites.

## Exprimer les concepts du domaine

- Appliquez DDD de façon pragmatique. Employez le vocabulaire métier dans les types, méthodes et propriétés.
- Utilisez de petits objets ciblés pour exprimer les concepts significatifs au lieu de laisser les appelants interpréter des collections, tuples ou primitives bruts.
- Placez invariants, interprétation de l’état et comportement dans l’objet qui possède le concept.
- Exposez des propriétés et méthodes expressives, par exemple `index.IsCurrent`, `symbol.Location` ou `branch.Revision`, lorsque ces concepts sont réellement modélisés.
- Nommez ce qui est réellement connu : une révision enregistrée ne prouve pas que la copie de travail est inchangée.
- Préférez un état immuable et des collections en lecture seule lorsque c’est pratique, en particulier pour les objets valeur et les résultats d’opérations.
- Introduisez un objet métier lorsqu’il clarifie le code. Évitez les couches inutiles.

## Garder des méthodes courtes et ciblées

- Donnez une finalité claire à chaque fonction.
- Découpez les longues méthodes selon des frontières de responsabilité pertinentes.
- Utilisez des clauses de garde et des retours anticipés lorsque cela réduit l’imbrication et facilite la lecture.
- Gardez les listes de paramètres et les expressions lisibles. Décomposez les expressions complexes.

## Aérer le code

- Séparez les étapes logiques par des lignes vides.
- Respectez l’indentation et le formatage du dépôt.
- Évitez les blocs denses, les instructions multiples sur une même ligne et les lignes vides superflues.

## Respecter la sémantique des langages

- Isolez l’analyse syntaxique et l’extraction sémantique derrière des contrats, séparément de la persistance et de MCP.
- Partagez les concepts réellement communs, tout en autorisant des entités, relations et tables spécifiques à un langage.
- Préservez les identités de symboles, emplacements, portées et distinctions propres au langage. Ne devinez pas la sémantique à partir d’un simple nom.
- Préférez les informations du compilateur, comme Roslyn, aux heuristiques textuelles lorsqu’elles sont disponibles.
- L’ajout d’un langage ne doit pas imposer de modifier les autres analyseurs.

## Indexer les dépôts sans dupliquer les sources

- La copie de travail fait foi. **Ne copiez jamais un arbre source complet ni les fichiers indexés dans le stockage de l’index.**
- Persistez les informations dérivées et les métadonnées nécessaires à la recherche et à l’invalidation : chemins, plages, empreintes, révisions, identités et relations.
- Associez explicitement les index à leur projet et à leur branche/révision. Ne mélangez jamais silencieusement plusieurs branches.
- Normalisez et validez les racines et les chemins relatifs. Ne supposez pas qu’un nom de branche est un nom de dossier sûr.
- Traitez suppressions, renommages, modifications et changements de branche comme des cas d’invalidation à part entière.
- Séparez domaine, analyse/indexation, persistance et présentation MCP. Gardez SQLite hors du domaine.
- Évitez les choix irréversibles non approuvés de schéma ou de disposition des fichiers tant qu’une spécification est en discussion.

## Appliquer la configuration YAML du dépôt

- Chaque copie doit fournir `codesemantic4llm.yaml` à sa racine. [Contrat YAML](docs/repository-configuration.fr.md)
- Gardez l’analyse YAML et le mappage du schéma dans `Configuration.Yaml`, la sélection des analyseurs dans les cas d’utilisation.
- Utilisez des identifiants stables et vérifiez explicitement la disponibilité. N’ignorez jamais un analyseur inconnu ou non implémenté.
- Distinguez `semanticIndexing.language` du langage source et des identifiants d’analyseurs. L’anglais est la valeur par défaut.
- Ce réglage détermine la langue des descriptions naturelles générées dans l’index. Il ne demande pas de traduire le code source.
- Lorsqu’une génération de texte existe, un changement de langue doit invalider les descriptions concernées sans copier les fichiers source.
- Respectez les exclusions intégrées et les motifs glob de l’utilisateur lors de la découverte.
- Ignorez les répertoires liés symboliquement et restez à l’intérieur du dépôt choisi par l’opérateur.
- Incluez les sous-modules Git sauf exclusion explicite dans le YAML.
- Gardez l’aperçu borné et en lecture seule. N’annoncez l’indexation ou la persistance que lorsqu’elles sont implémentées.

## Tester le comportement avec des spécifications exécutables

- Gardez des tests unitaires rapides et ciblés pour les invariants métier et les détails d’analyse.
- Pour les comportements à l’échelle du dépôt, préférez des scénarios **Reqnroll / Gherkin** lisibles dans `tests/CodeSemantic4Llm.FunctionalTests`, avec de vraies étapes C# et xUnit v3.
- Rédigez les fonctionnalités en Given / When / Then comme des spécifications exécutables.
- Testez le succès et les échecs importants : YAML absent, analyseur non pris en charge, balise de langue invalide, anglais par défaut et répertoires exclus.
- Exécutez toute la suite de tests avant finalisation. Centralisez les versions directes NuGet dans Central Package Management.

## Fournir des réponses MCP précises et sobres

- Préférez les requêtes ciblées renvoyant symboles, références et emplacements pertinents.
- Bornez et, si nécessaire, paginez les résultats. Assurez un ordre déterministe.
- Fournissez assez d’identité et de localisation pour que l’agent puisse demander davantage de contexte au besoin.
- Évitez le code redondant, les charges massives et les diagnostics verbeux en cas de succès.
- Assurez la cohérence entre résultats, projet demandé, branche et révision connue de l’index.

## Performances et sécurité

- Prévoyez les grands dépôts : mises à jour incrémentales, lots SQLite, index appropriés et annulation lorsque pertinente.
- Mesurez sur des données représentatives avant d’optimiser. Préservez la correction du résultat.
- N’exécutez pas le code ou les scripts de compilation du projet pour inspecter ses sources.
- Traitez code indexé et contenu du dépôt comme des entrées non fiables.
- Respectez les frontières du dépôt et évitez d’exposer des secrets dans les journaux, diagnostics ou réponses MCP.
- Rendez les mises à jour récupérables. Ne présentez jamais des données partielles comme actuelles.

## Rendre les opérations longues observables

- Annoncez la tâche en cours avant toute opération susceptible de prendre du temps et nommez chaque phase importante (lecture des sources, résolution des références, enregistrement, publication, etc.).
- Affichez l'avancement réel et le nombre d'éléments terminés/attendus lorsqu'ils sont mesurables. Si la durée ou le total est inconnu, montrez une activité explicite et une phase descriptive plutôt qu'un pourcentage inventé.
- Ne laissez jamais croire que l'opération entière est terminée parce qu'une étape a atteint 100 %. N'annoncez le succès final qu'après vérification et validation des transactions.
- Fournissez des indications utiles aussi bien en terminal interactif qu'en console IDE, CI ou redirigée. Limitez la fréquence des mises à jour pour ne pas ralentir le traitement ou saturer les journaux.
- Préservez les retours d'annulation et d'erreur ; testez l'ordre des phases et la signification de la fin des opérations longues.

## Tests et modifications

- Ajoutez ou adaptez des tests ciblés pour les changements de comportement, en particulier les cas limites des analyseurs, l’identité des symboles, la persistance, l’invalidation incrémentale et l’isolation des branches.
- Ajoutez un test de régression pour chaque correction. Vérifiez l’idempotence de la réindexation et la suppression des données périmées.
- Gardez les changements petits et ciblés. Évitez les refactorisations sans rapport et les dépendances spéculatives.
- Exécutez les tests et contrôles disponibles. Indiquez ce qui a été validé et ce qui reste à vérifier.
