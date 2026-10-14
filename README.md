# Вместе в путь

[![CI and publish](https://github.com/Tipz/HT6/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/Tipz/HT6/actions/workflows/ci.yml)

«Вместе в путь» — учебное клиент-серверное приложение для планирования и
сравнения семейных поездок. Пользователь может вести несколько поездок,
сравнивать варианты размещения и направлений, рассчитывать бюджет и хранить
данные в своей учётной записи.

Публичный сайт: **[together.2-26-49-210.sslip.io](https://together.2-26-49-210.sslip.io/)**.

Текущая версия выполнена в рамках домашнего задания № 6. К приложению добавлены
CI/CD, аудит безопасности, OAuth 2.0 через Яндекс ID, Яндекс Метрика с явным
согласием, health checks и структурированные JSON-логи. Вход по email и паролю
сохранён. Платёжный сервис не используется, поскольку он является опциональным и
не требуется предметной области проекта.

![Основной экран приложения с вымышленными данными](docs/evidence/chromium-1440.png)

Документация актуализирована 14.10.2026: [индекс](docs/README.md), [оставшиеся работы](docs/IMPLEMENTATION_PLAN.md), [исторический журнал](docs/history/README.md). Бюджетное дополнение развёрнуто 13.10.2026; схема публичного доступа и проверки описаны в [отчёте о развёртывании](docs/manual_production_deployment.md).

## Возможности

- регистрация и вход по паролю или через Яндекс ID;
- несколько независимых поездок с датами, составом семьи и возрастом детей;
- создание, редактирование и удаление вариантов направления и жилья;
- шесть статей бюджета с расчётом в целых копейках;
- лимит бюджета поездки и резерв на непредвиденные расходы, с показом остатка или превышения;
- список уточнений по отсутствующим расходам и выбранным критериям, с переходом к нужному полю;
- различение неизвестной суммы и нулевого расхода;
- сравнение двух и более вариантов по семи переключаемым критериям;
- серверное хранение данных в PostgreSQL с изоляцией пользователей;
- защита от потери изменений при сетевой ошибке или конфликте версий;
- добровольный импорт данных старой IndexedDB-версии без удаления локальной
  копии;
- адаптивный русскоязычный интерфейс на MudBlazor.

Основной экран следует концепции
[«План поездки»](docs/ui_concepts/03-analytical.html): холодная сине-серая
палитра, блок сравнения перед карточками и боковая навигация на широком экране.

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
[документации бюджетного планирования](docs/budget_planning.md).

## Быстрый запуск через Docker Compose

Потребуются Docker Engine и Docker Compose. Из корня репозитория:

```powershell
Copy-Item .env.example .env
```

Перед запуском обязательно задайте в `.env` новый длинный пароль PostgreSQL. Для
изолированного локального HTTP-стенда также установите:

```dotenv
POSTGRES_PASSWORD=replace-with-a-long-random-password
SECURE_COOKIES=false
```

Запустите приложение:

```powershell
docker compose up --build -d
docker compose ps
```

После успешного завершения контейнера `migrate` приложение доступно по адресу
<http://localhost:8080>. Состояние готовности можно проверить запросом
<http://localhost:8080/health/ready>.

Остановка без удаления данных:

```powershell
docker compose down
```

Файл `.env` исключён из Git. Не сохраняйте пароли, OAuth Client Secret и строки
подключения в `appsettings.json`, Docker image, логах или Blazor bundle.

## Запуск для разработки без Docker

Потребуются:

- .NET SDK **10.0.303**, зафиксированный в [`global.json`](global.json);
- PostgreSQL;
- два терминала для API и клиента.

Подготовьте строку подключения `ConnectionStrings__Together`, затем восстановите
инструменты и примените миграции:

```powershell
dotnet restore Together.slnx --locked-mode
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/Together.Api
```

Запустите API:

```powershell
dotnet run --project src/Together.Api
```

В другом терминале запустите клиент:

```powershell
dotnet run --project src/Together.Client
```

Клиент будет доступен по адресу <http://localhost:5180>, API — по адресу
<http://localhost:5182>. Для разработки клиента с hot reload можно использовать:

```powershell
dotnet watch --project src/Together.Client
```

Node.js и npm для сборки приложения не требуются.

## Яндекс ID

OAuth включается только при наличии обеих server-side настроек:

```text
Authentication__Yandex__ClientId
Authentication__Yandex__ClientSecret
```

При запуске через Compose им соответствуют `YANDEX_CLIENT_ID` и
`YANDEX_CLIENT_SECRET`. Callback приложения:

```text
<APP_URL>/signin-yandex
```

Этот точный URI необходимо зарегистрировать в консоли Яндекс ID. Backend
использует Authorization Code Flow с PKCE, запрашивает только `login:email`, не
передаёт access token в Blazor и не сохраняет его после входа.

Callback публичного сайта:
`https://together.2-26-49-210.sslip.io/signin-yandex`.
Исправление OAuth развёрнуто 13.10.2026;
полный вход с реальным аккаунтом Яндекса после обновления требует проверки
пользователем. Подробности: [отчёт OAuth](docs/yandex_oauth_configuration_fix.md).
При переносе необходимо настроить собственные URL, HTTPS, Client ID,
Client Secret и Redirect URI.

## Яндекс Метрика

Метрика включается настройкой `YandexMetrika__CounterId`, которой в Compose
соответствует `YANDEX_METRIKA_COUNTER_ID`.

- скрипт Метрики не загружается до явного согласия пользователя;
- согласие можно отозвать;
- в Метрику передаются только очищенные SPA paths без query string и fragment;
- разрешены цели `login_succeeded` с методом `password` или `yandex` и
  `trip_created`;
- email, идентификатор пользователя, поездки, возраст детей и заметки не
  передаются.

Интеграция проверена с настроенным счётчиком на локальном стенде. В другом
окружении необходимо создать собственный счётчик и передать его номер приложению.

## CI и публикация образа

Результаты ревизии этапов и локальных проверок: [docs/ci_revision.md](docs/ci_revision.md).

Workflow [`.github/workflows/ci.yml`](.github/workflows/ci.yml) запускается для
pull request и push в `main`, push тегов `v*`, а также вручную (`workflow_dispatch`).

Последовательность проверок:

```text
CodeQL (C# и JavaScript, блокирующий)
  → locked restore
  → format и NuGet audit
  → Release build и xUnit
  → Docker Compose и Chromium backend smoke
  → multi-platform publish
  → smoke опубликованного digest
```

CodeQL — первый обязательный job: ошибка анализа, загрузки результатов или любое
неподавленное замечание в SARIF останавливают последующие jobs. Для C# используется
явная сборка с SDK из `global.json`, включая сгенерированный Razor-код. SARIF
сохраняется в артефакт `codeql-results`; замечания доступны также в Code Scanning.
Code Scanning должен быть доступен и настроен для advanced setup в репозитории.

Format, аудит прямых и транзитивных NuGet-зависимостей, Release-сборка, оба
xUnit-проекта и актуальный `--backend-smoke` объединены в один job `quality`:
один restore и одна сборка решения после CodeQL. Отдельный необязательный
Dependency Review удалён; аудит NuGet остаётся обязательным на всех запусках.
Он проверяет уязвимости пакетов, но не заменяет проверку лицензий Dependency Review.
Браузерные артефакты берутся из каталога текущего запуска, а не из старых `docs/evidence`.
Smoke опубликованного digest сохранён: он проверяет запуск образа из GHCR
через deployment Compose, включая отдельное выполнение миграций.

Для запрета слияния PR настройте required status checks `CodeQL` и
`Format, audit, Release, xUnit and backend smoke` в ruleset/branch protection;
workflow сам по себе блокирует дальнейшие jobs и публикацию, но не кнопку merge.

Pull request выполняет проверки, но не публикует образ. Ручной запуск публикует
только из `main`. После успешного push в
`main` workflow публикует образ для `linux/amd64` и `linux/arm64` в
[GitHub Container Registry](https://github.com/Tipz/HT6/pkgs/container/together):

```text
ghcr.io/tipz/together
```

Доступны теги `latest`, `main`, `sha-<полный SHA>` и `v*` для Git-тегов.
Production deployment рекомендуется выполнять по неизменяемой ссылке
`ghcr.io/tipz/together@sha256:...`.

Автоматическое развёртывание обеспечивают GitHub Actions и Komodo/Renovate.
GitHub Actions выполняет проверки и публикует образ в GHCR;
Komodo/Renovate обеспечивают обновление образа и deployment приложения.
Настройки Komodo/Renovate управляются вне этого репозитория.

## Production deployment готового образа

Публичный стенд доступен по адресу
[https://together.2-26-49-210.sslip.io/](https://together.2-26-49-210.sslip.io/).
Автоматический выпуск через GitHub Actions и Komodo/Renovate описан в
[документации развёртывания](docs/manual_production_deployment.md).
Ниже приведены команды для самостоятельного запуска опубликованного GHCR-образа.

Для развёртывания без исходников и локальной сборки используйте
[`docker-compose.deploy.yml`](docker-compose.deploy.yml):

```bash
cp deploy.env.example .env.deploy
# PowerShell: Copy-Item deploy.env.example .env.deploy
```

Заполните как минимум:

```dotenv
TOGETHER_IMAGE=ghcr.io/tipz/together@sha256:replace-with-published-digest
POSTGRES_PASSWORD=replace-with-a-long-random-password
```

Для OAuth и аналитики также задайте:

```dotenv
YANDEX_CLIENT_ID=
YANDEX_CLIENT_SECRET=
YANDEX_METRIKA_COUNTER_ID=
```

Проверка и запуск:

```bash
docker compose --env-file .env.deploy --file docker-compose.deploy.yml config --quiet
docker compose --env-file .env.deploy --file docker-compose.deploy.yml up -d
docker compose --env-file .env.deploy --file docker-compose.deploy.yml ps
```

Compose:

- загружает опубликованные образы приложения и PostgreSQL;
- ожидает готовности PostgreSQL;
- выполняет EF Core migrations отдельным одноразовым контейнером;
- запускает приложение только после успешной миграции;
- сохраняет PostgreSQL и Data Protection keys в отдельных volumes;
- запускает приложение от пользователя `app` с read-only root filesystem.

Сам Compose по умолчанию использует `127.0.0.1:8080`, но текущий
`deploy.env.example` задаёт `APP_BIND_ADDRESS=0.0.0.0` для доступа из локальной
сети. В этом режиме ограничьте порт firewall доверенной LAN. Если reverse proxy
работает на том же хосте, безопаснее установить `APP_BIND_ADDRESS=127.0.0.1`.
В production используйте HTTPS и `SECURE_COOKIES=true`; задайте точный
`TRUSTED_PROXY_IP` и включите `USE_HTTPS_REDIRECTION` только после корректной
передачи `X-Forwarded-Proto`.

Проверки состояния:

- `/health` — liveness процесса;
- `/health/ready` — readiness приложения с проверкой PostgreSQL.

Остановка без удаления данных:

```bash
docker compose --env-file .env.deploy --file docker-compose.deploy.yml down
```

Не используйте `down --volumes`, если не требуется намеренно удалить базу и
ключи cookie. Перед обновлением и изменением схемы базы сделайте резервную копию.
Подробная инструкция находится в
[backend_documentation.md](backend_documentation.md).

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

- [аудит безопасности](docs/security_audit.md);
- [AI-анализ синтетического JSON-лога](docs/log_analysis.md);
- [документация интеграций](docs/integration_documentation.md).

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

Актуальный браузерный smoke выполняется на полном Compose-стенде:

```powershell
docker compose up --build -d
$env:APP_URL = 'http://localhost:8080'
dotnet run --project tests/Together.BrowserTests -- --backend-smoke
Remove-Item Env:APP_URL
```

Он проверяет регистрацию, загрузку серверного примера, лимит/резерв и уточнения, сохранение черновика при 503 и 409, подтверждение отбрасывания правок, сохранение данных после
перезагрузки и выход. Все тестовые данные вымышлены. Результаты и скриншоты
находятся в [`docs/evidence`](docs/evidence).

Набор без `--backend-smoke` относится к исторической IndexedDB-версии ДЗ № 4 и
не является проверкой текущего backend.

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

## Документация

- [Отчёт о выполнении ДЗ № 6](docs/homework_6_report.md)
- [Материалы сдачи ДЗ № 6](SUBMISSION.md)
- [Документация интеграций](docs/integration_documentation.md)
- [Аудит безопасности](docs/security_audit.md)
- [Backend и эксплуатация](backend_documentation.md)
- [Публичный сайт и автоматическое развёртывание](docs/manual_production_deployment.md)
- [Исправление и развёртывание Яндекс OAuth](docs/yandex_oauth_configuration_fix.md)
- [Техническое задание](docs/technical_specification.md)
- [Требования backend](docs/backend_requirements.md)
- [Пользовательские истории](docs/user_stories.md)
- [AI-анализ логов](docs/log_analysis.md)
- [Шаблоны AI-промптов](docs/prompt_templates.md)

Приложение доступно по публичному HTTPS-адресу; репозиторий и опубликованные
multi-platform образы доступны через GitHub и GHCR.
