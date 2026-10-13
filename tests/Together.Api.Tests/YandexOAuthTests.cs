using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Together.Contracts;

namespace Together.Api.Tests;

public sealed class YandexOAuthTests(TogetherApiFactory factory) : IClassFixture<TogetherApiFactory>
{
    [Fact]
    public async Task SuccessfulAndRepeatedLogin_UseSameUser()
    {
        using var client = factory.CreateClient();
        using var first = await client.GetAsync($"{ApiRoutes.Auth}/yandex?returnUrl=%2F");
        Assert.Contains("oauthResult=yandex", first.RequestMessage?.RequestUri?.Query);
        var firstUser = await client.GetFromJsonAsync<CurrentUserResponse>($"{ApiRoutes.Auth}/me");
        Assert.NotNull(firstUser);

        using var logout = await TripsApiTests.SendWithAntiforgery(
            client, HttpMethod.Post, $"{ApiRoutes.Auth}/logout", new
            {
            });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var repeated = await client.GetAsync($"{ApiRoutes.Auth}/yandex?returnUrl=%2F");
        Assert.Contains("oauthResult=yandex", repeated.RequestMessage?.RequestUri?.Query);
        var repeatedUser = await client.GetFromJsonAsync<CurrentUserResponse>($"{ApiRoutes.Auth}/me");
        Assert.Equal(firstUser.Id, repeatedUser?.Id);
    }

    [Fact]
    public async Task Cancellation_IsReportedWithoutAuthentication()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"{ApiRoutes.Auth}/yandex?returnUrl=%2F&scenario=cancel");

        Assert.Contains("oauthError=cancelled", response.RequestMessage?.RequestUri?.Query);
        using var me = await client.GetAsync($"{ApiRoutes.Auth}/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task ExistingPasswordEmail_IsNotLinkedToExternalLogin()
    {
        using var client = factory.CreateClient();
        var email = $"existing-{Guid.NewGuid():N}@example.test";
        const string password = "correct horse battery staple";
        using var register = await TripsApiTests.SendWithAntiforgery(
            client, HttpMethod.Post, $"{ApiRoutes.Auth}/register", new
            {
                email,
                password
            });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        using var response = await client.GetAsync(
            $"{ApiRoutes.Auth}/yandex?returnUrl=%2F&providerKey=different&email={Uri.EscapeDataString(email)}");
        Assert.Contains("oauthError=email_conflict", response.RequestMessage?.RequestUri?.Query);

        using var login = await TripsApiTests.SendWithAntiforgery(
            client, HttpMethod.Post, $"{ApiRoutes.Auth}/login?useCookies=true", new
            {
                email,
                password
            });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Theory]
    [InlineData("https://example.test/")]
    [InlineData("//example.test/")]
    [InlineData("/\\example.test/")]
    public async Task ExternalReturnUrl_IsRejected(string returnUrl)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync(
            $"{ApiRoutes.Auth}/yandex?returnUrl={Uri.EscapeDataString(returnUrl)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
