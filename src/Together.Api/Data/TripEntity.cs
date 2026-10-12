namespace Together.Api.Data;

public sealed class TripEntity
{
    public Guid Id
    {
        get; set;
    }
    public Guid OwnerId
    {
        get; set;
    }
    public ApplicationUser Owner { get; set; } = null!;
    public string Name { get; set; } = "";
    public long? BudgetLimitKopecks
    {
        get; set;
    }
    public int ReservePercent
    {
        get; set;
    }
    public DateOnly StartDate
    {
        get; set;
    }
    public DateOnly EndDate
    {
        get; set;
    }
    public int Adults
    {
        get; set;
    }
    public DateTimeOffset CreatedAt
    {
        get; set;
    }
    public DateTimeOffset UpdatedAt
    {
        get; set;
    }
    public long Revision
    {
        get; set;
    }
    public ICollection<TripChildEntity> Children { get; } = [];
    public ICollection<VariantEntity> Variants { get; } = [];
    public ICollection<SelectedVariantEntity> SelectedVariants { get; } = [];
    public ICollection<SelectedCriterionEntity> SelectedCriteria { get; } = [];
}

public sealed class TripChildEntity
{
    public Guid TripId
    {
        get; set;
    }
    public TripEntity Trip { get; set; } = null!;
    public int Position
    {
        get; set;
    }
    public int Age
    {
        get; set;
    }
}

public sealed class SelectedVariantEntity
{
    public Guid TripId
    {
        get; set;
    }
    public TripEntity Trip { get; set; } = null!;
    public Guid VariantId
    {
        get; set;
    }
    public VariantEntity Variant { get; set; } = null!;
    public int Position
    {
        get; set;
    }
}

public sealed class SelectedCriterionEntity
{
    public Guid TripId
    {
        get; set;
    }
    public TripEntity Trip { get; set; } = null!;
    public string Criterion { get; set; } = "";
    public int Position
    {
        get; set;
    }
}
