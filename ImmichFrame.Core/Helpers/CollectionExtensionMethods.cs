namespace ImmichFrame.Core.Helpers;

public static class CollectionExtensionMethods
{
    public static IEnumerable<T> TakeProportional<T>(this IEnumerable<T> enumerable, double proportion)
    {
        if (proportion <= 0) return [];

        var list = enumerable.ToList();
        var itemsToTake = (int)Math.Ceiling(list.Count * proportion);
        return list.Take(itemsToTake);
    }

    public static IEnumerable<T> WhereExcludes<T>(this IEnumerable<T> source, IEnumerable<T> excluded, Func<T, object> comparator)
    {
        var excludedKeys = excluded.Select(comparator).ToHashSet();
        return source.Where(item => !excludedKeys.Contains(comparator(item)));
    }

    public static async Task<T?> ChooseOne<T>(this IEnumerable<T> sources, Func<T, Task<long>> probabilitySelector)
    {
        var sourcesAndCounts = await Task.WhenAll(
            sources.Select(async source =>
                {
                    try
                    {
                        return (Source: source, Count: await probabilitySelector(source));
                    }
                    catch
                    {
                        // A failing source (e.g. an unreachable account) contributes no weight so the
                        // remaining healthy sources can still be chosen instead of faulting the whole draw.
                        return (Source: source, Count: 0L);
                    }
                })
                .ToList());

        var totalCount = sourcesAndCounts.Sum(source => source.Count);

        var randomIndex = Random.Shared.NextInt64(totalCount);

        foreach (var sourceAndCount in sourcesAndCounts)
        {
            if (randomIndex < sourceAndCount.Count)
            {
                return sourceAndCount.Source;
            }

            randomIndex -= sourceAndCount.Count;
        }

        return default;
    }
    
    public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source) => source.OrderBy(_ => Random.Shared.Next());
}