using System.Net;
using System.Net.Http.Json;
using Together.Contracts;

namespace Together.Api.Tests;

public sealed class TripsApiTests(TogetherApiFactory factory) : IClassFixture<TogetherApiFactory>
{
    [Fact]
    public async Task Trips_RequireAuthentication()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(ApiRoutes.Trips);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task User_CanCreateReadAndDetectConflict_WhileOtherUserCannotRead()
    {
        using var owner = factory.CreateClient();
        await RegisterAndLogin(owner, $"owner-{Guid.NewGuid():N}@example.test");
        var request = new TripWriteRequest
        {
            Name = "Летняя поездка",
            StartDate = new DateOnly(2027, 7, 1),
            EndDate = new DateOnly(2027, 7, 8),
            Adults = 2,
            ChildAges = [4]
        };
        using var createdResponse = await SendWithAntiforgery(owner, HttpMethod.Post, ApiRoutes.Trips, request);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<TripResponse>();
        Assert.NotNull(created);
        Assert.Equal(1, created.Revision);

        var variantRequest = new VariantWriteRequest
        {
            ExpectedTripRevision = created.Revision,
            Name = "Отель у моря",
            Destination = "Сочи",
            Accommodation = "Семейный номер",
            SourceUrl = "https://example.test/hotel",
            TravelMinutes = 120,
            Transfers = 0,
            Kitchen = "yes",
            Crib = "unknown",
            Playground = "no",
            DistanceMeters = 500,
            DistanceTarget = "море",
            Expenses = [100_000, 0, null, 25_000, null, 5_000]
        };
        using var variantResponse = await SendWithAntiforgery(
            owner, HttpMethod.Post, $"{ApiRoutes.Trips}/{created.Id}/variants", variantRequest);
        Assert.Equal(HttpStatusCode.Created, variantResponse.StatusCode);
        var withVariant = await variantResponse.Content.ReadFromJsonAsync<TripResponse>();
        Assert.NotNull(withVariant);
        Assert.Equal(2, withVariant.Revision);
        Assert.Equal(variantRequest.Expenses, Assert.Single(withVariant.Variants).Expenses);

        using var conflictResponse = await SendWithAntiforgery(
            owner, HttpMethod.Put, $"{ApiRoutes.Trips}/{created.Id}", request);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);

        using var other = factory.CreateClient();
        await RegisterAndLogin(other, $"other-{Guid.NewGuid():N}@example.test");
        using var hiddenResponse = await other.GetAsync($"{ApiRoutes.Trips}/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, hiddenResponse.StatusCode);
    }

    [Fact]
    public async Task Comparison_CanReplaceReorderAndClearCriteria_WithoutLosingRevisionProtection()
    {
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, $"comparison-{Guid.NewGuid():N}@example.test");
        using var createdResponse = await SendWithAntiforgery(client, HttpMethod.Post, ApiRoutes.Trips, new TripWriteRequest
        {
            Name = "Вымышленная поездка для сравнения",
            StartDate = new(2027, 7, 1),
            EndDate = new(2027, 7, 8),
            Adults = 2,
            ChildAges = [4]
        });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<TripResponse>())!;
        var current = created;
        string[][] criteriaSets = [["travel", "kitchen"], ["kitchen", "budget", "distance"], [], Together.Core.Catalog.Criteria];
        foreach (var criteria in criteriaSets)
        {
            using var response = await SendWithAntiforgery(client, HttpMethod.Put, $"{ApiRoutes.Trips}/{created.Id}/comparison", new ComparisonWriteRequest
            {
                ExpectedRevision = current.Revision,
                SelectedCriteria = criteria
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var updated = (await response.Content.ReadFromJsonAsync<TripResponse>())!;
            Assert.Equal(current.Revision + 1, updated.Revision);
            Assert.Equal(criteria, updated.SelectedCriteria);
            current = (await client.GetFromJsonAsync<TripResponse>($"{ApiRoutes.Trips}/{created.Id}"))!;
            Assert.Equal(criteria, current.SelectedCriteria);
        }
        using var stale = await SendWithAntiforgery(client, HttpMethod.Put, $"{ApiRoutes.Trips}/{created.Id}/comparison", new ComparisonWriteRequest
        {
            ExpectedRevision = created.Revision,
            SelectedCriteria = []
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var saved = (await client.GetFromJsonAsync<TripResponse>($"{ApiRoutes.Trips}/{created.Id}"))!;
        Assert.Equal(current.Revision, saved.Revision);
        Assert.Equal(current.SelectedCriteria, saved.SelectedCriteria);
    }

    [Fact]
    public async Task InvalidTrip_ReturnsValidationProblem()
    {
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, $"validation-{Guid.NewGuid():N}@example.test");
        using var response = await SendWithAntiforgery(client, HttpMethod.Post, ApiRoutes.Trips, new TripWriteRequest());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StateChangingRequest_RequiresValidAntiforgeryToken()
    {
        using var client = factory.CreateClient();
        using var missing = await client.PostAsJsonAsync(
            $"{ApiRoutes.Auth}/register",
            new
            {
                email = $"csrf-{Guid.NewGuid():N}@example.test",
                password = "correct horse battery staple"
            });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        using var invalidRequest = new HttpRequestMessage(HttpMethod.Post, $"{ApiRoutes.Auth}/register")
        {
            Content = JsonContent.Create(new { email = $"csrf-{Guid.NewGuid():N}@example.test", password = "correct horse battery staple" })
        };
        invalidRequest.Headers.Add("X-XSRF-TOKEN", "invalid");
        using var invalid = await client.SendAsync(invalidRequest);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private static async Task RegisterAndLogin(HttpClient client, string email)
    {
        const string password = "correct horse battery staple";
        using var register = await SendWithAntiforgery(client, HttpMethod.Post, $"{ApiRoutes.Auth}/register", new
        {
            email,
            password
        });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        using var login = await SendWithAntiforgery(client, HttpMethod.Post, $"{ApiRoutes.Auth}/login?useCookies=true", new
        {
            email,
            password
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task BudgetSettingsPersistAndConflictDoesNotOverwriteThem()
    {
        using var client = factory.CreateClient();
        var email = $"budget-{Guid.NewGuid():N}@example.test";
        await RegisterAndLogin(client, email);
        var request = new TripWriteRequest
        {
            Name = "Бюджетная поездка",
            StartDate = new(2027, 7, 1),
            EndDate = new(2027, 7, 8),
            Adults = 2,
            ChildAges = [4],
            BudgetLimitKopecks = 15000000,
            ReservePercent = 10
        };
        using var response = await SendWithAntiforgery(client, HttpMethod.Post, ApiRoutes.Trips, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<TripResponse>())!;
        Assert.Equal(15000000, created.BudgetLimitKopecks);
        Assert.Equal(10, created.ReservePercent);
        var variantRequest = new VariantWriteRequest
        {
            ExpectedTripRevision = created.Revision,
            Name = "Домик",
            Destination = "Лес",
            Accommodation = "Дом",
            Expenses = [1, 0, 0, 0, 0, 0]
        };
        using var variantResponse = await SendWithAntiforgery(client, HttpMethod.Post, $"{ApiRoutes.Trips}/{created.Id}/variants", variantRequest);
        Assert.Equal(HttpStatusCode.Created, variantResponse.StatusCode);
        var withVariant = (await variantResponse.Content.ReadFromJsonAsync<TripResponse>())!;
        var update = new TripWriteRequest
        {
            Name = request.Name,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Adults = 2,
            ChildAges = [4],
            ExpectedRevision = withVariant.Revision,
            BudgetLimitKopecks = 0,
            ReservePercent = 100
        };
        using var updatedResponse = await SendWithAntiforgery(client, HttpMethod.Put, $"{ApiRoutes.Trips}/{created.Id}", update);
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        using var staleResponse = await SendWithAntiforgery(client, HttpMethod.Put, $"{ApiRoutes.Trips}/{created.Id}", update);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        var saved = (await client.GetFromJsonAsync<TripResponse>($"{ApiRoutes.Trips}/{created.Id}"))!;
        Assert.Equal(0, saved.BudgetLimitKopecks);
        Assert.Equal(100, saved.ReservePercent);
        Assert.Equal(variantRequest.Expenses, Assert.Single(saved.Variants).Expenses);
        Assert.False(saved.Variants[0].NeedsBudgetReview);
        using var logout = await SendWithAntiforgery(client, HttpMethod.Post, $"{ApiRoutes.Auth}/logout", new
        {
        });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var login = await SendWithAntiforgery(client, HttpMethod.Post, $"{ApiRoutes.Auth}/login?useCookies=true",
            new
            {
                email,
                password = "correct horse battery staple"
            });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var restored = (await client.GetFromJsonAsync<TripResponse>($"{ApiRoutes.Trips}/{created.Id}"))!;
        Assert.Equal(0, restored.BudgetLimitKopecks);
        Assert.Equal(100, restored.ReservePercent);
        await RegisterAndLogin(client, $"budget-unused-{Guid.NewGuid():N}@example.test");
        using var denied = await SendWithAntiforgery(client, HttpMethod.Put, $"{ApiRoutes.Trips}/{created.Id}", update);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Theory]
    [InlineData(-1L, 0)]
    [InlineData(10000000001L, 0)]
    [InlineData(null, -1)]
    [InlineData(null, 101)]
    public async Task InvalidBudgetSettingsReturnValidationProblem(long? limit, int reserve)
    {
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, $"budget-invalid-{Guid.NewGuid():N}@example.test");
        using var response = await SendWithAntiforgery(client, HttpMethod.Post, ApiRoutes.Trips, new TripWriteRequest
        {
            Name = "Лето",
            StartDate = new(2027, 7, 1),
            EndDate = new(2027, 7, 8),
            Adults = 2,
            ChildAges = [4],
            BudgetLimitKopecks = limit,
            ReservePercent = reserve
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    internal static async Task<HttpResponseMessage> SendWithAntiforgery<T>(HttpClient client, HttpMethod method, string uri, T value)
    {
        var token = await client.GetFromJsonAsync<AntiforgeryTokenResponse>(ApiRoutes.Antiforgery);
        Assert.NotNull(token);
        var request = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(value) };
        request.Headers.Add("X-XSRF-TOKEN", token.Token);
        return await client.SendAsync(request);
    }
}
