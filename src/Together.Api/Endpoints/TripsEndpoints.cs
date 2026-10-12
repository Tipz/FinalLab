using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Together.Api.Data;
using Together.Contracts;
using Together.Core;

namespace Together.Api.Endpoints;

public static class TripsEndpoints
{
    public static RouteGroupBuilder MapTripsApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(ApiRoutes.Trips).RequireAuthorization();
        group.MapGet("/", GetTrips);
        group.MapGet("/{id:guid}", GetTrip);
        group.MapPost("/", CreateTrip);
        group.MapPut("/{id:guid}", UpdateTrip);
        group.MapDelete("/{id:guid}", DeleteTrip);
        group.MapPost("/{tripId:guid}/variants", CreateVariant);
        group.MapPut("/{tripId:guid}/variants/{id:guid}", UpdateVariant);
        group.MapDelete("/{tripId:guid}/variants/{id:guid}", DeleteVariant);
        group.MapPut("/{id:guid}/comparison", UpdateComparison);
        return group;
    }

    private static async Task<IResult> GetTrips(ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();
        var trips = await Graph(db).Where(x => x.OwnerId == userId)
            .OrderByDescending(x => x.UpdatedAt).ToListAsync(cancellationToken);
        return Results.Ok(trips.Select(ToResponse));
    }

    private static async Task<IResult> GetTrip(Guid id, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();
        var trip = await Graph(db).SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == userId, cancellationToken);
        return trip is null ? Results.NotFound() : Results.Ok(ToResponse(trip));
    }

    private static async Task<IResult> CreateTrip(TripWriteRequest request, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();
        var errors = ValidateTrip(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);
        if (request.ExpectedRevision != 0)
            return Conflict("Новая поездка должна иметь revision 0.");

        var now = DateTimeOffset.UtcNow;
        var trip = new TripEntity
        {
            Id = request.Id is { } requestedId && requestedId != Guid.Empty ? requestedId : Guid.NewGuid(),
            OwnerId = userId,
            Name = request.Name.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Adults = request.Adults,
            BudgetLimitKopecks = request.BudgetLimitKopecks,
            ReservePercent = request.ReservePercent,
            CreatedAt = now,
            UpdatedAt = now,
            Revision = 1
        };
        AddChildren(trip, request.ChildAges);
        for (var i = 0; i < Catalog.Criteria.Length; i++)
            trip.SelectedCriteria.Add(new SelectedCriterionEntity { Criterion = Catalog.Criteria[i], Position = i });
        db.Trips.Add(trip);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict("Поездка с таким идентификатором уже существует.");
        }
        return Results.Created($"{ApiRoutes.Trips}/{trip.Id}", ToResponse(trip));
    }

    private static async Task<IResult> UpdateTrip(Guid id, TripWriteRequest request, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();
        var errors = ValidateTrip(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);
        var trip = await Graph(db).SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == userId, cancellationToken);
        if (trip is null)
            return Results.NotFound();
        if (trip.Revision != request.ExpectedRevision)
            return Conflict();

        var familyChanged = trip.StartDate != request.StartDate || trip.EndDate != request.EndDate ||
            trip.Adults != request.Adults || !trip.Children.OrderBy(x => x.Position).Select(x => x.Age).SequenceEqual(request.ChildAges);
        trip.Name = request.Name.Trim();
        trip.StartDate = request.StartDate;
        trip.EndDate = request.EndDate;
        trip.Adults = request.Adults;
        trip.BudgetLimitKopecks = request.BudgetLimitKopecks;
        trip.ReservePercent = request.ReservePercent;
        db.TripChildren.RemoveRange(trip.Children);
        AddChildren(trip, request.ChildAges);
        if (familyChanged)
            foreach (var variant in trip.Variants)
            {
                variant.NeedsBudgetReview = true;
                variant.Revision++;
                variant.UpdatedAt = DateTimeOffset.UtcNow;
            }
        Touch(trip);
        return await SaveTrip(db, trip, cancellationToken);
    }

    private static async Task<IResult> DeleteTrip(Guid id, long expectedRevision, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();
        var trip = await db.Trips.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == userId, cancellationToken);
        if (trip is null)
            return Results.NotFound();
        if (trip.Revision != expectedRevision)
            return Conflict();
        db.Trips.Remove(trip);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict();
        }
    }

    private static async Task<IResult> CreateVariant(Guid tripId, VariantWriteRequest request, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();
        var errors = ValidateVariant(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);
        var trip = await Graph(db).SingleOrDefaultAsync(x => x.Id == tripId && x.OwnerId == userId, cancellationToken);
        if (trip is null)
            return Results.NotFound();
        if (trip.Revision != request.ExpectedTripRevision || request.ExpectedRevision != 0)
            return Conflict();

        var now = DateTimeOffset.UtcNow;
        var variant = new VariantEntity
        {
            Id = request.Id is { } requestedId && requestedId != Guid.Empty ? requestedId : Guid.NewGuid(),
            TripId = tripId,
            CreatedAt = now,
            UpdatedAt = now,
            Revision = 1
        };
        Apply(variant, request);
        AddExpenses(variant, request.Expenses);
        trip.Variants.Add(variant);
        db.Variants.Add(variant);
        Touch(trip);
        var saved = await SaveTrip(db, trip, cancellationToken);
        return saved is Microsoft.AspNetCore.Http.HttpResults.Ok<TripResponse>
            ? Results.Created($"{ApiRoutes.Trips}/{tripId}/variants/{variant.Id}", ToResponse(trip))
            : saved;
    }

    private static async Task<IResult> UpdateVariant(Guid tripId, Guid id, VariantWriteRequest request, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();
        var errors = ValidateVariant(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);
        var trip = await Graph(db).SingleOrDefaultAsync(x => x.Id == tripId && x.OwnerId == userId, cancellationToken);
        if (trip is null)
            return Results.NotFound();
        var variant = trip.Variants.SingleOrDefault(x => x.Id == id);
        if (variant is null)
            return Results.NotFound();
        if (trip.Revision != request.ExpectedTripRevision || variant.Revision != request.ExpectedRevision)
            return Conflict();

        Apply(variant, request);
        db.VariantExpenses.RemoveRange(variant.Expenses);
        AddExpenses(variant, request.Expenses);
        variant.Revision++;
        variant.UpdatedAt = DateTimeOffset.UtcNow;
        Touch(trip);
        return await SaveTrip(db, trip, cancellationToken);
    }

    private static async Task<IResult> DeleteVariant(Guid tripId, Guid id, long expectedTripRevision, long expectedRevision, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();
        var trip = await Graph(db).SingleOrDefaultAsync(x => x.Id == tripId && x.OwnerId == userId, cancellationToken);
        if (trip is null)
            return Results.NotFound();
        var variant = trip.Variants.SingleOrDefault(x => x.Id == id);
        if (variant is null)
            return Results.NotFound();
        if (trip.Revision != expectedTripRevision || variant.Revision != expectedRevision)
            return Conflict();
        db.Variants.Remove(variant);
        Touch(trip);
        return await SaveTrip(db, trip, cancellationToken);
    }

    private static async Task<IResult> UpdateComparison(Guid id, ComparisonWriteRequest request, ClaimsPrincipal principal, ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
            return Results.Unauthorized();
        var trip = await Graph(db).SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == userId, cancellationToken);
        if (trip is null)
            return Results.NotFound();
        if (trip.Revision != request.ExpectedRevision)
            return Conflict();
        var errors = ValidateComparison(trip, request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        db.SelectedVariants.RemoveRange(trip.SelectedVariants);
        db.SelectedCriteria.RemoveRange(trip.SelectedCriteria);
        for (var i = 0; i < request.SelectedVariantIds.Length; i++)
            trip.SelectedVariants.Add(new SelectedVariantEntity { VariantId = request.SelectedVariantIds[i], Position = i });
        for (var i = 0; i < request.SelectedCriteria.Length; i++)
            trip.SelectedCriteria.Add(new SelectedCriterionEntity { Criterion = request.SelectedCriteria[i], Position = i });
        Touch(trip);
        return await SaveTrip(db, trip, cancellationToken);
    }

    private static IQueryable<TripEntity> Graph(ApplicationDbContext db) => db.Trips
        .Include(x => x.Children)
        .Include(x => x.SelectedCriteria)
        .Include(x => x.SelectedVariants)
        .Include(x => x.Variants).ThenInclude(x => x.Expenses)
        .AsSplitQuery();

    private static TripResponse ToResponse(TripEntity trip) => new(
        trip.Id, trip.Name, trip.StartDate, trip.EndDate, trip.Adults,
        trip.Children.OrderBy(x => x.Position).Select(x => x.Age).ToArray(),
        trip.SelectedVariants.OrderBy(x => x.Position).Select(x => x.VariantId).ToArray(),
        trip.SelectedCriteria.OrderBy(x => x.Position).Select(x => x.Criterion).ToArray(),
        trip.Variants.OrderBy(x => x.CreatedAt).Select(ToResponse).ToArray(),
        trip.CreatedAt, trip.UpdatedAt, trip.Revision, trip.BudgetLimitKopecks, trip.ReservePercent);

    private static VariantResponse ToResponse(VariantEntity variant)
    {
        var expenses = Catalog.ExpenseKeys.Select(key => variant.Expenses.SingleOrDefault(x => x.Category == key)?.AmountKopecks).ToArray();
        return new VariantResponse(variant.Id, variant.TripId, variant.Name, variant.Destination, variant.Accommodation,
            variant.SourceUrl, variant.RoadDescription, variant.Notes, variant.TravelMinutes, variant.Transfers,
            variant.Kitchen, variant.Crib, variant.Playground, variant.DistanceMeters, variant.DistanceTarget,
            expenses, variant.NeedsBudgetReview, variant.CreatedAt, variant.UpdatedAt, variant.Revision);
    }

    private static Dictionary<string, string[]> ValidateTrip(TripWriteRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!DataValidation.Text(request.Name?.Trim(), 100))
            errors["name"] = ["Укажите название, от 1 до 100 символов."];
        if (request.StartDate == default)
            errors["startDate"] = ["Укажите дату начала."];
        if (request.EndDate <= request.StartDate)
            errors["endDate"] = ["Окончание должно быть позже начала."];
        if (request.Adults is < 1 or > 10)
            errors["adults"] = ["Укажите число от 1 до 10."];
        if (request.BudgetLimitKopecks is < 0 or > 10000000000L)
            errors["budgetLimitKopecks"] = ["Укажите сумму от 0 до 100 000 000 ₽ либо null."];
        if (request.ReservePercent is < 0 or > 100)
            errors["reservePercent"] = ["Укажите целый процент от 0 до 100."];
        if (request.ChildAges is null || request.ChildAges.Length is < 1 or > 10 || request.ChildAges.Any(x => x is < 0 or > 17))
            errors["childAges"] = ["Нужно от 1 до 10 возрастов от 0 до 17 лет."];
        if (request.ExpectedRevision < 0)
            errors["expectedRevision"] = ["Revision не может быть отрицательным."];
        return errors;
    }

    private static Dictionary<string, string[]> ValidateVariant(VariantWriteRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!DataValidation.Text(request.Name?.Trim(), 150))
            errors["name"] = ["Обязательное поле, до 150 символов."];
        if (!DataValidation.Text(request.Destination?.Trim(), 150))
            errors["destination"] = ["Обязательное поле, до 150 символов."];
        if (!DataValidation.Text(request.Accommodation?.Trim(), 150))
            errors["accommodation"] = ["Обязательное поле, до 150 символов."];
        if (!DataValidation.Url(request.SourceUrl?.Trim()))
            errors["sourceUrl"] = ["Укажите абсолютную ссылку http или https."];
        if (!DataValidation.Text(request.RoadDescription, 2000, false))
            errors["roadDescription"] = ["Не более 2000 символов."];
        if (!DataValidation.Text(request.Notes, 2000, false))
            errors["notes"] = ["Не более 2000 символов."];
        if (request.TravelMinutes is < 0 or > 100000)
            errors["travelMinutes"] = ["Укажите значение от 0 до 100 000."];
        if (request.Transfers is < 0 or > 20)
            errors["transfers"] = ["Укажите значение от 0 до 20."];
        if (request.DistanceMeters is < 0 or > 1000000 || !DataValidation.Text(request.DistanceTarget, 100, request.DistanceMeters.HasValue) || (request.DistanceMeters is null && request.DistanceTarget.Length > 0))
            errors["distance"] = ["Расстояние и цель должны быть заполнены вместе."];
        if (new[] { request.Kitchen, request.Crib, request.Playground }.Any(x => x is not ("yes" or "no" or "unknown")))
            errors["amenities"] = ["Допустимы yes, no или unknown."];
        if (request.Expenses is null || request.Expenses.Length != Catalog.ExpenseKeys.Length || request.Expenses.Any(x => x is < 0 or > 10000000000L))
            errors["expenses"] = ["Передайте шесть сумм от 0 до 100 000 000 ₽ либо null."];
        if (request.ExpectedRevision < 0 || request.ExpectedTripRevision < 0)
            errors["revision"] = ["Revision не может быть отрицательным."];
        return errors;
    }

    private static Dictionary<string, string[]> ValidateComparison(TripEntity trip, ComparisonWriteRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.SelectedVariantIds.Distinct().Count() != request.SelectedVariantIds.Length || request.SelectedVariantIds.Any(id => trip.Variants.All(x => x.Id != id)))
            errors["selectedVariantIds"] = ["Выбор содержит дубликат или вариант другой поездки."];
        if (request.SelectedCriteria.Distinct().Count() != request.SelectedCriteria.Length || request.SelectedCriteria.Any(x => !Catalog.Criteria.Contains(x)))
            errors["selectedCriteria"] = ["Передан неизвестный или повторяющийся критерий."];
        return errors;
    }

    private static void Apply(VariantEntity variant, VariantWriteRequest request)
    {
        variant.Name = request.Name.Trim();
        variant.Destination = request.Destination.Trim();
        variant.Accommodation = request.Accommodation.Trim();
        variant.SourceUrl = request.SourceUrl.Trim();
        variant.RoadDescription = request.RoadDescription.Trim();
        variant.Notes = request.Notes.Trim();
        variant.TravelMinutes = request.TravelMinutes;
        variant.Transfers = request.Transfers;
        variant.Kitchen = request.Kitchen;
        variant.Crib = request.Crib;
        variant.Playground = request.Playground;
        variant.DistanceMeters = request.DistanceMeters;
        variant.DistanceTarget = request.DistanceTarget.Trim();
        if (request.BudgetReviewed)
            variant.NeedsBudgetReview = false;
    }

    private static void AddChildren(TripEntity trip, int[] ages)
    {
        for (var i = 0; i < ages.Length; i++)
            trip.Children.Add(new TripChildEntity { Position = i, Age = ages[i] });
    }

    private static void AddExpenses(VariantEntity variant, long?[] expenses)
    {
        for (var i = 0; i < Catalog.ExpenseKeys.Length; i++)
            variant.Expenses.Add(new VariantExpenseEntity { Category = Catalog.ExpenseKeys[i], AmountKopecks = expenses[i] });
    }

    private static void Touch(TripEntity trip)
    {
        trip.Revision++;
        trip.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static async Task<IResult> SaveTrip(ApplicationDbContext db, TripEntity trip, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToResponse(trip));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict();
        }
    }

    private static IResult Conflict(string detail = "Данные уже изменились. Загрузите актуальную версию.") =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Конфликт версии", detail: detail);

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
