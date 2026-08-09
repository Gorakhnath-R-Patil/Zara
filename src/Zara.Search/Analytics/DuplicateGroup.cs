namespace Zara.Search.Analytics;

public sealed record DuplicateFile(long FileId, string Name);

/// <param name="SizeBytes">The size shared by every file in the group (duplicates
/// necessarily have identical size — this is part of the grouping key).</param>
/// <param name="Files">Two or more files sharing this size and hash.</param>
public sealed record DuplicateGroup(long SizeBytes, IReadOnlyList<DuplicateFile> Files);
