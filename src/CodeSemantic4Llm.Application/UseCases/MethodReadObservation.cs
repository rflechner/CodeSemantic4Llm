using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>Source metadata used to keep method pagination on the same checkout and file version.</summary>
internal sealed record MethodReadObservation(RepositoryState State, SymbolDefinition Method, string ContentHash);
