namespace Zara.Ai.QueryCompilation;

/// <summary>
/// Compiles natural language into a <c>StructuredQuery</c> via the LLM —
/// ARCHITECTURE.md §14.3. Only reached for queries <see cref="IIntentRouter"/>
/// couldn't resolve deterministically; T46's golden-set harness measures
/// what fraction of real queries reach this path (the target is ≤25%).
/// </summary>
public interface IQueryCompiler
{
    Task<CompiledQuery> CompileAsync(string naturalLanguageQuery, CancellationToken cancellationToken = default);
}
