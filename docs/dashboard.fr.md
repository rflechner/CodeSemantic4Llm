# Tableau de bord des index locaux

[English](dashboard.md) · **Français**

La commande `dashboard` ouvre un **explorateur en lecture seule** pour tous les index publiés dans le cache SQLite local configuré. Elle n’exige aucun chemin de dépôt, ne lance pas MCP et n’effectue aucune indexation.

```sh
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- dashboard
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- dashboard --port 5091
```

Ouvrez **http://127.0.0.1:5080/** (ou le port choisi) et arrêtez le processus avec Ctrl+C. Le port par défaut est **5080**. `--port` accepte les valeurs de 1 à 65535. Le serveur écoute uniquement sur la boucle locale IPv4.

L’explorateur utilise `CODESEMANTIC4LLM_INDEX_ROOT` ou, par défaut, `LocalApplicationData/CodeSemantic4Llm/indexes`, comme `index` et `serve`. Indexez chaque copie et chaque branche à examiner :

```sh
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- index /path/to/checkout
```

L’inspecteur affiche les résumés XML enregistrés et ceux du LLM facultatif, configuré par `semanticIndexing.llm`. La documentation source est indexée même sans génération. Le tableau de bord lit SQLite sans ouvrir les sources ni envoyer de code au modèle. [Markdig](https://github.com/xoofx/markdig) rend les titres, listes, emphases, blocs de code, citations et tableaux dans le panneau de résumé. Le HTML brut s’affiche comme du texte, les images gardent leur libellé et seuls les liens HTTP(S) et mailto sont cliquables. Ce rendu ne modifie pas le Markdown et ne nécessite ni réindexation ni appel au LLM.

## Données consultables

- **Projets** : bases reconnues et publiées, identifiées par le chemin d’origine. Le libellé est le dernier nom de dossier ; le `projectName` facultatif du YAML n’est pas stocké dans SQLite.
- **Branches** : instantanés publiés indépendamment, avec HEAD enregistré, date de fin, langue des descriptions et disponibilité des références.
- **Vue d’ensemble** : nombre de projets, fichiers, déclarations et usages résolus, avec répartition par catégorie.
- **Intent search** : questions en langage naturel sur les noms simples ou qualifiés et les résumés. Le classement SQLite FTS5/BM25 est celui de `search_code`. Sélectionnez une catégorie et une limite (10 par défaut, 200 maximum). Chaque résultat affiche rang, emplacement et résumé Markdown. Sélectionnez une déclaration pour voir ses usages. Un avis signale les résultats tronqués.
- **Symboles** : recherche paginée dans les noms qualifiés et les chemins, filtrée par catégorie et fichier. L’inspecteur indique l’emplacement exact, le type contenant et les usages enregistrés.
- **Fichiers** : recherche par chemin relatif, avec identifiant et version de l’analyseur, empreinte et nombre de déclarations. Sélectionnez un fichier pour voir ses symboles.
- **Références** : relations résolues sémantiquement et paginées, avec emplacements source et cible. Réindexez les anciens instantanés sans références pour les voir.

La navigation porte sur les **instantanés publiés**. La révision enregistrée ne garantit pas que les fichiers actuels correspondent encore. Le tableau de bord affiche uniquement les métadonnées SQLite. Les recherches utilisent des paramètres SQL et renvoient au plus 200 entrées par requête. Les bases absentes ou corrompues sont ignorées lors de la découverte.

## Essayer la recherche avant de connecter un LLM

Choisissez un projet et une branche, ouvrez **Intent search** et posez par exemple la question `Where do we publish a branch snapshot?`. Cliquez sur **Search code**. Les résultats identifient l’instantané et la révision utilisés. Modifier question, catégorie, limite, projet ou branche efface les résultats ; soumettez la nouvelle sélection.

La recherche est lexicale. Elle normalise la requête (mots vides anglais et français, préfixes littéraux) et découpe les noms composés (`AchatService` → `Achat Service`). Le nombre de termes trouvés prime, puis les types précèdent les membres à couverture égale, puis BM25 applique les poids 8, 4, 1 et 8 au nom simple, au nom qualifié, au résumé et aux composants du nom. Les identifiants et résumés XML restent recherchables sans LLM. Privilégiez la langue de la documentation ou celle des descriptions générées : le moteur ne traduit pas les questions et ne déduit pas de synonymes. Le rang indique un ordre, pas une probabilité.

Le tableau de bord valide l’identifiant opaque du cache et délègue au même moteur que MCP, avec le même classement et les mêmes limites. Il interroge la **branche publiée sélectionnée** sans vérifier la branche actuelle ni HEAD. On peut ainsi examiner hors ligne un index dont les emplacements ont changé depuis. Pour un index antérieur au schéma 7, relancez `index` ; une recherche ne le met jamais à niveau.

## Points d’accès HTTP internes

L’interface HTML/CSS/JavaScript hors ligne est intégrée à l’exécutable et servie sur `/`. Les routes d’API utilisent GET et renvoient du JSON.

| Route | Rôle |
| --- | --- |
| `/api/projects` | Projets valides et branches publiées |
| `/api/summary?project=ID&branch=NAME` | Répartition par catégorie et langage, nombre de références |
| `/api/files?project=ID&branch=NAME` | Page de métadonnées de fichiers |
| `/api/symbols?project=ID&branch=NAME` | Page de déclarations |
| `/api/search?project=ID&branch=NAME&q=QUESTION` | Résultats de recherche classés |
| `/api/references?project=ID&branch=NAME` | Page de relations de référence |

Les routes paginées acceptent `q` (sous-chaîne insensible à la casse), `offset` (0 par défaut) et `limit` (50 par défaut, 200 maximum). `/api/symbols` accepte aussi `kind` et `file`. Chaque déclaration conserve `summary` et fournit un fragment temporaire `summaryHtml`, nul pour un résumé vide. Restreignez `/api/references` à une déclaration en fournissant ensemble `targetPath`, `line` et `column`.

`/api/search` exige `q` (512 caractères maximum et au moins un mot recherchable), accepte `kind` et `limit` (10 par défaut, de 1 à 200). La réponse contient `snapshot`, `symbols` classés avec `summaryHtml`, et `hasMore`, sans décalage ni nombre total. Une requête invalide renvoie 400, un projet ou une branche inconnus 404, un index à reconstruire 409. L’annulation se propage au moteur de recherche partagé.

Utilisez exclusivement les identifiants opaques validés de `/api/projects` ; un chemin arbitraire est refusé. Les connexions SQLite sont en lecture seule, HTTP est limité à la boucle locale et CORS est désactivé. L’interface ne nécessite ni CDN, ni npm, ni réseau. Elle est destinée à un **poste local de confiance**, sans exposition sur Internet ou par proxy inverse.
