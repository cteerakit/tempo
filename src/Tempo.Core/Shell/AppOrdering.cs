namespace Tempo.Core.Shell;

/// <summary>The user's drag-and-drop order of app buttons, keyed by each button's group key.</summary>
public static class AppOrdering
{
    /// <summary>
    /// Sorts <paramref name="items"/> by their position in <paramref name="savedOrder"/>. Items the user
    /// never placed keep their natural relative order and go after the placed ones.
    /// </summary>
    public static List<T> Apply<T>(IReadOnlyList<T> items, Func<T, string> key, IReadOnlyList<string> savedOrder,
        Func<T, bool>? isPinned = null)
    {
        if (savedOrder.Count == 0 && isPinned is null)
            return [.. items];

        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < savedOrder.Count; i++)
            rank.TryAdd(savedOrder[i], i);

        return items
            .Select((item, index) => (item, index, rank: rank.TryGetValue(key(item), out var r) ? r : int.MaxValue))
            .OrderBy(x => isPinned is null || isPinned(x.item) ? 0 : 1)
            .ThenBy(x => x.rank)
            .ThenBy(x => x.index)
            .Select(x => x.item)
            .ToList();
    }

    /// <summary>
    /// The order to save after a drag: the visible keys as arranged, then previously saved keys that
    /// aren't visible right now (closed apps), so they return to roughly where they were.
    /// Per-window keys ("hwnd:") are never saved because they don't survive the window closing.
    /// </summary>
    public static List<string> Merge(IEnumerable<string> visibleKeys, IEnumerable<string> savedOrder)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in visibleKeys.Concat(savedOrder))
        {
            if (!k.StartsWith("hwnd:", StringComparison.OrdinalIgnoreCase) && seen.Add(k))
                result.Add(k);
        }
        return result;
    }
}
