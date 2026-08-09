using Zara.Core.Files;

namespace Zara.Search.Names;

public readonly record struct NameHit(FileId FileId, string Name, NameMatchKind MatchKind);
