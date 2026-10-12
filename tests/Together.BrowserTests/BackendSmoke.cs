using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Together.BrowserTests;

public static class BackendSmoke
{
    public static async Task<int> RunAsync(IPlaywright playwright, string baseUrl, string evidence)
    {
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new()
            {
                Width = 1440,
                Height = 1000
            },
            Locale = "ru-RU"
        });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        page.Console += (_, message) =>
        {
            // Anonymous probes return 401; budget scenarios deliberately exercise 409 and 503.
            if (message.Type == "error" && !new[] { "401", "409", "503" }.Any(code => message.Text.Contains(code, StringComparison.Ordinal)))
                errors.Add(message.Text);
        };

        try
        {
            await page.GotoAsync(baseUrl);
            await Expect(page.GetByRole(AriaRole.Heading, new()
            {
                Name = "Вход",
                Exact = true
            }))
                .ToBeVisibleAsync(new()
                {
                    Timeout = 60_000
                });
            await page.GetByRole(AriaRole.Button, new()
            {
                Name = "Создать аккаунт",
                Exact = true
            }).ClickAsync();
            await page.GetByLabel("Email").FillAsync($"browser-{Guid.NewGuid():N}@example.test");
            await page.GetByLabel("Пароль").FillAsync("correct horse battery staple");
            await page.GetByRole(AriaRole.Button, new()
            {
                Name = "Создать аккаунт",
                Exact = true
            }).ClickAsync();

            await Expect(page.GetByRole(AriaRole.Heading, new()
            {
                Name = "Все варианты. Все расходы."
            }))
                .ToBeVisibleAsync(new()
                {
                    Timeout = 60_000
                });
            await Expect(page.Locator(".variant-card")).ToHaveCountAsync(3, new()
            {
                Timeout = 60_000
            });
            await page.ReloadAsync();
            await Expect(page.Locator(".variant-card")).ToHaveCountAsync(3, new()
            {
                Timeout = 60_000
            });
            await BudgetPlanningSmoke.RunAsync(page, context, baseUrl, evidence);
            await page.ScreenshotAsync(new()
            {
                Path = Path.Combine(evidence, "backend-chromium.png"),
                FullPage = true
            });
            await page.GetByRole(AriaRole.Button, new()
            {
                Name = "Выйти",
                Exact = true
            }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new()
            {
                Name = "Вход",
                Exact = true
            }))
                .ToBeVisibleAsync(new()
                {
                    Timeout = 60_000
                });

            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

            await WriteResult(evidence, browser.Version, true, null);
            Console.WriteLine("PASS backend-smoke: registration, budget/reserve persistence, clarifications, failed write, real conflict, mobile/keyboard and logout");
            return 0;
        }
        catch (Exception ex)
        {
            await page.ScreenshotAsync(new()
            {
                Path = Path.Combine(evidence, "backend-chromium-failure.png"),
                FullPage = true
            });
            var diagnostics = errors.Count == 0
                ? ex.Message
                : $"{ex.Message}{Environment.NewLine}Browser errors:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}";
            await WriteResult(evidence, browser.Version, false, diagnostics);
            Console.WriteLine($"FAIL backend-smoke: {diagnostics}");
            return 1;
        }
    }

    private static Task WriteResult(string evidence, string version, bool passed, string? error) =>
        File.WriteAllTextAsync(
            Path.Combine(evidence, "backend-browser-results.json"),
            JsonSerializer.Serialize(new
            {
                engine = "Chromium",
                version,
                passed,
                error
            },
                new JsonSerializerOptions { WriteIndented = true }));
}
