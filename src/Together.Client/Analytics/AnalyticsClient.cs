using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using Together.Client.Storage;

namespace Together.Client.Analytics;

public sealed class AnalyticsClient(IJSRuntime js, AuthClient auth, NavigationManager navigation) : IDisposable
{
    private const string LoginSucceeded = "login_succeeded";
    private const string TripCreated = "trip_created";
    private long? counterId;
    private bool initialized;

    public AnalyticsConsentState Consent { get; private set; } = AnalyticsConsentState.Unavailable;
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (initialized)
            return;
        initialized = true;
        counterId = (await auth.SettingsAsync()).YandexMetrikaCounterId;
        if (counterId is null)
            return;

        var stored = await js.InvokeAsync<string?>("togetherAnalytics.getConsent");
        Consent = stored switch
        {
            "granted" => AnalyticsConsentState.Granted,
            "denied" => AnalyticsConsentState.Denied,
            _ => AnalyticsConsentState.Unknown
        };
        if (Consent == AnalyticsConsentState.Granted)
            await EnableSafelyAsync();
        navigation.LocationChanged += OnLocationChanged;
        Changed?.Invoke();
    }

    public async Task GrantAsync()
    {
        if (counterId is null)
            return;
        Consent = AnalyticsConsentState.Granted;
        await js.InvokeVoidAsync("togetherAnalytics.setConsent", "granted");
        await EnableSafelyAsync();
        Changed?.Invoke();
    }

    public async Task DenyAsync()
    {
        if (counterId is null)
            return;
        Consent = AnalyticsConsentState.Denied;
        await js.InvokeVoidAsync("togetherAnalytics.setConsent", "denied");
        await js.InvokeVoidAsync("togetherAnalytics.disable");
        Changed?.Invoke();
    }

    public Task LoginSucceededAsync(string method) =>
        method is "password" or "yandex"
            ? TrackGoalAsync(LoginSucceeded, method)
            : Task.CompletedTask;

    public Task TripCreatedAsync() => TrackGoalAsync(TripCreated, null);

    public async Task<bool> TryTrackGoalAsync(string goal, string? value = null)
    {
        if (goal == LoginSucceeded && value is "password" or "yandex")
        {
            await TrackGoalAsync(goal, value);
            return true;
        }
        if (goal == TripCreated && value is null)
        {
            await TrackGoalAsync(goal, null);
            return true;
        }
        return false;
    }

    public async Task TrackPageAsync(string uri)
    {
        if (Consent != AnalyticsConsentState.Granted || counterId is null)
            return;
        await js.InvokeVoidAsync("togetherAnalytics.hit", SanitizePath(uri));
    }

    public static string SanitizePath(string uri)
    {
        string clean;
        if (Uri.TryCreate(uri, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            clean = absolute.AbsolutePath;
        else
            clean = uri.Split('?', '#')[0];
        if (string.IsNullOrEmpty(clean))
            return "/";
        clean = clean.StartsWith('/') ? clean : $"/{clean}";
        var segments = clean.Trim('/').Split('/');
        if (segments.Length == 3 && segments[0] == "trips" && Guid.TryParse(segments[1], out _)
            && segments[2] is "variants" or "comparison")
            return $"/trips/{segments[2]}";
        return clean;
    }

    private async Task TrackGoalAsync(string goal, string? value)
    {
        if (Consent != AnalyticsConsentState.Granted || counterId is null)
            return;
        await js.InvokeVoidAsync("togetherAnalytics.goal", goal, value);
    }

    private async Task EnableSafelyAsync()
    {
        try
        {
            await js.InvokeVoidAsync("togetherAnalytics.enable", counterId!.Value);
            await TrackPageAsync(navigation.Uri);
        }
        catch (JSException)
        {
            // Consent remains recorded, but analytics availability never affects the application.
        }
    }

    private async void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        try
        {
            await TrackPageAsync(args.Location);
        }
        catch (JSException)
        {
            // Analytics must never interrupt navigation.
        }
    }

    public void Dispose()
    {
        navigation.LocationChanged -= OnLocationChanged;
    }
}

public enum AnalyticsConsentState
{
    Unavailable,
    Unknown,
    Granted,
    Denied
}
