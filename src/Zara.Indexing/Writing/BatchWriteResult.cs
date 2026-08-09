namespace Zara.Indexing.Writing;

/// <param name="Written">Rows upserted into <c>files</c>.</param>
/// <param name="SkippedNoFrn">Entries skipped because they carried no FRN —
/// only possible from the Win32 fallback enumerator (ARCHITECTURE.md §10.2);
/// see <see cref="IFileIndexWriter"/>'s remarks for why that's a documented
/// limitation rather than a bug.</param>
public readonly record struct BatchWriteResult(int Written, int SkippedNoFrn);
