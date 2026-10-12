namespace Together.Core;

public sealed record VariantClarification(string FieldId, string Message);

public static class VariantClarifications
{
    public static IReadOnlyList<VariantClarification> For(Variant variant, IEnumerable<string> selectedCriteria)
    {
        var items = new List<VariantClarification>();
        for (var i = 0; i < Catalog.ExpenseKeys.Length; i++)
            if (variant.Expenses[i] is null)
                items.Add(new($"expense-{i}", $"Уточните расходы: {Catalog.ExpenseLabels[i]}"));
        if (variant.NeedsBudgetReview)
            items.Add(new("budget-reviewed", Catalog.ReviewNotice));

        var criteria = selectedCriteria.ToHashSet();
        if (criteria.Contains("travel") && variant.TravelMinutes is null)
            items.Add(new("hours", "Уточните время в пути"));
        if (criteria.Contains("transfers") && variant.Transfers is null)
            items.Add(new("transfers", "Уточните количество пересадок"));
        if (criteria.Contains("kitchen") && variant.Kitchen == "unknown")
            items.Add(new("kitchen", "Уточните наличие кухни"));
        if (criteria.Contains("crib") && variant.Crib == "unknown")
            items.Add(new("crib", "Уточните наличие детской кроватки"));
        if (criteria.Contains("playground") && variant.Playground == "unknown")
            items.Add(new("playground", "Уточните наличие детской площадки"));
        if (criteria.Contains("distance") && variant.DistanceMeters is null)
            items.Add(new("distance", "Уточните расстояние до нужного места"));
        return items;
    }
}
