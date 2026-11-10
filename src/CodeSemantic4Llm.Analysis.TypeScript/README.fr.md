# Analyseur TypeScript

[English](README.md) · **Français**

L’analyseur `typescript` extrait les déclarations TypeScript avec Tree-sitter sans lancer
Node.js, exécuter le code du dépôt ni copier les sources dans SQLite. Il profite de
l’indexation par branche, de la recherche SQLite/FTS5, du tableau de bord, des outils MCP
`find_symbols`, `search_code` et `get_type_members`, de la réindexation incrémentale et
des résumés LLM facultatifs.

Activez-le explicitement dans `codesemantic4llm.yaml` à la racine du dépôt :

```yaml
version: 1
analyzers:
  - dotnet-csharp
  - typescript
semanticIndexing:
  language: fr
  llm:
    enabled: false
```

Les extensions par défaut sont **`.ts`, `.mts` et `.cts`**. L’analyseur extrait
espaces de noms, classes (y compris les classes décorées Angular), interfaces, énumérations
et membres, alias de types, fonctions, variables, constructeurs, méthodes et propriétés.
Chaque symbole conserve son nom qualifié, sa position d’identifiant à partir de 1 et son
type déclarant immédiat. Les commentaires JSDoc sont lus et les déclarations complètes
peuvent être envoyées temporairement au LLM. Les outils de diagnostics et la validation
syntaxique de `watch` utilisent les erreurs de Tree-sitter.

L’intégration utilise **TreeSitter.DotNet 1.3.0** et sa grammaire TypeScript native.
C’est une analyse syntaxique : elle **ne résout pas** les imports, exports,
héritages, appels ou utilisations entre fichiers via le compilateur TypeScript.
`find_usages` traite actuellement les références C#, pas les références TypeScript.
Les décorateurs Angular sont analysés comme du TypeScript ordinaire ; les expressions
des gabarits et les liens d’injection de dépendances ne sont pas indexés séparément.

**TSX/JSX n’est pas encore pris en charge.** Le package choisi ne fournit pas la
grammaire TSX distincte. Les fichiers `.tsx` sont exclus par défaut et refusés
explicitement s’ils parviennent à l’analyseur. Une surcharge `fileExtensions` ne
suffit pas à ajouter cette grammaire. Le support TSX et les références sémantiques
pourront faire l’objet de PR distinctes.

Voir la [configuration des dépôts](../../docs/repository-configuration.fr.md) et le
[guide d’indexation](../../docs/indexing.fr.md).
