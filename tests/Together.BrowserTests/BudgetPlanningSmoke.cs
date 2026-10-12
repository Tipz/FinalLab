using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Together.BrowserTests;

public static class BudgetPlanningSmoke
{
    public static async Task RunAsync(IPage page, IBrowserContext context, string baseUrl, string evidence)
    {
        var tripSettings = page.Locator(".trip-budget-settings");
        var editTrip = page.GetByRole(AriaRole.Button, new()
        {
            Name = "Параметры поездки",
            Exact = true
        });
        var save = page.GetByRole(AriaRole.Button, new()
        {
            Name = "Сохранить",
            Exact = true
        });
        await editTrip.ClickAsync();
        await page.Locator("#budget-limit").FillAsync("150000");
        await page.Locator("#reserve-percent").FillAsync("10");
        await save.ClickAsync();
        await Expect(tripSettings).ToContainTextAsync("150 000 ₽");
        await Expect(tripSettings).ToContainTextAsync("10%");

        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Добавить вариант",
            Exact = true
        }).ClickAsync();
        await page.Locator("#name").FillAsync("Проверка бюджета");
        await page.Locator("#destination").FillAsync("Вымышленный город");
        await page.Locator("#accommodation").FillAsync("Семейный дом");
        for (var i = 0; i < 6; i++)
            if (i != 2)
                await page.Locator($"#expense-{i}").FillAsync(i == 0 ? "98000" : "0");
        await save.ClickAsync();
        var card = page.Locator("article[aria-label='Проверка бюджета']");
        await Expect(card).ToContainTextAsync("Известные расходы");
        await Expect(card).ToContainTextAsync("Итог может превысить лимит");
        await card.Locator(".clarifications summary").ClickAsync();
        await card.GetByRole(AriaRole.Button, new()
        {
            Name = "Заполнить: Уточните расходы: Питание",
            Exact = true
        }).ClickAsync();
        await Expect(page.Locator("#expense-2")).ToBeFocusedAsync();
        await page.Locator("#expense-2").FillAsync("32000");
        await save.ClickAsync();
        await Expect(card).ToContainTextAsync("143 000 ₽");
        await Expect(card).ToContainTextAsync("До лимита осталось — 7 000 ₽");
        await Expect(card).Not.ToContainTextAsync("Уточните расходы: Питание");

        // A failed server response must preserve the draft and the last saved settings.
        await editTrip.ClickAsync();
        await page.Locator("#budget-limit").FillAsync("160000");
        await page.RouteAsync("**/api/trips/*", async route =>
        {
            if (route.Request.Method == "PUT")
                await route.FulfillAsync(new()
                {
                    Status = 503,
                    ContentType = "application/problem+json",
                    Body = "{\"status\":503}"
                });
            else
                await route.ContinueAsync();
        });
        await save.ClickAsync();
        await Expect(page.GetByText("Не удалось сохранить данные на сервере", new()
        {
            Exact = true
        })).ToBeVisibleAsync();
        await Expect(page.Locator("#budget-limit")).ToHaveValueAsync("160000");
        await Expect(tripSettings).ToContainTextAsync("150 000 ₽");
        await page.UnrouteAsync("**/api/trips/*");
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Повторить",
            Exact = true
        }).ClickAsync();
        await Expect(tripSettings).ToContainTextAsync("160 000 ₽");

        // Change the actual PostgreSQL row via a second request while the editor holds an old revision.
        await editTrip.ClickAsync();
        await page.Locator("#budget-limit").FillAsync("180000");
        var tripId = await page.Locator("#trip-select").InputValueAsync();
        await UpdateBudget(context, baseUrl, tripId, 17000000);
        await save.ClickAsync();
        await Expect(page.GetByText("Данные изменились в другой вкладке.", new()
        {
            Exact = false
        })).ToBeVisibleAsync();
        await Expect(page.Locator("#budget-limit")).ToHaveValueAsync("180000");
        var reload = page.GetByRole(AriaRole.Button, new()
        {
            Name = "Загрузить актуальные данные",
            Exact = true
        });
        EventHandler<IDialog> dismiss = async (_, dialog) => await dialog.DismissAsync();
        page.Dialog += dismiss;
        await reload.ClickAsync();
        await Expect(page.Locator("#budget-limit")).ToHaveValueAsync("180000");
        page.Dialog -= dismiss;
        EventHandler<IDialog> accept = async (_, dialog) => await dialog.AcceptAsync();
        page.Dialog += accept;
        await reload.ClickAsync();
        await Expect(page.Locator("dialog[open]")).ToHaveCountAsync(0);
        page.Dialog -= accept;
        await Expect(tripSettings).ToContainTextAsync("170 000 ₽");

        await page.ReloadAsync();
        await Expect(tripSettings).ToContainTextAsync("170 000 ₽");
        await Expect(card).ToContainTextAsync("До лимита осталось — 27 000 ₽");
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(evidence, "budget-desktop.png"),
            FullPage = true
        });
        await page.SetViewportSizeAsync(360, 900);
        await card.Locator(".clarifications summary").ClickAsync();
        var clarify = card.GetByRole(AriaRole.Button, new()
        {
            Name = "Заполнить: Уточните время в пути",
            Exact = true
        });
        await clarify.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("#hours")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Expect(page.Locator("#minutes")).ToBeFocusedAsync();
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Отмена",
            Exact = true
        }).ClickAsync();
        if (!await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth"))
            throw new InvalidOperationException("Budget UI overflows a 360px viewport.");
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(evidence, "budget-mobile.png"),
            FullPage = true
        });
        await page.SetViewportSizeAsync(1440, 1000);
        await page.EvaluateAsync("() => document.documentElement.style.zoom = '2'");
        await editTrip.ClickAsync();
        await page.Locator("#budget-limit").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Expect(page.Locator("#reserve-percent")).ToBeFocusedAsync();
        var bounds = await page.Locator("#reserve-percent").BoundingBoxAsync();
        if (bounds is null || bounds.X < 0 || bounds.X + bounds.Width > 1440)
            throw new InvalidOperationException("Budget settings overflow at CSS zoom 200%.");
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(evidence, "budget-form-200.png"),
            FullPage = true
        });
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Отмена",
            Exact = true
        }).ClickAsync();
        await page.EvaluateAsync("() => document.documentElement.style.zoom = ''");
    }

    private static async Task UpdateBudget(IBrowserContext context, string baseUrl, string tripId, long limit)
    {
        var root = baseUrl.TrimEnd('/');
        var response = await context.APIRequest.GetAsync($"{root}/api/trips/{tripId}");
        using var document = JsonDocument.Parse(await response.TextAsync());
        var trip = document.RootElement;
        var csrf = await context.APIRequest.GetAsync($"{root}/api/auth/antiforgery");
        using var tokenDocument = JsonDocument.Parse(await csrf.TextAsync());
        var token = tokenDocument.RootElement.GetProperty("token").GetString()!;
        var updated = await context.APIRequest.PutAsync($"{root}/api/trips/{tripId}", new()
        {
            Headers = new Dictionary<string, string> { ["X-XSRF-TOKEN"] = token },
            DataObject = new
            {
                name = trip.GetProperty("name").GetString(),
                startDate = trip.GetProperty("startDate").GetString(),
                endDate = trip.GetProperty("endDate").GetString(),
                adults = trip.GetProperty("adults").GetInt32(),
                childAges = trip.GetProperty("childAges").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
                expectedRevision = trip.GetProperty("revision").GetInt64(),
                budgetLimitKopecks = limit,
                reservePercent = 10
            }
        });
        if (!updated.Ok)
            throw new InvalidOperationException($"Second-session budget update returned {updated.Status}.");
    }
}
