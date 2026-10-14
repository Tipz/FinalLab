using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Together.Client;
using Together.Client.Analytics;
using Together.Client.Storage;
using Together.Contracts;
using Together.Core;

namespace Together.Tests;

public sealed class ScreenNavigationTests : BunitContext, IAsyncLifetime
{
    private readonly Workspace workspace = new();
    private readonly NavigationManager navigation;
    private readonly WorkspaceHandler handler;

    public ScreenNavigationTests()
    {
        for (var i = 0; i < 2; i++)
        {
            var trip = ExampleData.Create().Trips.Single();
            trip.Name = $"Вымышленная поездка {i + 1}";
            trip.SelectedVariantIds = trip.Variants.Take(2).Select(v => v.Id).ToList();
            workspace.Trips.Add(trip);
        }
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./js/storage.js").Setup<string?>("read").SetResult(null);
        handler = new WorkspaceHandler(workspace);
        Services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        Services.AddScoped<ApiHttp>();
        Services.AddScoped<ApiTripStore>();
        Services.AddScoped<AuthClient>();
        Services.AddScoped<BrowserStore>();
        Services.AddScoped<AnalyticsClient>();
        navigation = Services.GetRequiredService<NavigationManager>();
    }

    public Task InitializeAsync() => Task.CompletedTask;
    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    [Fact]
    public async Task ThreeScreensPreserveSelectionAndCriteriaSeparatelyForEachTrip()
    {
        var cut = Render<App>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".trip-card").Count));
        Assert.Equal("Мои поездки", cut.Find("h1").TextContent);
        Assert.Empty(cut.FindAll(".variant-card,table"));

        var trip = workspace.Trips[1];
        await cut.InvokeAsync(() => navigation.NavigateTo($"/trips/{trip.Id}/variants"));
        cut.WaitForAssertion(() => Assert.Equal("Варианты поездки", cut.Find("h1").TextContent));
        Assert.Equal(trip.Id.ToString(), cut.Find("#trip-select").GetAttribute("value"));
        Assert.Equal(3, cut.FindAll(".variant-card").Count);
        Assert.Empty(cut.FindAll("table"));
        cut.FindAll(".select-variant input")[2].Change(true);
        cut.WaitForAssertion(() => Assert.Equal(3, trip.SelectedVariantIds.Count));
        cut.WaitForAssertion(() => Assert.Equal("Сохранено", cut.Find(".feedback").TextContent));

        await cut.InvokeAsync(() => navigation.NavigateTo($"/trips/{trip.Id}/comparison"));
        cut.WaitForAssertion(() => Assert.Equal("Сравнение", cut.Find("h1").TextContent));
        Assert.Equal(4, cut.FindAll("thead th").Count);
        var kitchen = cut.FindAll(".filters input")[3];
        kitchen.Change(false);
        cut.WaitForAssertion(() => Assert.Equal(6, cut.FindAll("tbody tr").Count));

        await cut.InvokeAsync(() => navigation.NavigateTo("/"));
        cut.WaitForAssertion(() => Assert.Equal("Мои поездки", cut.Find("h1").TextContent));
        await cut.InvokeAsync(() => navigation.NavigateTo($"/trips/{workspace.Trips[0].Id}/comparison"));
        cut.WaitForAssertion(() => Assert.Equal(7, cut.FindAll("tbody tr").Count));
        Assert.Equal(3, cut.FindAll("thead th").Count);
        await cut.InvokeAsync(() => navigation.NavigateTo($"/trips/{trip.Id}/comparison"));
        cut.WaitForAssertion(() => Assert.Equal(6, cut.FindAll("tbody tr").Count));
        Assert.Equal(4, cut.FindAll("thead th").Count);
        Assert.Single(cut.FindAll("nav [aria-current=page]"));
    }

    [Theory]
    [InlineData("variants", "Варианты поездки", 3)]
    [InlineData("comparison", "Сравнение", 2)]
    public void DirectLinkLoadsRequestedTripAndScreen(string screen, string heading, int cards)
    {
        var trip = workspace.Trips[1];
        navigation.NavigateTo($"/trips/{trip.Id}/{screen}");
        var cut = Render<App>();
        cut.WaitForAssertion(() => Assert.Equal(cards, cut.FindAll(".variant-card").Count));
        Assert.Equal(heading, cut.Find("h1").TextContent);
        Assert.Equal(trip.Id.ToString(), cut.Find("#trip-select").GetAttribute("value"));
        Assert.Equal(screen == "comparison" ? 1 : 0, cut.FindAll("table").Count);
    }

    [Fact]
    public void UnknownTripDoesNotOpenAnotherTrip()
    {
        navigation.NavigateTo($"/trips/{Guid.NewGuid()}/comparison");
        var cut = Render<App>();
        cut.WaitForAssertion(() => Assert.Contains("Поездка недоступна", cut.Markup));
        Assert.Empty(cut.FindAll(".variant-card,table,#trip-select"));
    }

    [Fact]
    public async Task NavigationKeepsEditorDraftUntilDiscardIsConfirmed()
    {
        var trip = workspace.Trips[0];
        navigation.NavigateTo($"/trips/{trip.Id}/variants");
        var cut = Render<App>();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".variant-card").Count));
        cut.Find(".trip-toolbar button").Click();
        cut.Find("#name").Input("Несохранённый черновик");
        var confirmation = JSInterop.Setup<bool>("confirm", _ => true);
        confirmation.SetResult(false);
        await cut.InvokeAsync(() => navigation.NavigateTo("/"));
        cut.WaitForAssertion(() => Assert.Equal("Несохранённый черновик", cut.Find("#name").GetAttribute("value")));
        Assert.Equal("Варианты поездки", cut.Find("h1").TextContent);
        Assert.EndsWith("/variants", navigation.Uri);
        confirmation.SetResult(true);
        await cut.InvokeAsync(() => navigation.NavigateTo("/"));
        cut.WaitForAssertion(() => Assert.Equal("Мои поездки", cut.Find("h1").TextContent));
        Assert.Empty(cut.FindAll("dialog"));
    }

    [Fact]
    public void ExpiredSessionCanRedirectToLoginDuringAWrite()
    {
        navigation.NavigateTo($"/trips/{workspace.Trips[0].Id}/variants");
        var cut = Render<App>();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".variant-card").Count));
        handler.RejectWrites = true;
        cut.Find(".select-variant input").Change(false);
        cut.WaitForAssertion(() => Assert.Equal("Вход", cut.Find("h1").TextContent));
        Assert.EndsWith("/login", navigation.Uri);
    }

    private sealed class WorkspaceHandler(Workspace workspace) : HttpMessageHandler
    {
        public bool RejectWrites
        {
            get; set;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == ApiRoutes.PublicSettings)
                return Json(new PublicSettingsResponse(false, null));
            if (path == ApiRoutes.Antiforgery)
                return Json(new AntiforgeryTokenResponse("test-token"));
            if (path == ApiRoutes.Trips && request.Method == HttpMethod.Get)
                return Json(workspace.Trips);
            if (RejectWrites && request.Method == HttpMethod.Put)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            if (request.Method == HttpMethod.Put && path.EndsWith("/comparison", StringComparison.Ordinal))
            {
                var id = Guid.Parse(path.Split('/')[3]);
                var trip = workspace.Trips.Single(t => t.Id == id);
                var settings = (await request.Content!.ReadFromJsonAsync<ComparisonWriteRequest>(cancellationToken))!;
                trip.SelectedVariantIds = [.. settings.SelectedVariantIds];
                trip.SelectedCriteria = [.. settings.SelectedCriteria];
                trip.Revision++;
                return Json(trip);
            }
            throw new InvalidOperationException($"Unexpected test request: {request.Method} {path}");
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
