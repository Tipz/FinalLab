using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Together.BrowserTests;

public static class BackendSmoke
{
    public static async Task<int> RunAsync(IPlaywright playwright, string baseUrl, string evidence, bool headed = false)
    {
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = !headed
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
            await CheckResponsiveScreen(page, "login", evidence, navigation: false);
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
                Name = "Мои поездки",
                Exact = true
            }))
                .ToBeVisibleAsync(new()
                {
                    Timeout = 60_000
                });
            await CheckScreens(page, baseUrl, evidence);
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

            await CheckEmptyNavigation(browser, baseUrl, evidence);

            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

            await WriteResult(evidence, browser.Version, true, null);
            Console.WriteLine("PASS backend-smoke: registration, three screens, direct links/history, independent trips, budget/reserve persistence, clarifications, failed write, real conflict, empty menu with legacy data, mobile/keyboard and logout");
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

    private static async Task CheckEmptyNavigation(IBrowser browser, string baseUrl, string evidence)
    {
        await using var context = await browser.NewContextAsync(new()
        {
            Locale = "ru-RU"
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(baseUrl);
        await Expect(page.Locator("h1")).ToHaveTextAsync("Вход", new()
        {
            Timeout = 60_000
        });
        var legacy = JsonSerializer.Serialize(Together.Core.ExampleData.Create(), Together.Core.Workspace.JsonOptions);
        await page.EvaluateAsync("""
            async json => {
                const storage = await import('./js/storage.js');
                await storage.read();
                const db = await new Promise((resolve, reject) => {
                    const request = indexedDB.open('together-trips', 1);
                    request.onsuccess = () => resolve(request.result);
                    request.onerror = reject;
                });
                await new Promise((resolve, reject) => {
                    const tx = db.transaction('workspace', 'readwrite');
                    tx.objectStore('workspace').put(json, 'current');
                    tx.oncomplete = resolve;
                    tx.onerror = reject;
                });
                db.close();
            }
            """, legacy);
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Создать аккаунт",
            Exact = true
        }).ClickAsync();
        await page.GetByLabel("Email").FillAsync($"empty-menu-{Guid.NewGuid():N}@example.test");
        await page.GetByLabel("Пароль").FillAsync("fictional menu password");
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Создать аккаунт",
            Exact = true
        }).ClickAsync();
        await Expect(page.Locator(".first-trip")).ToBeVisibleAsync(new()
        {
            Timeout = 60_000
        });
        foreach (var width in new[] { 360, 768, 1440 })
        {
            await page.SetViewportSizeAsync(width, 1000);
            foreach (var label in new[] { "2. Варианты поездки", "3. Сравнение", "1. Мои поездки" })
            {
                await page.GetByRole(AriaRole.Link, new()
                {
                    Name = label,
                    Exact = true
                }).ClickAsync();
                await Expect(page.Locator("h1")).ToHaveTextAsync(label[3..]);
                await Expect(page.GetByRole(AriaRole.Heading, new()
                {
                    Name = label.StartsWith("1.", StringComparison.Ordinal) ? "Создайте первую поездку" : "Сначала создайте поездку",
                    Exact = true
                })).ToBeVisibleAsync();
                await Expect(page.Locator("nav a")).ToHaveCountAsync(3);
                await Expect(page.Locator("nav [aria-disabled]")).ToHaveCountAsync(0);
                await Expect(page.Locator("nav [aria-current=page]")).ToHaveTextAsync(label);
                Console.WriteLine($"PASS empty menu click: {width}px, {label}");
            }
        }
        var response = await context.APIRequest.GetAsync(baseUrl.TrimEnd('/') + "/api/trips");
        using var trips = JsonDocument.Parse(await response.TextAsync());
        if (trips.RootElement.GetArrayLength() != 0)
            throw new InvalidOperationException("Empty menu navigation must not upload local trips or create examples.");
        if (await page.EvaluateAsync<string>("async () => (await import('./js/storage.js')).read()") != legacy)
            throw new InvalidOperationException("Empty menu navigation changed legacy data.");
        await page.GetByRole(AriaRole.Link, new()
        {
            Name = "3. Сравнение",
            Exact = true
        }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new()
        {
            Name = "Сначала создайте поездку",
            Exact = true
        })).ToBeVisibleAsync();
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(evidence, "empty-menu-desktop.png"),
            FullPage = true
        });
        await page.SetViewportSizeAsync(360, 1000);
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(evidence, "empty-menu-mobile.png"),
            FullPage = true
        });
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Создать поездку",
            Exact = true
        }).ClickAsync();
        await page.Locator("#name").FillAsync("Вымышленная поездка из пустого сравнения");
        await page.Locator("#start").FillAsync("2027-07-12");
        await page.Locator("#end").FillAsync("2027-07-19");
        await page.Locator("#child-0").FillAsync("5");
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Сохранить",
            Exact = true
        }).ClickAsync();
        await Expect(page.Locator("#trip-select")).ToBeVisibleAsync();
        await Expect(page.Locator("h1")).ToHaveTextAsync("Варианты поездки");
        await page.GetByRole(AriaRole.Link, new()
        {
            Name = "3. Сравнение",
            Exact = true
        }).ClickAsync();
        await Expect(page.Locator("h1")).ToHaveTextAsync("Сравнение");
        await Expect(page.GetByText("Выберите минимум два варианта", new()
        {
            Exact = true
        })).ToBeVisibleAsync();
    }

    private static async Task CheckScreens(IPage page, string baseUrl, string evidence)
    {
        await Expect(page.Locator(".trip-card")).ToHaveCountAsync(1, new()
        {
            Timeout = 60_000
        });
        await Expect(page.Locator(".variant-card,table")).ToHaveCountAsync(0);
        var variants = page.GetByRole(AriaRole.Link, new()
        {
            Name = "Открыть варианты",
            Exact = true
        });
        var variantsPath = (await variants.GetAttributeAsync("href"))!;
        var tripId = variantsPath.Split('/')[2];
        var comparisonPath = $"/trips/{tripId}/comparison";
        await CheckMenuClicks(page);
        await CheckResponsiveScreen(page, "trips", evidence);
        await variants.ClickAsync();
        await Expect(page.Locator("h1")).ToHaveTextAsync("Варианты поездки");
        await Expect(page.Locator(".variant-card")).ToHaveCountAsync(3);
        await Expect(page.Locator("table")).ToHaveCountAsync(0);
        await CheckResponsiveScreen(page, "variants", evidence);
        await page.Locator(".variant-card").First.GetByRole(AriaRole.Button, new()
        {
            Name = "Редактировать",
            Exact = true
        }).ClickAsync();
        await page.Locator("#name").FillAsync("Несохранённый черновик маршрута");
        EventHandler<IDialog> dismissNavigation = async (_, dialog) => await dialog.DismissAsync();
        page.Dialog += dismissNavigation;
        await page.EvaluateAsync("() => history.back()");
        await Expect(page).ToHaveURLAsync(baseUrl.TrimEnd('/') + variantsPath);
        await Expect(page.Locator("#name")).ToHaveValueAsync("Несохранённый черновик маршрута");
        page.Dialog -= dismissNavigation;
        EventHandler<IDialog> acceptNavigation = async (_, dialog) => await dialog.AcceptAsync();
        page.Dialog += acceptNavigation;
        await page.EvaluateAsync("() => history.back()");
        await Expect(page.Locator("h1")).ToHaveTextAsync("Мои поездки");
        await Expect(page.Locator("dialog")).ToHaveCountAsync(0);
        page.Dialog -= acceptNavigation;
        await page.GoForwardAsync();
        await Expect(page.Locator(".variant-card")).ToHaveCountAsync(3);
        await Expect(page.Locator(".variant-card").First).Not.ToContainTextAsync("Несохранённый черновик маршрута");
        var middleSelection = page.Locator(".select-variant input").Nth(1);
        await middleSelection.UncheckAsync();
        await Expect(page.Locator(".feedback")).ToHaveTextAsync("Сохранено");
        await page.GetByRole(AriaRole.Link, new()
        {
            Name = "Перейти к сравнению",
            Exact = true
        }).ClickAsync();
        await Expect(page.Locator("h1")).ToHaveTextAsync("Сравнение");
        await Expect(page.Locator("#comparison table")).ToBeVisibleAsync();
        await Expect(page.Locator("thead th")).ToHaveCountAsync(3);
        await Expect(page.Locator(".variant-card")).ToHaveCountAsync(2);
        await CheckResponsiveScreen(page, "comparison", evidence);
        await page.GoBackAsync();
        await Expect(page.Locator("h1")).ToHaveTextAsync("Варианты поездки");
        await page.GoBackAsync();
        await Expect(page.Locator("h1")).ToHaveTextAsync("Мои поездки");
        await page.GoForwardAsync();
        await Expect(page.Locator("h1")).ToHaveTextAsync("Варианты поездки");
        await page.GoForwardAsync();
        await Expect(page.Locator("h1")).ToHaveTextAsync("Сравнение");

        await page.GetByRole(AriaRole.Link, new()
        {
            Name = "К вариантам поездки",
            Exact = true
        }).ClickAsync();
        // The comparison still has two selected cards while the route is changing.
        // Wait for the variants screen before using its middle checkbox.
        await Expect(page.Locator("h1")).ToHaveTextAsync("Варианты поездки");
        await Expect(page.Locator(".variant-card")).ToHaveCountAsync(3);
        await Expect(middleSelection).Not.ToBeCheckedAsync();
        await middleSelection.CheckAsync();
        await Expect(page.Locator(".feedback")).ToHaveTextAsync("Сохранено");
        await page.GetByRole(AriaRole.Link, new()
        {
            Name = "Перейти к сравнению",
            Exact = true
        }).ClickAsync();
        await Expect(page.Locator("thead th")).ToHaveCountAsync(4);

        // Refresh must keep both the requested trip and its comparison settings.
        var kitchen = page.GetByRole(AriaRole.Checkbox, new()
        {
            Name = "Кухня",
            Exact = true
        });
        await kitchen.UncheckAsync();
        await Expect(page.Locator(".feedback")).ToHaveTextAsync("Сохранено");
        await page.ReloadAsync();
        await Expect(page.Locator("#trip-select")).ToHaveValueAsync(tripId);
        await Expect(kitchen).Not.ToBeCheckedAsync();
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(6);

        await page.Locator("nav a[href='/']").ClickAsync();
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Новая поездка",
            Exact = true
        }).ClickAsync();
        await page.Locator("#name").FillAsync("Вымышленная вторая поездка");
        await page.Locator("#start").FillAsync("2027-07-12");
        await page.Locator("#end").FillAsync("2027-07-19");
        await page.Locator("#child-0").FillAsync("5");
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Сохранить",
            Exact = true
        }).ClickAsync();
        await Expect(page.Locator("h1")).ToHaveTextAsync("Варианты поездки");
        await Expect(page.GetByRole(AriaRole.Heading, new()
        {
            Name = "Пока нет вариантов",
            Exact = true
        })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new()
        {
            Name = "Перейти к сравнению",
            Exact = true
        }).ClickAsync();
        await Expect(page.GetByText("Выберите минимум два варианта", new()
        {
            Exact = true
        })).ToBeVisibleAsync();
        await Expect(kitchen).ToBeCheckedAsync();
        await page.Locator("#trip-select").SelectOptionAsync(tripId);
        await Expect(kitchen).Not.ToBeCheckedAsync();
        await kitchen.CheckAsync();
        await Expect(page.Locator(".feedback")).ToHaveTextAsync("Сохранено");

        await page.GotoAsync(baseUrl.TrimEnd('/') + $"/trips/{Guid.NewGuid()}/comparison");
        await Expect(page.GetByRole(AriaRole.Heading, new()
        {
            Name = "Поездка недоступна",
            Exact = true
        })).ToBeVisibleAsync();
        await Expect(page.Locator(".variant-card,table,#trip-select")).ToHaveCountAsync(0);
        await page.GotoAsync(baseUrl.TrimEnd('/') + comparisonPath);
        await Expect(page.Locator("#comparison table")).ToBeVisibleAsync();
        await page.GotoAsync(baseUrl.TrimEnd('/') + variantsPath);
        await Expect(page.Locator(".variant-card")).ToHaveCountAsync(3);
        await page.ReloadAsync();
        await Expect(page.Locator("#trip-select")).ToHaveValueAsync(tripId);
        await Expect(page.Locator(".variant-card")).ToHaveCountAsync(3);
    }

    private static async Task CheckMenuClicks(IPage page)
    {
        var menu = page.GetByRole(AriaRole.Navigation, new()
        {
            Name = "Экраны планирования",
            Exact = true
        });
        foreach (var width in new[] { 360, 768, 1440 })
        {
            await page.SetViewportSizeAsync(width, 1000);
            foreach (var (label, heading) in new[]
            {
                ("2. Варианты поездки", "Варианты поездки"),
                ("3. Сравнение", "Сравнение"),
                ("1. Мои поездки", "Мои поездки"),
                ("3. Сравнение", "Сравнение"),
                ("2. Варианты поездки", "Варианты поездки"),
                ("1. Мои поездки", "Мои поездки")
            })
            {
                await menu.GetByRole(AriaRole.Link, new()
                {
                    Name = label,
                    Exact = true
                }).ClickAsync();
                await Expect(page.Locator("h1")).ToHaveTextAsync(heading);
                await Expect(menu.Locator("a[aria-current='page']")).ToHaveTextAsync(label);
                if (heading == "Мои поездки")
                    await Expect(page.Locator(".trip-card")).ToHaveCountAsync(1);
                else
                    await Expect(page.Locator(".variant-card")).ToHaveCountAsync(3);
                Console.WriteLine($"PASS menu click: {width}px, {label}");
            }
        }
    }

    private static async Task CheckResponsiveScreen(IPage page, string screen, string evidence, bool navigation = true)
    {
        foreach (var width in new[] { 360, 768, 1440 })
        {
            await page.SetViewportSizeAsync(width, 1000);
            if (navigation)
                await Expect(page.Locator("nav[aria-label='Экраны планирования']")).ToBeVisibleAsync();
            if (!await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"))
                throw new InvalidOperationException($"Screen {screen} overflows at {width}px.");
            if (!await page.EvaluateAsync<bool>("""
                () => [...document.querySelectorAll('.mud-button-root,.sidebar a,.brand,.filters label,.select-variant,summary')]
                    .filter(element => element.getClientRects().length > 0)
                    .every(element => {
                        const bounds = element.getBoundingClientRect();
                        return bounds.width >= 43.9 && bounds.height >= 43.9;
                    })
                """))
                throw new InvalidOperationException($"Screen {screen} has an interactive target smaller than 44px at {width}px.");
            await page.ScreenshotAsync(new()
            {
                Path = Path.Combine(evidence, $"screen-{screen}-{width}.png"),
                FullPage = true
            });
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
