using Zara.Core.Files;

namespace Zara.Search.Names;

/// <inheritdoc cref="INameIndex"/>
/// <remarks>
/// <para><b>Structure (§12.2's design, in ordinary C# collections rather than
/// the packed-arena/RoaringBitmap form the architecture doc sketches):</b>
/// parallel arrays hold id/name/folded-name per slot; a trigram → slot-set
/// dictionary provides fast candidate pre-filtering, verified against a real
/// substring check before anything is returned. Deferring RoaringBitmap
/// (there's no vetted package for it in this solution yet, and TRACKER.md's
/// M3 notes explicitly say to measure a simpler structure first) — swap the
/// candidate-set representation for one if profiling at the 500k+ file scale
/// shows plain <see cref="HashSet{T}"/> intersection isn't fast enough.
/// Not yet implemented: diacritic folding (only <c>ToLowerInvariant</c> right
/// now) — a documented, deliberate simplification, not an oversight. Top-K
/// selection IS bounded (a sorted list capped at <c>maxResults</c>, not a
/// full sort) after T21's 500k-scale benchmark showed the earlier
/// <c>OrderBy().Take()</c> approach costing real time on unselective
/// queries — see <see cref="Search"/>'s remarks for the measured numbers.</para>
///
/// <para><b>Thread safety:</b> every public member takes an internal lock.
/// The Engine process (M7+) will have one writer thread applying index
/// deltas concurrently with reader threads serving search RPCs — this type
/// has to be safe for that from day one rather than retrofitted later.</para>
/// </remarks>
public sealed class NameIndex : INameIndex
{
    private readonly object _gate = new();

    private readonly Dictionary<FileId, int> _slotByFileId = [];
    private readonly List<FileId> _ids = [];
    private readonly List<string> _names = [];
    private readonly List<string> _folded = [];
    private readonly List<bool> _tombstoned = [];
    private readonly Stack<int> _freeSlots = new();
    private readonly Dictionary<string, HashSet<int>> _trigrams = new(StringComparer.Ordinal);

    public int Count
    {
        get { lock (_gate) { return _slotByFileId.Count; } }
    }

    public void Upsert(FileId fileId, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        lock (_gate)
        {
            if (_slotByFileId.TryGetValue(fileId, out int existingSlot))
            {
                RemoveFromTrigramIndex(existingSlot);
                _names[existingSlot] = name;
                _folded[existingSlot] = Fold(name);
                AddToTrigramIndex(existingSlot);
                return;
            }

            int slot;
            if (_freeSlots.Count > 0)
            {
                slot = _freeSlots.Pop();
                _ids[slot] = fileId;
                _names[slot] = name;
                _folded[slot] = Fold(name);
                _tombstoned[slot] = false;
            }
            else
            {
                slot = _ids.Count;
                _ids.Add(fileId);
                _names.Add(name);
                _folded.Add(Fold(name));
                _tombstoned.Add(false);
            }

            _slotByFileId[fileId] = slot;
            AddToTrigramIndex(slot);
        }
    }

    public void Remove(FileId fileId)
    {
        lock (_gate)
        {
            if (!_slotByFileId.Remove(fileId, out int slot))
            {
                return;
            }

            RemoveFromTrigramIndex(slot);
            _tombstoned[slot] = true;
            _ids[slot] = default;
            _names[slot] = string.Empty;
            _folded[slot] = string.Empty;
            _freeSlots.Push(slot);
        }
    }

    public IReadOnlyList<NameHit> Search(string query, int maxResults = 50)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        if (maxResults <= 0)
        {
            return [];
        }

        lock (_gate)
        {
            string[] tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
            {
                return [];
            }

            HashSet<int>? candidates = null;
            foreach (string rawToken in tokens)
            {
                string foldedToken = Fold(rawToken);
                HashSet<int> tokenCandidates = foldedToken.Length >= 3
                    ? CandidatesFromTrigrams(foldedToken)
                    : AllLiveSlots(); // short tokens aren't trigram-indexed — see class remarks

                if (candidates is null)
                {
                    candidates = tokenCandidates;
                }
                else
                {
                    candidates.IntersectWith(tokenCandidates);
                }

                if (candidates.Count == 0)
                {
                    return [];
                }
            }

            string foldedQuery = Fold(query);

            // Bounded top-K selection, not "score everything then
            // OrderBy().Take()": LINQ's OrderBy must fully sort its input
            // before it can yield anything, even with a Take() after it —
            // Take does not turn it into a partial sort. For an unselective
            // query (a common word matching tens of thousands of entries),
            // a full O(n log n) sort of every candidate to keep the top 50
            // is exactly the cost this design's own remarks flagged as
            // deferred — and T21's 500k-scale benchmark confirmed it's real
            // (p95 came in at ~20ms, right at the ceiling, on unselective
            // queries). This keeps a sorted list of at most maxResults
            // "best so far" — O(n log k) instead of O(n log n), and O(1)
            // rejection for most candidates once it's full and they're worse
            // than the current worst-kept.
            var kept = new List<(int Slot, NameMatchKind Kind)>(Math.Min(maxResults, candidates!.Count));

            foreach (int slot in candidates)
            {
                if (_tombstoned[slot])
                {
                    continue;
                }

                var kind = ClassifyMatch(_folded[slot], foldedQuery, tokens);
                if (kind is not { } k)
                {
                    continue;
                }

                var item = (Slot: slot, Kind: k);

                if (kept.Count < maxResults)
                {
                    kept.Insert(FindInsertionPoint(kept, item), item);
                }
                else if (CompareCandidates(item, kept[^1]) < 0)
                {
                    kept.RemoveAt(kept.Count - 1);
                    kept.Insert(FindInsertionPoint(kept, item), item);
                }
            }

            var results = new List<NameHit>(kept.Count);
            foreach (var (slot, kind) in kept)
            {
                results.Add(new NameHit(_ids[slot], _names[slot], kind));
            }

            return results;
        }
    }

    /// <summary>Same ordering as the old <c>OrderBy(Kind).ThenBy(Name)</c>:
    /// match quality first, then name, alphabetically.</summary>
    private int CompareCandidates((int Slot, NameMatchKind Kind) a, (int Slot, NameMatchKind Kind) b)
    {
        int kindCompare = a.Kind.CompareTo(b.Kind);
        return kindCompare != 0 ? kindCompare : string.Compare(_names[a.Slot], _names[b.Slot], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Manual binary search for the sorted-insert position — avoids
    /// allocating an <see cref="IComparer{T}"/> per call (this runs once per
    /// kept candidate, not once per query, so that allocation would add up).</summary>
    private int FindInsertionPoint(List<(int Slot, NameMatchKind Kind)> sorted, (int Slot, NameMatchKind Kind) item)
    {
        int lo = 0;
        int hi = sorted.Count;

        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (CompareCandidates(sorted[mid], item) <= 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    /// <summary>
    /// Trigram candidates are a SOUND but not PRECISE pre-filter — every
    /// trigram of the token being present in a name does not guarantee the
    /// token itself appears as a contiguous substring (the trigrams could
    /// come from unrelated parts of the name). This is the verification step
    /// ARCHITECTURE.md §12.2 calls out explicitly ("verify each candidate
    /// with an ordinal IndexOf"); returns null for a trigram false positive.
    /// </summary>
    private static NameMatchKind? ClassifyMatch(string foldedName, string foldedQuery, string[] tokens)
    {
        foreach (string token in tokens)
        {
            string foldedToken = Fold(token);
            if (foldedToken.Length > 0 && !foldedName.Contains(foldedToken, StringComparison.Ordinal))
            {
                return null;
            }
        }

        if (foldedName.Equals(foldedQuery, StringComparison.Ordinal))
        {
            return NameMatchKind.Exact;
        }

        if (foldedName.StartsWith(foldedQuery, StringComparison.Ordinal))
        {
            return NameMatchKind.Prefix;
        }

        int idx = foldedName.IndexOf(foldedQuery, StringComparison.Ordinal);
        if (idx > 0 && IsWordBoundary(foldedName[idx - 1]))
        {
            return NameMatchKind.WordBoundary;
        }

        // Every token individually verified present above, even if (for a
        // multi-token query) not as one contiguous run — still a real match,
        // just the weakest tier.
        return NameMatchKind.Substring;
    }

    private static bool IsWordBoundary(char c) => c is ' ' or '-' or '_' or '.' or '\\' or '/';

    private HashSet<int> CandidatesFromTrigrams(string foldedToken)
    {
        HashSet<int>? result = null;

        foreach (string trigram in Trigrams(foldedToken))
        {
            if (!_trigrams.TryGetValue(trigram, out var set) || set.Count == 0)
            {
                return []; // a required trigram has zero matches -> empty intersection
            }

            if (result is null)
            {
                result = new HashSet<int>(set);
            }
            else
            {
                result.IntersectWith(set);
            }

            if (result.Count == 0)
            {
                return [];
            }
        }

        return result ?? [];
    }

    private HashSet<int> AllLiveSlots()
    {
        var result = new HashSet<int>();
        for (int i = 0; i < _ids.Count; i++)
        {
            if (!_tombstoned[i])
            {
                result.Add(i);
            }
        }

        return result;
    }

    private void AddToTrigramIndex(int slot)
    {
        string folded = _folded[slot];
        if (folded.Length < 3)
        {
            return;
        }

        foreach (string trigram in Trigrams(folded))
        {
            if (!_trigrams.TryGetValue(trigram, out var set))
            {
                set = [];
                _trigrams[trigram] = set;
            }

            set.Add(slot);
        }
    }

    private void RemoveFromTrigramIndex(int slot)
    {
        string folded = _folded[slot];
        if (folded.Length < 3)
        {
            return;
        }

        foreach (string trigram in Trigrams(folded))
        {
            if (_trigrams.TryGetValue(trigram, out var set))
            {
                set.Remove(slot);
                if (set.Count == 0)
                {
                    _trigrams.Remove(trigram);
                }
            }
        }
    }

    private static IEnumerable<string> Trigrams(string folded)
    {
        for (int i = 0; i + 3 <= folded.Length; i++)
        {
            yield return folded.Substring(i, 3);
        }
    }

    private static string Fold(string s) => s.ToLowerInvariant();
}
