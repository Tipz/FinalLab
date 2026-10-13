using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Together.Api.Authentication;
using Together.Api.Data;

namespace Together.Api.Tests;

public sealed class TogetherApiFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"together-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var databaseRegistrations = services.Where(descriptor =>
                descriptor.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                descriptor.ServiceType.FullName?.Contains("IDbContextOptionsConfiguration", StringComparison.Ordinal) == true).ToArray();
            foreach (var descriptor in databaseRegistrations)
                services.Remove(descriptor);
            services.RemoveAll<ApplicationDbContext>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));
            services.RemoveAll<YandexProviderState>();
            services.AddSingleton(new YandexProviderState(true, FakeYandexHandler.SchemeName));
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, FakeYandexHandler>(
                FakeYandexHandler.SchemeName,
                _ => { });
        });
    }
}

public sealed class FakeYandexHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestYandex";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Request.Query["scenario"] == "cancel")
        {
            Response.Redirect("/login?oauthError=cancelled");
            return;
        }

        var providerKey = Request.Query["providerKey"].FirstOrDefault() ?? "fake-yandex-user";
        var email = Request.Query["email"].FirstOrDefault() ?? "yandex-user@example.test";
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, providerKey),
            new Claim(ClaimTypes.Email, email)
        ], SchemeName);
        await Context.SignInAsync(
            IdentityConstants.ExternalScheme,
            new ClaimsPrincipal(identity),
            properties);
        Response.Redirect(properties.RedirectUri ?? "/api/auth/yandex/callback?returnUrl=%2F");
    }
}
