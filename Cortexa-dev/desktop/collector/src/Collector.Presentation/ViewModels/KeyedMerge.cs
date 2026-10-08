namespace Collector.Presentation.ViewModels;

public sealed class KeyedMerge<TRow, TModel>(
    Func<TRow, string> rowKey,
    Func<TModel, string> modelKey,
    Func<TModel, TRow> create,
    Action<TRow, TModel> update)
{
    public Func<IList<TRow>, TModel, int>? InsertIndex { get; init; }

    public void Apply(IList<TRow> rows, IEnumerable<TModel> models)
    {
        var incoming = Distinct(models);
        RemoveMissing(rows, incoming);
        var existing = rows.ToDictionary(rowKey, StringComparer.Ordinal);
        foreach (var (key, model) in incoming)
        {
            if (existing.TryGetValue(key, out var row))
            {
                update(row, model);
                continue;
            }

            var created = create(model);
            rows.Insert(InsertIndex?.Invoke(rows, model) ?? rows.Count, created);
        }
    }

    private List<(string Key, TModel Model)> Distinct(IEnumerable<TModel> models)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return [.. models.Select(model => (Key: modelKey(model), Model: model)).Where(pair => seen.Add(pair.Key))];
    }

    private void RemoveMissing(IList<TRow> rows, List<(string Key, TModel Model)> incoming)
    {
        var keys = incoming.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        for (var index = rows.Count - 1; index >= 0; index--)
        {
            if (!keys.Contains(rowKey(rows[index])))
            {
                rows.RemoveAt(index);
            }
        }
    }
}
