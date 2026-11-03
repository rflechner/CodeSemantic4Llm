# TypeScript / Angular analyzer

**English** · [Français](README.fr.md)

This assembly is reserved for the TypeScript and Angular indexing adapter.

Planned implementation:
- Tree-sitter-based parsing, with appropriate TypeScript and TSX grammar support.
- TypeScript-specific entities and relationships rather than forcing every construct into a C#-shaped model.
- Optional semantic enrichment when a TypeScript language-service integration is justified.
- Tests for imports, exports, components, decorators, templates, and references.

No analyzer is registered yet. Choose and validate the Tree-sitter .NET integration before adding dependencies.
Do not represent this adapter as functional until implemented.
