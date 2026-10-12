using System.Text.Json;
using Microsoft.Playwright;
using Together.Core;
using Together.BrowserTests;
using static Microsoft.Playwright.Assertions;

if (args.Contains("--install")) return Microsoft.Playwright.Program.Main(["install", "chromium", "firefox"]);
if (args.Contains("--serve-published"))
{
    await PublishedPreview.RunAsync();
    return 0;
}
var baseUrl = Environment.GetEnvironmentVariable("APP_URL") ?? "http://localhost:5180";
var evidence = Path.GetFullPath(Environment.GetEnvironmentVariable("BROWSER_EVIDENCE_DIR") ?? "docs/evidence");
Directory.CreateDirectory(evidence);
using var playwright = await Playwright.CreateAsync();
if (args.Contains("--startup"))
{
    return await StartupSmoke.RunAsync(playwright, baseUrl, evidence, args.Contains("--before-fix"));
}
if (args.Contains("--backend-smoke"))
{
    return await BackendSmoke.RunAsync(playwright, baseUrl, evidence);
}
if (args.Contains("--hot-reload"))
{
    await using var browser = await playwright.Chromium.LaunchAsync(new()
    {
        Headless = true
    });
    var page = await browser.NewPageAsync();
    await page.GotoAsync(baseUrl);
    await Expect(page.GetByRole(AriaRole.Heading, new()
    {
        Name = "Все варианты. Все расходы."
    })).ToBeVisibleAsync(new()
    {
        Timeout = 60000
    });
    var path = Path.GetFullPath("src/Together.Client/wwwroot/css/app.css");
    var original = await File.ReadAllTextAsync(path);
    try
    {
        await File.WriteAllTextAsync(path, original + "\n:root { --hot-reload-check: verified; }\n");
        await page.WaitForFunctionAsync("() => getComputedStyle(document.documentElement).getPropertyValue('--hot-reload-check').trim() === 'verified'");
    }
    finally { await File.WriteAllTextAsync(path, original); }
    await page.WaitForFunctionAsync("() => getComputedStyle(document.documentElement).getPropertyValue('--hot-reload-check').trim() === ''");
    await File.WriteAllTextAsync(Path.Combine(evidence, "hot-reload.json"), "{\"passed\":true,\"check\":\"CSS updated in an open browser and restored without manual reload\"}");
    Console.WriteLine("PASS hot-reload: CSS updated and restored in an open browser");
    return 0;
}
var results = new List<object>();
var metrics = new List<object>();
var failures = 0;
foreach (var engine in new[] { playwright.Chromium, playwright.Firefox })
{
    await using var browser = await engine.LaunchAsync(new()
    {
        Headless = true
    });
    Console.WriteLine($"{engine.Name} {browser.Version}");
    async Task Case(string name, Func<IPage, Task> test)
    {
        var filter = Environment.GetEnvironmentVariable("TEST_FILTER");
        if (!string.IsNullOrEmpty(filter) && !filter.Split(',').Contains(name))
            return;
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
        var consoleErrors = new List<string>();
        page.PageError += (_, message) => consoleErrors.Add(message);
        page.Console += (_, message) => { if (message.Type == "error") consoleErrors.Add(message.Text); };
        try
        {
            await page.GotoAsync(baseUrl);
            await Expect(page.GetByRole(AriaRole.Heading, new()
            {
                Name = "Все варианты. Все расходы."
            })).ToBeVisibleAsync(new()
            {
                Timeout = 60000
            });
            await test(page);
            if (consoleErrors.Count > 0)
                throw new Exception(string.Join("\n", consoleErrors));
            results.Add(new
            {
                engine = engine.Name,
                version = browser.Version,
                name,
                passed = true
            });
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            failures++;
            await page.ScreenshotAsync(new()
            {
                Path = Path.Combine(evidence, $"{engine.Name}-{name}-failure.png"),
                FullPage = true
            });
            results.Add(new
            {
                engine = engine.Name,
                version = browser.Version,
                name,
                passed = false,
                error = ex.Message
            });
            Console.WriteLine($"FAIL {name}: {ex.Message}");
        }
    }
    await Case("workflow", async page =>
    {
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Новая поездка",
            Exact = true
        }).ClickAsync();
        await page.Locator("#name").FillAsync("Лето");
        await page.Locator("#start").FillAsync("2027-07-12");
        await page.Locator("#end").FillAsync("2027-07-19");
        await page.Locator("#child-0").FillAsync("3");
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Добавить ребёнка",
            Exact = true
        }).ClickAsync();
        await page.Locator("#child-1").FillAsync("8");
        await Save(page);
        await Expect(page.GetByText("Ночей: 7", new()
        {
            Exact = true
        })).ToBeVisibleAsync();
        await Add(page, "Первый", true);
        await Add(page, "Второй", false);
        var cards = page.Locator(".variant-card");
        await cards.Nth(0).GetByRole(AriaRole.Checkbox).CheckAsync();
        await Saved(page);
        await cards.Nth(1).GetByRole(AriaRole.Checkbox).CheckAsync();
        await Saved(page);
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(7);
        await Expect(cards.Nth(0).Locator(".price")).ToContainTextAsync("143");
        await cards.Nth(0).GetByRole(AriaRole.Button, new()
        {
            Name = "Редактировать"
        }).ClickAsync();
        await page.Locator("#expense-1").FillAsync("60000");
        await Save(page);
        await Expect(page.Locator("tbody tr").First.Locator("td").First).ToContainTextAsync("147");
        await page.GetByRole(AriaRole.Checkbox, new()
        {
            Name = "Кухня",
            Exact = true
        }).UncheckAsync();
        await Saved(page);
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(6);
        await page.ReloadAsync();
        await page.Locator("#trip-select").SelectOptionAsync(new SelectOptionValue { Label = "Лето" });
        await Expect(page.Locator(".variant-card")).ToHaveCountAsync(2);
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(6);
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Параметры поездки",
            Exact = true
        }).ClickAsync();
        await page.Locator("#end").FillAsync("2027-07-20");
        await Save(page);
        await Expect(cards.Nth(0).GetByText(Catalog.ReviewNotice, new()
        {
            Exact = true
        })).ToBeVisibleAsync();
        await cards.Nth(0).GetByRole(AriaRole.Button, new()
        {
            Name = "Редактировать"
        }).ClickAsync();
        await page.GetByRole(AriaRole.Checkbox, new()
        {
            Name = "Расходы проверены"
        }).CheckAsync();
        await Save(page);
        await Expect(cards.Nth(0).GetByText(Catalog.ReviewNotice, new()
        {
            Exact = true
        })).ToHaveCountAsync(0);
        page.Dialog += (_, dialog) => dialog.AcceptAsync();
        await cards.Nth(1).GetByRole(AriaRole.Button, new()
        {
            Name = "Удалить"
        }).ClickAsync();
        await Expect(cards).ToHaveCountAsync(1);
        await Expect(page.GetByText("Выберите минимум два варианта", new()
        {
            Exact = true
        })).ToBeVisibleAsync();
        await page.ReloadAsync();
        await page.Locator("#trip-select").SelectOptionAsync(new SelectOptionValue { Label = "Лето" });
        await Expect(cards).ToHaveCountAsync(1);
    });
    await Case("example-data", async page =>
    {
        await Seed(page, Fixtures.Create());
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Добавить пример",
            Exact = true
        }).ClickAsync();
        await Expect(page.Locator(".variant-card")).ToHaveCountAsync(3);
        await Expect(page.GetByRole(AriaRole.Button, new()
        {
            Name = "Добавить пример",
            Exact = true
        })).ToHaveCountAsync(0);
        await page.ReloadAsync();
        await page.Locator("#trip-select").SelectOptionAsync(new SelectOptionValue { Label = ExampleData.TripName });
        await Expect(page.Locator(".variant-card")).ToHaveCountAsync(3);
    });
    await Case("validation", async page =>
    {
        await Seed(page, Fixtures.Create());
        await page.Locator(".variant-card").First.GetByRole(AriaRole.Button, new()
        {
            Name = "Редактировать"
        }).ClickAsync();
        await page.Locator("#url").FillAsync("javascript:alert(1)");
        await page.Locator("#expense-0").FillAsync("-1");
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Сохранить",
            Exact = true
        }).ClickAsync();
        await Expect(page.Locator("#url")).ToBeFocusedAsync();
        await Expect(page.Locator("#url-error")).ToContainTextAsync("http");
        await Expect(page.Locator("#expense-0-error")).ToBeVisibleAsync();
        page.Dialog += (_, dialog) => dialog.DismissAsync();
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Отмена",
            Exact = true
        }).ClickAsync();
        await Expect(page.Locator("dialog")).ToBeVisibleAsync();
        await Expect(page.Locator("#url")).ToHaveValueAsync("javascript:alert(1)");
    });
    await Case("storage-failure", async page =>
    {
        await Seed(page, Fixtures.Create());
        var before = await Stored(page);
        await page.EvaluateAsync("() => { window.originalPut = IDBObjectStore.prototype.put; IDBObjectStore.prototype.put = function() { this.transaction.abort(); }; }");
        var kitchen = page.GetByRole(AriaRole.Checkbox, new()
        {
            Name = "Кухня",
            Exact = true
        });
        // Dispatch without Playwright's actionability wait: the deliberately aborted
        // IDB callback is verified through the resulting UI state below.
        await kitchen.EvaluateAsync("element => element.click()");
        await Expect(page.GetByText("Не удалось сохранить данные в этом браузере", new()
        {
            Exact = true
        })).ToBeVisibleAsync();
        await Expect(kitchen).ToBeCheckedAsync();
        await page.Locator(".variant-card").First.GetByRole(AriaRole.Button, new()
        {
            Name = "Редактировать"
        }).ClickAsync();
        await page.Locator("#name").FillAsync("Несохранённый черновик");
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Сохранить",
            Exact = true
        }).ClickAsync();
        await Expect(page.GetByText("Не удалось сохранить данные в этом браузере", new()
        {
            Exact = true
        })).ToBeVisibleAsync();
        await Expect(page.Locator("#name")).ToHaveValueAsync("Несохранённый черновик");
        Check(before == await Stored(page), "Rejected transaction changed data");
        await page.EvaluateAsync("() => { IDBObjectStore.prototype.put = window.originalPut; }");
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Повторить",
            Exact = true
        }).ClickAsync();
        await Expect(page.Locator("dialog")).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Heading, new()
        {
            Name = "Несохранённый черновик"
        })).ToBeVisibleAsync();
    });
    await Case("conflict", async page =>
    {
        await Seed(page, Fixtures.Create());
        var other = await page.Context.NewPageAsync();
        await other.GotoAsync(baseUrl);
        await Expect(other.Locator(".variant-card")).ToHaveCountAsync(3);
        await page.Locator(".variant-card").First.GetByRole(AriaRole.Button, new()
        {
            Name = "Редактировать"
        }).ClickAsync();
        await page.Locator("#name").FillAsync("Черновик первой вкладки");
        await other.GetByRole(AriaRole.Checkbox, new()
        {
            Name = "Кухня",
            Exact = true
        }).UncheckAsync();
        await Saved(other);
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Сохранить",
            Exact = true
        }).ClickAsync();
        await Expect(page.GetByText("Данные изменились в другой вкладке.", new()
        {
            Exact = false
        })).ToBeVisibleAsync();
        await Expect(page.Locator("#name")).ToHaveValueAsync("Черновик первой вкладки");
        page.Dialog += (_, dialog) => dialog.AcceptAsync();
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Загрузить актуальные данные"
        }).ClickAsync();
        await Expect(page.Locator("dialog")).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Checkbox, new()
        {
            Name = "Кухня",
            Exact = true
        })).Not.ToBeCheckedAsync();
    });
    await Case("corrupt-data", async page =>
    {
        const string corrupt = """{"schemaVersion":1,"revision":1,"trips":null}""";
        await Put(page, corrupt);
        await page.ReloadAsync();
        await Expect(page.GetByText("Не удалось прочитать данные.", new()
        {
            Exact = false
        })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Повторить",
            Exact = true
        }).ClickAsync();
        await Expect(page.GetByText("Не удалось прочитать данные.", new()
        {
            Exact = false
        })).ToBeVisibleAsync();
        Check(corrupt == await Stored(page), "Corrupt data was overwritten");
    });
    await Case("responsive-offline", async page =>
    {
        await Seed(page, Fixtures.Create());
        foreach (var width in new[] { 360, 768, 1440 })
        {
            await page.SetViewportSizeAsync(width, 1000);
            Check(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"), $"Page overflow at {width}");
            Check(await page.EvaluateAsync<bool>("""
                () => [...document.querySelectorAll('.mud-button-root,.filters label,.select-variant,.action-link,summary,select')].every(e => {
                    const r=e.getBoundingClientRect(); return r.height >= 43.9 && r.width >= 43.9;
                })
                """), $"Interactive target smaller than 44px at {width}");
            await page.ScreenshotAsync(new()
            {
                Path = Path.Combine(evidence, $"{engine.Name}-{width}.png"),
                FullPage = true
            });
            Check(await page.Locator("thead th").First.EvaluateAsync<string>("e => getComputedStyle(e).position") == "sticky", "Header is not sticky");
        }
        await page.SetViewportSizeAsync(720, 500);
        await page.Locator(".variant-card").First.GetByRole(AriaRole.Button, new()
        {
            Name = "Редактировать"
        }).ClickAsync();
        await page.EvaluateAsync("() => document.documentElement.style.zoom = '2'");
        Check(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"), "Overflow at 200% zoom");
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(evidence, $"{engine.Name}-form-200.png")
        });
        await page.Context.SetOfflineAsync(true);
        await page.Locator("#notes").FillAsync("Сохранено без сети");
        await Save(page);
        await Expect(page.GetByText("Сохранено без сети", new()
        {
            Exact = true
        })).ToBeVisibleAsync();
    });
    await Case("keyboard", async page =>
    {
        await Seed(page, Fixtures.Create());
        var button = page.GetByRole(AriaRole.Button, new()
        {
            Name = "Параметры поездки",
            Exact = true
        });
        await button.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("dialog")).ToBeVisibleAsync();
        for (var i = 0; i < 25; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            Check(await page.EvaluateAsync<bool>("() => document.querySelector('dialog').contains(document.activeElement)"), "Focus escaped the modal");
        }
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("dialog")).ToHaveCountAsync(0);
        await Expect(button).ToBeFocusedAsync();
        var kitchen = page.GetByRole(AriaRole.Checkbox, new()
        {
            Name = "Кухня",
            Exact = true
        });
        await kitchen.FocusAsync();
        await page.Keyboard.PressAsync("Space");
        await Saved(page);
        await Expect(kitchen).Not.ToBeCheckedAsync();
    });
    await Case("independent-trips", async page =>
    {
        var fixture = Fixtures.Create(2);
        await Seed(page, fixture);
        foreach (var label in Catalog.CriterionLabels)
        {
            await page.GetByRole(AriaRole.Checkbox, new()
            {
                Name = label,
                Exact = true
            }).UncheckAsync();
            await Saved(page);
        }
        await Expect(page.GetByText("Выберите хотя бы один критерий", new()
        {
            Exact = true
        })).ToBeVisibleAsync();
        await page.Locator("#trip-select").SelectOptionAsync(fixture.Trips[1].Id.ToString());
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(7);
        await page.Locator("#trip-select").SelectOptionAsync(fixture.Trips[0].Id.ToString());
        await Expect(page.GetByText("Выберите хотя бы один критерий", new()
        {
            Exact = true
        })).ToBeVisibleAsync();
        await page.ReloadAsync();
        await Expect(page.GetByText("Выберите хотя бы один критерий", new()
        {
            Exact = true
        })).ToBeVisibleAsync();
    });
    await Case("delete-failure", async page =>
    {
        await Seed(page, Fixtures.Create(1, 1));
        var before = await Stored(page);
        page.Dialog += (_, dialog) => dialog.AcceptAsync();
        await page.EvaluateAsync("() => { window.originalPut = IDBObjectStore.prototype.put; IDBObjectStore.prototype.put = function() { this.transaction.abort(); }; }");
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Удалить",
            Exact = true
        }).ClickAsync();
        await Expect(page.GetByText("Не удалось сохранить данные в этом браузере", new()
        {
            Exact = true
        })).ToBeVisibleAsync();
        await Expect(page.Locator(".variant-card")).ToHaveCountAsync(1);
        Check(before == await Stored(page), "Failed delete changed storage");
        await page.EvaluateAsync("() => { IDBObjectStore.prototype.put = window.originalPut; }");
        await page.GetByRole(AriaRole.Button, new()
        {
            Name = "Повторить",
            Exact = true
        }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new()
        {
            Name = "Пока нет вариантов"
        })).ToBeVisibleAsync();
        await page.ReloadAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new()
        {
            Name = "Пока нет вариантов"
        })).ToBeVisibleAsync();
    });
    await Case("performance", async page =>
    {
        await Seed(page, Fixtures.Create(10, 50));
        var timings = new List<double>();
        for (var i = 0; i < 6; i++)
        {
            var elapsed = await page.EvaluateAsync<double>("""
                () => new Promise((resolve,reject) => {
                    const input = [...document.querySelectorAll('.filters label')].find(e => e.textContent.trim() === 'Кухня').querySelector('input');
                    const expected = input.checked ? 6 : 7;
                    const start = performance.now();
                    const timeout = setTimeout(() => { observer.disconnect(); reject(new Error('Render timeout')); },5000);
                    const observer = new MutationObserver(() => {
                        if(document.querySelectorAll('tbody tr').length === expected) { observer.disconnect(); clearTimeout(timeout); resolve(performance.now()-start); }
                    });
                    observer.observe(document.querySelector('main'),{childList:true,subtree:true}); input.click();
                })
                """);
            if (i > 0)
                timings.Add(elapsed);
        }
        var columnTimings = new List<double>();
        for (var i = 0; i < 4; i++)
        {
            columnTimings.Add(await page.EvaluateAsync<double>("""
                () => new Promise((resolve,reject) => {
                    const input = document.querySelector('.variant-card input[type=checkbox]');
                    const expected = input.checked ? 10 : 11;
                    const start = performance.now();
                    const timeout = setTimeout(() => { observer.disconnect(); reject(new Error('Column timeout')); },5000);
                    const observer = new MutationObserver(() => {
                        if(document.querySelectorAll('thead th').length === expected) { observer.disconnect(); clearTimeout(timeout); resolve(performance.now()-start); }
                    });
                    observer.observe(document.querySelector('main'),{childList:true,subtree:true}); input.click();
                })
                """));
        }
        await page.Locator(".variant-card").First.GetByRole(AriaRole.Button, new()
        {
            Name = "Редактировать"
        }).ClickAsync();
        var budgetMs = await page.EvaluateAsync<double>("""
            () => new Promise((resolve,reject) => {
                const field = document.getElementById('expense-1');
                const start = performance.now();
                const timeout = setTimeout(() => { observer.disconnect(); reject(new Error('Budget timeout')); },5000);
                const observer = new MutationObserver(() => {
                    if(document.querySelector('.budget-preview .price').textContent.replace(/\s/g,'').includes('147000')) { observer.disconnect(); clearTimeout(timeout); resolve(performance.now()-start); }
                });
                observer.observe(document.querySelector('.budget-preview'),{childList:true,subtree:true,characterData:true});
                field.value = '60000'; field.dispatchEvent(new Event('input',{bubbles:true}));
            })
            """);
        await page.EvaluateAsync("""
            () => {
                const transaction = IDBDatabase.prototype.transaction;
                IDBDatabase.prototype.transaction = function(...args) {
                    const tx = transaction.apply(this,args);
                    if(args[1] === 'readwrite') tx.addEventListener('complete',()=>window.commitCompletedAt=performance.now());
                    return tx;
                };
                const observer = new MutationObserver(() => {
                    if(window.commitCompletedAt && document.querySelector('.variant-card .price').textContent.replace(/\s/g,'').includes('147000')) {
                        window.renderAfterCommit = performance.now()-window.commitCompletedAt; observer.disconnect();
                    }
                });
                observer.observe(document.querySelector('main'),{childList:true,subtree:true,characterData:true});
            }
            """);
        await Save(page);
        var afterCommit = await page.EvaluateAsync<double>("() => window.renderAfterCommit");
        metrics.Add(new
        {
            engine = engine.Name,
            criterionMs = timings,
            columnMs = columnTimings,
            budgetMs,
            renderAfterCommitMs = afterCommit,
            trips = 10,
            variantsPerTrip = 50,
            columns = 10
        });
        Console.WriteLine($"PERF criterion max={timings.Max():F1}ms column max={columnTimings.Max():F1}ms budget={budgetMs:F1}ms afterCommit={afterCommit:F1}ms");
        Check(timings.Max() <= 200, "Criterion exceeds 200ms");
        Check(columnTimings.Max() <= 200, "Column exceeds 200ms");
        Check(budgetMs <= 100, "Budget exceeds 100ms");
        Check(afterCommit <= 300, "Rendering after commit exceeds 300ms");
    });
}
await File.WriteAllTextAsync(Path.Combine(evidence, "browser-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
await File.WriteAllTextAsync(Path.Combine(evidence, "performance.json"), JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));
return failures == 0 ? 0 : 1;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Saved(IPage page) => await Expect(page.Locator(".feedback")).ToHaveTextAsync("Сохранено");
static async Task Save(IPage page) { await page.GetByRole(AriaRole.Button, new() { Name = "Сохранить", Exact = true }).ClickAsync(); await Expect(page.Locator("dialog")).ToHaveCountAsync(0); }
static async Task Add(IPage page, string name, bool budget)
{
    await page.GetByRole(AriaRole.Button, new()
    {
        Name = "Добавить вариант",
        Exact = true
    }).Last.ClickAsync();
    await page.Locator("#name").FillAsync(name);
    await page.Locator("#destination").FillAsync("Геленджик");
    await page.Locator("#accommodation").FillAsync("Апартаменты");
    if (budget)
    {
        string[] values = ["42000", "56000", "24000", "6000", "10000", "5000"];
        for (var i = 0; i < 6; i++)
            await page.Locator($"#expense-{i}").FillAsync(values[i]);
    }
    await Save(page);
}
static async Task Seed(IPage page, Workspace data) { await Put(page, JsonSerializer.Serialize(data, Workspace.JsonOptions)); await page.ReloadAsync(); await Expect(page.Locator(".variant-card")).ToHaveCountAsync(data.Trips[0].Variants.Count, new() { Timeout = 60000 }); }
static async Task Put(IPage page, string payload) => await page.EvaluateAsync("""
    payload => new Promise((resolve,reject) => {
        const request = indexedDB.open('together-trips',1);
        request.onsuccess = () => { const db=request.result; const tx=db.transaction('workspace','readwrite'); tx.objectStore('workspace').put(payload,'current'); tx.oncomplete=()=>{db.close();resolve()}; tx.onerror=()=>reject(tx.error); };
    })
    """, payload);
static async Task<string> Stored(IPage page) => await page.EvaluateAsync<string>("async () => (await import('./js/storage.js')).read()");
