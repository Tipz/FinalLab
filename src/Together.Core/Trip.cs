using System.Text.Json.Serialization;

namespace Together.Core;

public sealed class Trip
{
    [JsonRequired] public Guid Id { get; set; } = Guid.NewGuid();
    [JsonRequired] public string Name { get; set; } = "";
    [JsonRequired]
    public DateOnly StartDate
    {
        get; set;
    }
    [JsonRequired]
    public DateOnly EndDate
    {
        get; set;
    }
    [JsonRequired] public int Adults { get; set; } = 2;
    public long? BudgetLimitKopecks { get; set; }
    public int ReservePercent { get; set; }
    [JsonRequired] public int[] ChildAges { get; set; } = [0];
    [JsonRequired] public List<Guid> SelectedVariantIds { get; set; } = [];
    [JsonRequired] public List<string> SelectedCriteria { get; set; } = [.. Catalog.Criteria];
    [JsonRequired] public List<Variant> Variants { get; set; } = [];
    [JsonRequired] public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonRequired] public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [JsonRequired]
    public long Revision
    {
        get; set;
    }
    [JsonIgnore] public int Nights => EndDate.DayNumber - StartDate.DayNumber;
    public Trip Copy(bool copyVariants = true)
    {
        var copy = (Trip)MemberwiseClone();
        copy.ChildAges = [.. ChildAges];
        copy.SelectedVariantIds = [.. SelectedVariantIds];
        copy.SelectedCriteria = [.. SelectedCriteria];
        copy.Variants = copyVariants ? Variants.Select(v => v.Copy()).ToList() : [.. Variants];
        return copy;
    }
}
