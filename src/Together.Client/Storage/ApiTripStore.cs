using System.Net;
using System.Net.Http.Json;
using Together.Contracts;
using Together.Core;
using TripVariant = Together.Core.Variant;

namespace Together.Client.Storage;

public sealed class ApiTripStore(ApiHttp api)
{
    private Workspace snapshot = new();

    public async Task<Workspace> ReadAsync()
    {
        using var response = await api.SendAsync(new HttpRequestMessage(HttpMethod.Get, ApiRoutes.Trips));
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new AuthenticationRequiredException();
        response.EnsureSuccessStatusCode();
        var trips = await response.Content.ReadFromJsonAsync<TripResponse[]>() ?? [];
        snapshot = new Workspace { Trips = trips.Select(ToCore).ToList() };
        return snapshot.Copy();
    }

    public async Task<string> WriteAsync(Workspace next, long expectedRevision, Guid tripId, bool comparisonOnly = false)
    {
        var current = snapshot.Trips.SingleOrDefault(x => x.Id == tripId);
        var changed = next.Trips.Single(x => x.Id == tripId);
        HttpResponseMessage response;
        if (current is null)
            response = await CreateTrip(changed);
        else if (comparisonOnly)
            response = await Put($"{ApiRoutes.Trips}/{tripId}/comparison", new ComparisonWriteRequest
            {
                ExpectedRevision = current.Revision,
                SelectedVariantIds = [.. changed.SelectedVariantIds],
                SelectedCriteria = [.. changed.SelectedCriteria]
            });
        else if (TripFieldsChanged(current, changed))
            response = await Put($"{ApiRoutes.Trips}/{tripId}", ToRequest(changed, current.Revision));
        else
            response = await WriteVariant(current, changed);

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new AuthenticationRequiredException();
            if (response.StatusCode == HttpStatusCode.Conflict)
                return "conflict";
            if (response.StatusCode == HttpStatusCode.BadRequest)
                return "invalid";
            if (!response.IsSuccessStatusCode)
                return "write";
            var saved = await response.Content.ReadFromJsonAsync<TripResponse>();
            if (saved is null)
                return "write";
            Replace(next, ToCore(saved));
            next.Revision = expectedRevision + 1;
            snapshot = next.Copy();
            return "ok";
        }
    }

    private async Task<HttpResponseMessage> CreateTrip(Trip trip)
    {
        var response = await Post(ApiRoutes.Trips, ToRequest(trip, 0));
        if (!response.IsSuccessStatusCode || trip.Variants.Count == 0)
            return response;
        var saved = await response.Content.ReadFromJsonAsync<TripResponse>();
        response.Dispose();
        if (saved is null)
            return new HttpResponseMessage(HttpStatusCode.BadGateway);
        foreach (var variant in trip.Variants.OrderBy(x => x.CreatedAt))
        {
            response = await Post($"{ApiRoutes.Trips}/{trip.Id}/variants", ToRequest(variant, saved.Revision, 0));
            if (!response.IsSuccessStatusCode)
                return response;
            saved = await response.Content.ReadFromJsonAsync<TripResponse>();
            response.Dispose();
            if (saved is null)
                return new HttpResponseMessage(HttpStatusCode.BadGateway);
        }
        if (trip.SelectedVariantIds.Count > 0 || !trip.SelectedCriteria.SequenceEqual(saved.SelectedCriteria))
        {
            response = await Put($"{ApiRoutes.Trips}/{trip.Id}/comparison", new ComparisonWriteRequest
            {
                ExpectedRevision = saved.Revision,
                SelectedVariantIds = [.. trip.SelectedVariantIds],
                SelectedCriteria = [.. trip.SelectedCriteria]
            });
            if (!response.IsSuccessStatusCode)
                return response;
            saved = await response.Content.ReadFromJsonAsync<TripResponse>();
            response.Dispose();
            if (saved is null)
                return new HttpResponseMessage(HttpStatusCode.BadGateway);
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(saved) };
    }

    private async Task<HttpResponseMessage> WriteVariant(Trip current, Trip changed)
    {
        var added = changed.Variants.SingleOrDefault(x => current.Variants.All(y => y.Id != x.Id));
        if (added is not null)
            return await Post($"{ApiRoutes.Trips}/{changed.Id}/variants", ToRequest(added, current.Revision, 0));
        var removed = current.Variants.SingleOrDefault(x => changed.Variants.All(y => y.Id != x.Id));
        if (removed is not null)
            return await Send(new HttpRequestMessage(HttpMethod.Delete,
                $"{ApiRoutes.Trips}/{changed.Id}/variants/{removed.Id}?expectedTripRevision={current.Revision}&expectedRevision={removed.Revision}"));
        var modified = changed.Variants.SingleOrDefault(x =>
        {
            var old = current.Variants.Single(y => y.Id == x.Id);
            return !VariantEqual(old, x);
        });
        return modified is null
            ? new HttpResponseMessage(HttpStatusCode.BadRequest)
            : await Put($"{ApiRoutes.Trips}/{changed.Id}/variants/{modified.Id}",
                ToRequest(modified, current.Revision, current.Variants.Single(x => x.Id == modified.Id).Revision));
    }

    private Task<HttpResponseMessage> Post<T>(string uri, T value) => Send(new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(value) });
    private Task<HttpResponseMessage> Put<T>(string uri, T value) => Send(new HttpRequestMessage(HttpMethod.Put, uri) { Content = JsonContent.Create(value) });
    private Task<HttpResponseMessage> Send(HttpRequestMessage request) => api.SendAsync(request);

    private static TripWriteRequest ToRequest(Trip trip, long revision) => new()
    {
        Id = trip.Id,
        ExpectedRevision = revision,
        Name = trip.Name,
        StartDate = trip.StartDate,
        EndDate = trip.EndDate,
        Adults = trip.Adults,
        BudgetLimitKopecks = trip.BudgetLimitKopecks,
        ReservePercent = trip.ReservePercent,
        ChildAges = [.. trip.ChildAges]
    };

    private static VariantWriteRequest ToRequest(TripVariant variant, long tripRevision, long revision) => new()
    {
        Id = variant.Id,
        ExpectedTripRevision = tripRevision,
        ExpectedRevision = revision,
        Name = variant.Name,
        Destination = variant.Destination,
        Accommodation = variant.Accommodation,
        SourceUrl = variant.SourceUrl,
        RoadDescription = variant.RoadDescription,
        Notes = variant.Notes,
        TravelMinutes = variant.TravelMinutes,
        Transfers = variant.Transfers,
        Kitchen = variant.Kitchen,
        Crib = variant.Crib,
        Playground = variant.Playground,
        DistanceMeters = variant.DistanceMeters,
        DistanceTarget = variant.DistanceTarget,
        Expenses = [.. variant.Expenses],
        BudgetReviewed = !variant.NeedsBudgetReview
    };

    private static Trip ToCore(TripResponse source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        StartDate = source.StartDate,
        EndDate = source.EndDate,
        Adults = source.Adults,
        BudgetLimitKopecks = source.BudgetLimitKopecks,
        ReservePercent = source.ReservePercent,
        ChildAges = source.ChildAges,
        SelectedVariantIds = [.. source.SelectedVariantIds],
        SelectedCriteria = [.. source.SelectedCriteria],
        Variants = source.Variants.Select(ToCore).ToList(),
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        Revision = source.Revision
    };

    private static TripVariant ToCore(VariantResponse source) => new()
    {
        Id = source.Id,
        TripId = source.TripId,
        Name = source.Name,
        Destination = source.Destination,
        Accommodation = source.Accommodation,
        SourceUrl = source.SourceUrl,
        RoadDescription = source.RoadDescription,
        Notes = source.Notes,
        TravelMinutes = source.TravelMinutes,
        Transfers = source.Transfers,
        Kitchen = source.Kitchen,
        Crib = source.Crib,
        Playground = source.Playground,
        DistanceMeters = source.DistanceMeters,
        DistanceTarget = source.DistanceTarget,
        Expenses = source.Expenses,
        NeedsBudgetReview = source.NeedsBudgetReview,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        Revision = source.Revision
    };

    private static bool TripFieldsChanged(Trip x, Trip y) => x.Name != y.Name || x.StartDate != y.StartDate ||
        x.EndDate != y.EndDate || x.Adults != y.Adults || !x.ChildAges.SequenceEqual(y.ChildAges) ||
        x.BudgetLimitKopecks != y.BudgetLimitKopecks || x.ReservePercent != y.ReservePercent;

    private static bool VariantEqual(TripVariant x, TripVariant y) => x.Name == y.Name && x.Destination == y.Destination &&
        x.Accommodation == y.Accommodation && x.SourceUrl == y.SourceUrl && x.RoadDescription == y.RoadDescription &&
        x.Notes == y.Notes && x.TravelMinutes == y.TravelMinutes && x.Transfers == y.Transfers && x.Kitchen == y.Kitchen &&
        x.Crib == y.Crib && x.Playground == y.Playground && x.DistanceMeters == y.DistanceMeters &&
        x.DistanceTarget == y.DistanceTarget && x.Expenses.SequenceEqual(y.Expenses) && x.NeedsBudgetReview == y.NeedsBudgetReview;

    private static void Replace(Workspace workspace, Trip trip)
    {
        var index = workspace.Trips.FindIndex(x => x.Id == trip.Id);
        if (index < 0)
            workspace.Trips.Add(trip);
        else
            workspace.Trips[index] = trip;
    }
}
