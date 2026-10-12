namespace Together.Core;

public static class DataValidation
{
    public static bool Text(string? value, int max, bool required = true) => value is not null && value.Length <= max && (!required || value.Trim().Length > 0);
    public static bool Url(string? value) => value is not null && (value == "" || (value.Length <= 2048 && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0));
    public static bool IsValid(Workspace data)
    {
        if (data.SchemaVersion != 1 || data.Revision < 0 || data.Trips is null || data.Trips.Any(t => t is null))
            return false;
        if (data.Trips.Select(t => t.Id).Distinct().Count() != data.Trips.Count)
            return false;
        var ids = new HashSet<Guid>();
        foreach (var t in data.Trips)
        {
            if (t.Id == Guid.Empty || !Text(t.Name, 100) || t.StartDate == default || t.EndDate <= t.StartDate || t.Adults is < 1 or > 10 ||
                t.ChildAges is null || t.ChildAges.Length is < 1 or > 10 || t.ChildAges.Any(x => x is < 0 or > 17) ||
                t.BudgetLimitKopecks is < 0 or > 10000000000L || t.ReservePercent is < 0 or > 100 ||
                t.Revision < 0 || t.CreatedAt == default || t.UpdatedAt < t.CreatedAt || t.Variants is null || t.SelectedVariantIds is null || t.SelectedCriteria is null)
                return false;
            foreach (var v in t.Variants)
            {
                if (v is null || !ids.Add(v.Id) || v.Id == Guid.Empty || v.TripId != t.Id || !Text(v.Name, 150) || !Text(v.Destination, 150) || !Text(v.Accommodation, 150) ||
                    !Url(v.SourceUrl) || !Text(v.RoadDescription, 2000, false) || !Text(v.Notes, 2000, false) ||
                    v.TravelMinutes is < 0 or > 100000 || v.Transfers is < 0 or > 20 || v.DistanceMeters is < 0 or > 1000000 ||
                    !Text(v.DistanceTarget, 100, v.DistanceMeters.HasValue) || (v.DistanceMeters is null && v.DistanceTarget.Length > 0) ||
                    !new[] { "yes", "no", "unknown" }.Contains(v.Kitchen) || !new[] { "yes", "no", "unknown" }.Contains(v.Crib) || !new[] { "yes", "no", "unknown" }.Contains(v.Playground) ||
                    v.Expenses is null || v.Expenses.Length != 6 || v.Expenses.Any(x => x is < 0 or > 10000000000L) ||
                    v.Revision < 0 || v.CreatedAt == default || v.UpdatedAt < v.CreatedAt)
                    return false;
            }
            if (t.SelectedCriteria.Any(c => !Catalog.Criteria.Contains(c)) || t.SelectedCriteria.Distinct().Count() != t.SelectedCriteria.Count ||
                t.SelectedVariantIds.Distinct().Count() != t.SelectedVariantIds.Count || t.SelectedVariantIds.Any(id => !t.Variants.Any(v => v.Id == id)))
                return false;
        }
        return true;
    }
}
