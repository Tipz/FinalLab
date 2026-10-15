# Руководство разработчика

Все команды выполняются из корня репозитория.

- [Локальный запуск через Docker Compose](../backend_documentation.md#локальный-запуск)
- [Запуск из исходников без Docker](../backend_documentation.md#запуск-из-исходников-без-docker)
- [CI и публикация образа](integration_documentation.md#3-cicd)
- [Развёртывание готового образа](manual_production_deployment.md)

## Технологии

| Область | Технологии |
| --- | --- |
| Клиент | C#, .NET 10, standalone Blazor WebAssembly, MudBlazor 9.7.0 |
| Backend | ASP.NET Core Minimal API, ASP.NET Core Identity |
| Данные | EF Core, Npgsql, PostgreSQL |
| Тесты | xUnit, bUnit, Playwright .NET |
| Эксплуатация | Docker, Docker Compose, GitHub Actions, GHCR |
| Интеграции | Яндекс ID OAuth 2.0, Яндекс Метрика |

Бизнес-модель, вычисления и валидация находятся в `Together.Core` и не зависят
от браузера или базы данных. Контракты API выделены в `Together.Contracts`.

Правила лимита, резерва, уточнений и результаты проверок описаны в
[документации бюджетного планирования](budget_planning.md).

## Структура репозитория

| Путь | Назначение |
| --- | --- |
| `src/Together.Core` | Модель, вычисления и валидация |
| `src/Together.Contracts` | DTO и маршруты API |
| `src/Together.Client` | Blazor WebAssembly UI и API-клиент |
| `src/Together.Api` | Identity, Minimal API, EF Core и миграции |
| `tests/Together.Tests` | Модульные и компонентные тесты |
| `tests/Together.Api.Tests` | Интеграционные тесты API и безопасности |
| `tests/Together.BrowserTests` | Playwright browser smoke |
| `docker-compose.yml` | Локальная сборка и запуск из исходников |
| `docker-compose.deploy.yml` | Развёртывание опубликованного образа |
| `.github/workflows/ci.yml` | CI, security checks и публикация GHCR image |
| `docs` | Требования, отчёты, инструкции и evidence |

## Проверки

Release-сборка и оба основных набора xUnit:

```powershell
dotnet restore Together.slnx --locked-mode
dotnet format Together.slnx --verify-no-changes --no-restore
dotnet build Together.slnx --configuration Release --no-restore
dotnet test tests/Together.Tests/Together.Tests.csproj --configuration Release --no-build --no-restore
dotnet test tests/Together.Api.Tests/Together.Api.Tests.csproj --configuration Release --no-build --no-restore
dotnet package list --project Together.slnx --include-transitive --vulnerable
```

Актуальный браузерный smoke выполняется на полном Compose-стенде. Перед первым
запуском установите браузеры Playwright:

```powershell
dotnet run --project tests/Together.BrowserTests -- --install
```

Подготовьте `.env` по [инструкции локального запуска](../backend_documentation.md#локальный-запуск),
затем выполните smoke:

```powershell
docker compose up --build -d
$env:APP_URL = 'http://localhost:8080'
dotnet run --project tests/Together.BrowserTests -- --backend-smoke
Remove-Item Env:APP_URL
```

Он проверяет регистрацию, три основных экрана, прямые ссылки и историю браузера,
независимость поездок, адаптивность навигации, загрузку серверного примера,
лимит/резерв и уточнения, сохранение черновика при 503 и 409, подтверждение отбрасывания правок, сохранение данных после
перезагрузки и выход. Все тестовые данные вымышлены. Результаты и скриншоты
находятся в [`docs/evidence`](evidence).

Команда без `--backend-smoke` запускает прежний набор для IndexedDB.
Его результаты сохранены в [архиве](history/README.md).

## Безопасность, мониторинг и логи

В проекте реализованы:

- antiforgery-защита изменяющих API-запросов;
- HttpOnly, SameSite и Secure cookies;
- CSP, HSTS и другие security headers;
- rate limiting и блокировка повторных неуспешных входов;
- проверка владения серверными данными;
- optimistic concurrency с ответом `409 Conflict`;
- аудит NuGet-зависимостей и CodeQL;
- JSON-логи в stdout с timestamp, level, category, Event ID и Trace ID;
- ограничение размера Docker logs;
- Docker health checks и проверка готовности приложения.

Отдельные документы:

- [аудит безопасности](security_audit.md);
- [AI-анализ синтетического JSON-лога](log_analysis.md);
- [документация интеграций](integration_documentation.md).

## Результаты и история разработки

Команды выше служат инструкцией для повторного запуска. Фактические результаты
и ограничения проверок приведены в [итоговом отчёте](../development_report.md).
Планы и отчёты прежних этапов сохранены в [архиве разработки](history/README.md).
