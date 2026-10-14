using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Together.Client.Analytics;
using Together.Client.Storage;
using Together.Contracts;

namespace Together.Tests;

public sealed class AnalyticsTests : BunitContext
{
    private readonly AnalyticsClient analytics;

    public AnalyticsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new HttpClient(new SettingsHandler()) { BaseAddress = new Uri("https://app.example.test/") });
        Services.AddScoped<ApiHttp>();
        Services.AddScoped<AuthClient>();
        Services.AddScoped<AnalyticsClient>();
        analytics = Services.GetRequiredService<AnalyticsClient>();
    }

    [Fact]
    public async Task TagAndTraffic_AreAbsentBeforeConsent()
    {
        await analytics.InitializeAsync();
        await analytics.TrackPageAsync("https://app.example.test/trips?private=value#fragment");

        Assert.DoesNotContain(JSInterop.Invocations, x => x.Identifier is "togetherAnalytics.enable" or "togetherAnalytics.hit");
        Assert.Equal(AnalyticsConsentState.Unknown, analytics.Consent);
    }

    [Fact]
    public async Task Consent_AllowsSanitizedViewsAndFixedGoals_AndRevocationStopsThem()
    {
        await analytics.InitializeAsync();
        await analytics.GrantAsync();
        await analytics.TrackPageAsync("https://app.example.test/trips?private=value#fragment");
        var accepted = await analytics.TryTrackGoalAsync("login_succeeded", "yandex");
        var rejectedGoal = await analytics.TryTrackGoalAsync("arbitrary", null);
        var rejectedParameter = await analytics.TryTrackGoalAsync("login_succeeded", "email@example.test");

        Assert.True(accepted);
        Assert.False(rejectedGoal);
        Assert.False(rejectedParameter);
        Assert.Contains(JSInterop.Invocations, x => x.Identifier == "togetherAnalytics.enable");
        Assert.Contains(JSInterop.Invocations, x => x.Identifier == "togetherAnalytics.hit"
            && x.Arguments.Count > 0 && Equals(x.Arguments[0], "/trips"));
        Assert.Contains(JSInterop.Invocations, x => x.Identifier == "togetherAnalytics.goal"
            && Equals(x.Arguments[0], "login_succeeded") && Equals(x.Arguments[1], "yandex"));

        await analytics.DenyAsync();
        var hitCount = JSInterop.Invocations.Count(x => x.Identifier == "togetherAnalytics.hit");
        await analytics.TrackPageAsync("https://app.example.test/after-revoke?secret=1");
        Assert.Equal(hitCount, JSInterop.Invocations.Count(x => x.Identifier == "togetherAnalytics.hit"));
    }

    [Theory]
    [InlineData("https://app.example.test/path?email=x#token", "/path")]
    [InlineData("/local?query=1#fragment", "/local")]
    [InlineData("local?query=1", "/local")]
    [InlineData("?query=1#fragment", "/")]
    [InlineData("/#fragment", "/")]
    [InlineData("https://app.example.test/trips/ce89a611-b84d-4892-801a-c284f0503269/variants?private=value", "/trips/variants")]
    [InlineData("/trips/ce89a611-b84d-4892-801a-c284f0503269/comparison#options", "/trips/comparison")]
    public void SanitizePath_RemovesQueryAndFragment(string input, string expected) =>
        Assert.Equal(expected, AnalyticsClient.SanitizePath(input));

    private sealed class SettingsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PublicSettingsResponse(false, 123456))
            });
    }
}
