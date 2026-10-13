namespace Together.Api.Data;

public sealed class VariantEntity
{
    public Guid Id
    {
        get; set;
    }
    public Guid TripId
    {
        get; set;
    }
    public TripEntity Trip
    {
        get; set;
    } = null!;
    public string Name
    {
        get; set;
    } = "";
    public string Destination
    {
        get; set;
    } = "";
    public string Accommodation
    {
        get; set;
    } = "";
    public string SourceUrl
    {
        get; set;
    } = "";
    public string RoadDescription
    {
        get; set;
    } = "";
    public string Notes
    {
        get; set;
    } = "";
    public int? TravelMinutes
    {
        get; set;
    }
    public int? Transfers
    {
        get; set;
    }
    public string Kitchen
    {
        get; set;
    } = "unknown";
    public string Crib
    {
        get; set;
    } = "unknown";
    public string Playground
    {
        get; set;
    } = "unknown";
    public int? DistanceMeters
    {
        get; set;
    }
    public string DistanceTarget
    {
        get; set;
    } = "";
    public bool NeedsBudgetReview
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
    public ICollection<VariantExpenseEntity> Expenses
    {
        get;
    } = [];
}

public sealed class VariantExpenseEntity
{
    public Guid VariantId
    {
        get; set;
    }
    public VariantEntity Variant
    {
        get; set;
    } = null!;
    public string Category
    {
        get; set;
    } = "";
    public long? AmountKopecks
    {
        get; set;
    }
}
