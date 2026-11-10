# TypeScript analyzer

**English** · [Français](README.fr.md)

The `typescript` analyzer extracts TypeScript declarations through Tree-sitter without running
Node.js, evaluating repository code, or copying source files into SQLite. It participates in
branch-aware indexing, SQLite/FTS5 search, the dashboard, MCP `find_symbols` / `search_code` /
`get_type_members`, incremental reuse, and optional LLM summarization.

Enable it explicitly in the repository-root `codesemantic4llm.yaml`:

```yaml
version: 1
analyzers:
  - dotnet-csharp
  - typescript
semanticIndexing:
  language: en
  llm:
    enabled: false
```

Supported extensions are **`.ts`, `.mts`, `.cts`** by default. The adapter extracts
namespaces, classes (including Angular-decorated classes), interfaces, enums and enum members,
type aliases, functions, variables, constructors, methods and properties. Symbols retain
qualified names, one-based identifier locations, and immediate declaring types. It reads
leading JSDoc text and passes complete transient declarations with JSDoc to the optional LLM.
`get_file_diagnostics`, `get_type_diagnostics` and `watch` syntax validation use Tree-sitter
parse errors.

This adapter uses **TreeSitter.DotNet 1.3.0** and its bundled TypeScript native grammar.
It is syntax-based: it does **not** resolve imports, exports, inheritance, calls, or usages
across files with a TypeScript compiler. `find_usages` currently resolves C# references,
**not** TypeScript references. Angular decorators are parsed as ordinary TypeScript syntax;
template expressions and dependency injection relationships are not separately indexed.

**TSX/JSX is not supported yet.** The selected package does not bundle the distinct TSX
grammar. `.tsx` is intentionally excluded from the defaults and explicitly rejected if
passed to the analyzer. A `fileExtensions` override cannot supply a missing grammar.
Full TSX support and compiler-backed TypeScript references are separate follow-up work.

See [repository configuration](../../docs/repository-configuration.md) and
[indexing](../../docs/indexing.md).
