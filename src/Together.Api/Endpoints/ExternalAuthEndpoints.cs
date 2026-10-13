using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Together.Api.Authentication;
using Together.Api.Data;
using Together.Contracts;

namespace Together.Api.Endpoints;

public static class ExternalAuthEndpoints
{
    public static IEndpointRouteBuilder MapExternalAuth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet($"{ApiRoutes.Auth}/yandex", StartYandexLogin)
            .RequireRateLimiting("auth")
            .AllowAnonymous();
        endpoints.MapGet($"{ApiRoutes.Auth}/yandex/callback", CompleteYandexLogin)
            .RequireRateLimiting("auth")
            .AllowAnonymous();
        return endpoints;
    }

    private static IResult StartYandexLogin(
        string? returnUrl,
        YandexProviderState provider,
        SignInManager<ApplicationUser> signInManager)
    {
        if (!provider.Enabled)
            return Results.NotFound();
        if (!TryLocalPath(returnUrl, out var localReturnUrl))
            return Results.BadRequest(new
            {
                error = "invalid_return_url"
            });

        var callback = $"{ApiRoutes.Auth}/yandex/callback?returnUrl={Uri.EscapeDataString(localReturnUrl)}";
        return Results.Challenge(
            signInManager.ConfigureExternalAuthenticationProperties(provider.Scheme, callback),
            [provider.Scheme]);
    }

    private static async Task<IResult> CompleteYandexLogin(
        string? returnUrl,
        YandexProviderState provider,
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        HttpContext context)
    {
        if (!provider.Enabled)
            return Results.NotFound();
        if (!TryLocalPath(returnUrl, out var localReturnUrl))
            return Results.Redirect("/login?oauthError=invalid_return_url");

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null || !string.Equals(info.LoginProvider, provider.Scheme, StringComparison.Ordinal))
            return Results.Redirect("/login?oauthError=provider");

        try
        {
            var existingLogin = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (existingLogin is not null)
            {
                await signInManager.SignInAsync(existingLogin, isPersistent: false);
                return Success(localReturnUrl);
            }

            var email = info.Principal.FindFirstValue(ClaimTypes.Email)?.Trim();
            if (string.IsNullOrWhiteSpace(email))
                return Results.Redirect("/login?oauthError=email_missing");

            if (await userManager.FindByEmailAsync(email) is not null)
                return Results.Redirect("/login?oauthError=email_conflict");

            var user = new ApplicationUser { UserName = email, Email = email };
            var created = await userManager.CreateAsync(user);
            if (!created.Succeeded)
                return Results.Redirect("/login?oauthError=account");

            var loginAdded = await userManager.AddLoginAsync(user, info);
            if (!loginAdded.Succeeded)
            {
                await userManager.DeleteAsync(user);
                return Results.Redirect("/login?oauthError=account");
            }

            await signInManager.SignInAsync(user, isPersistent: false);
            return Success(localReturnUrl);
        }
        finally
        {
            await context.SignOutAsync(IdentityConstants.ExternalScheme);
        }
    }

    private static IResult Success(string returnUrl) => Results.Redirect(
        $"/login?oauthResult=yandex&returnUrl={Uri.EscapeDataString(returnUrl)}");

    internal static bool TryLocalPath(string? value, out string path)
    {
        path = string.IsNullOrWhiteSpace(value) ? "/" : value;
        return path.StartsWith("/", StringComparison.Ordinal)
            && !path.StartsWith("//", StringComparison.Ordinal)
            && !path.StartsWith("/\\", StringComparison.Ordinal)
            && !path.Any(char.IsControl)
            && Uri.TryCreate(path, UriKind.Relative, out _);
    }
}
