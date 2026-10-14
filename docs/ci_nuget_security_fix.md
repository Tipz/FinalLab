# Исправление NU1903 в CI — 14 октября 2026

`dotnet restore Together.slnx --locked-mode` останавливался из-за двух
уязвимостей в `Microsoft.AspNetCore.Components.WebAssembly.Server` 10.0.12.
Ошибка воспроизведена локально до изменения зависимостей.
В обоих бюллетенях Microsoft исправленной версией для .NET 10 указана 10.0.13:

- [GHSA-g27p-qf6j-c96c](https://github.com/advisories/GHSA-g27p-qf6j-c96c);
- [GHSA-qjpx-jqm5-h2c4](https://github.com/advisories/GHSA-qjpx-jqm5-h2c4).

В `src/Together.Api/Together.Api.csproj` пакет обновлён до 10.0.13.
Командой `dotnet restore Together.slnx --force-evaluate` обновлены lock-файлы
API и API-тестов, включая требуемые транзитивные зависимости.
Остальные прямые зависимости, SDK и workflow сохранены.
`TreatWarningsAsErrors`, аудит NuGet и locked restore остаются включены.

Проверки выполнены на Windows с SDK 10.0.303 и существующими изменениями
пользователя в компонентах клиента и тестах. Эти исходные файлы не изменялись
в рамках исправления NU1903.

| Команда | Фактический результат |
| --- | --- |
| `dotnet restore Together.slnx --force-evaluate` | Exit 0, lock-файлы обновлены |
| `dotnet restore Together.slnx --locked-mode` | Exit 0 для всех 7 проектов, NU1903 отсутствует |
| `dotnet package list --project Together.slnx --no-restore --include-transitive --vulnerable --format json` | Exit 0, уязвимые пакеты не найдены |
| `dotnet format Together.slnx --verify-no-changes --no-restore` | Exit 0 |
| `dotnet build Together.slnx --configuration Release --no-restore` | Exit 0, 0 предупреждений и 0 ошибок |
| `dotnet test tests/Together.Tests/Together.Tests.csproj --configuration Release --no-build --no-restore` | 66/66, 0 пропущено |
| `dotnet test tests/Together.Api.Tests/Together.Api.Tests.csproj --configuration Release --no-build --no-restore` | 25/25, 0 пропущено |
| `dotnet run --project tests/Together.BrowserTests --configuration Release --no-build --no-restore -- --backend-smoke` | Exit 1: ожидался текст «Сохранено» в `.feedback`, получена пустая строка |

Smoke выполнен при `APP_URL=http://localhost:5188` на ранее запущенном локальном
стенде с доступным `/health/ready`. `BROWSER_EVIDENCE_DIR` направлен в отдельный
временный каталог вне репозитория. Проверки меню на ширинах 360, 768 и 1440 px
прошли; полный smoke не прошёл. Этот стенд не пересобирался и не перезапускался
с обновлённой зависимостью, поэтому запуск не подтверждает браузерную проверку
новой сборки API.

Docker отсутствует в PATH. Контейнерные проверки, CodeQL и повторный запуск
GitHub Actions не выполнены; их результат должен быть получен в CI после
отправки обновлённого csproj и обоих lock-файлов.
