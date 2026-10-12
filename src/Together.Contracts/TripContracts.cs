namespace Together.Contracts;

public sealed record TripResponse(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    int Adults,
    int[] ChildAges,
    Guid[] SelectedVariantIds,
    string[] SelectedCriteria,
    VariantResponse[] Variants,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision,
    long? BudgetLimitKopecks = null,
    int ReservePercent = 0);

public sealed record VariantResponse(
    Guid Id,
    Guid TripId,
    string Name,
    string Destination,
    string Accommodation,
    string SourceUrl,
    string RoadDescription,
    string Notes,
    int? TravelMinutes,
    int? Transfers,
    string Kitchen,
    string Crib,
    string Playground,
    int? DistanceMeters,
    string DistanceTarget,
    long?[] Expenses,
    bool NeedsBudgetReview,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision);

public sealed class TripWriteRequest
{
    public long? BudgetLimitKopecks
    {
        get; init;
    }
    public int ReservePercent
    {
        get; init;
    }
    public Guid? Id
    {
        get; init;
    }
    public long ExpectedRevision
    {
        get; init;
    }
    public string Name { get; init; } = "";
    public DateOnly StartDate
    {
        get; init;
    }
    public DateOnly EndDate
    {
        get; init;
    }
    public int Adults
    {
        get; init;
    }
    public int[] ChildAges { get; init; } = [];
}

public sealed class VariantWriteRequest
{
    public Guid? Id
    {
        get; init;
    }
    public long ExpectedTripRevision
    {
        get; init;
    }
    public long ExpectedRevision
    {
        get; init;
    }
    public string Name { get; init; } = "";
    public string Destination { get; init; } = "";
    public string Accommodation { get; init; } = "";
    public string SourceUrl { get; init; } = "";
    public string RoadDescription { get; init; } = "";
    public string Notes { get; init; } = "";
    public int? TravelMinutes
    {
        get; init;
    }
    public int? Transfers
    {
        get; init;
    }
    public string Kitchen { get; init; } = "unknown";
    public string Crib { get; init; } = "unknown";
    public string Playground { get; init; } = "unknown";
    public int? DistanceMeters
    {
        get; init;
    }
    public string DistanceTarget { get; init; } = "";
    public long?[] Expenses { get; init; } = new long?[6];
    public bool BudgetReviewed
    {
        get; init;
    }
}

public sealed class ComparisonWriteRequest
{
    public long ExpectedRevision
    {
        get; init;
    }
    public Guid[] SelectedVariantIds { get; init; } = [];
    public string[] SelectedCriteria { get; init; } = [];
}
