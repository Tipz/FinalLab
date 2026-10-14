# Аудит безопасности — домашнее задание № 6

> **Архивная версия аудита.** Датированные наблюдения и подтверждения
> сохранены. Действующие меры: [безопасность проекта](../security_audit.md);
> актуальные проверки: [итоговый отчёт](../../development_report.md).

Статус: аудит и подтверждённые исправления выполнены; остаточные риски и границы
проверок указаны явно.
Дата обзора: 20 сентября 2026 года; актуализация — 23 сентября 2026 года.

Документ различает автоматизированные результаты, подтверждение владельцем на
локальном стенде и проверки, не воспроизводимые из публичного репозитория.

Актуализация 14.10.2026: приложение доступно по публичному
[HTTPS-адресу](https://together.2-26-49-210.sslip.io/).
Автоматическое развёртывание выполняется через GitHub Actions и Komodo/Renovate.
Датированные findings ниже сохраняют контекст сентябрьского аудита.
Выпуск и проверки описаны в [документации развёртывания](../manual_production_deployment.md),
последующее исправление — в [отчёте OAuth](../yandex_oauth_configuration_fix.md).
Полный OAuth-вход с реальным аккаунтом после обновления не подтверждён.

## 1. Область аудита

Изучены:

- ASP.NET Core Minimal API и Identity configuration;
- API поездок и проверки владельца;
- Blazor WebAssembly rendering и API clients;
- EF Core model и PostgreSQL configuration;
- Dockerfile и оба Compose-файла;
- GitHub Actions workflows;
- env/appsettings templates;
- существующие unit, component, API и browser tests;
- требования `AGENTS.md`, технического задания и ДЗ № 6.

Границы аудита:

- DAST и penetration testing не выполнялись;
- конфигурация production-окружения не хранится в репозитории;
- реальные production logs не передавались AI и не публиковались;
- Dependency Review выполняется только для pull request и не запускался в
  последнем push run.

## 2. Уже существующие меры защиты

- Все endpoints поездок требуют authentication.
- Сервер фильтрует поездки по `OwnerId`.
- Обновления проверяют ожидаемую revision и возвращают conflict.
- EF Core используется без обнаруженного raw SQL.
- Пользовательский текст рендерится Blazor как текст; это покрыто component test.
- Внешние URL принимаются только как абсолютные `http`/`https` и открываются с
  `noopener noreferrer`.
- Authentication cookie настроена как HttpOnly и поддерживает Secure policy.
- CORS использует список разрешённых origins и credentials.
- Auth endpoints имеют fixed-window rate limit.
- Problem Details возвращает trace id вместо внутренних exception details.
- Контейнер запускается от непривилегированного пользователя и с read-only root
  filesystem в Compose.
- PostgreSQL не публикует порт в production Compose.
- Миграция отделена от запуска приложения.
- Data Protection keys сохраняются в отдельном volume.
- `.env` и `.env.*` исключены из Git, кроме шаблона.

## 3. Предварительные находки

Findings сопоставлены с кодом; исправления подтверждены указанными тестами,
успешным GitHub Actions run либо владельцем локального стенда.

### SA-01 — Antiforgery для изменяющих cookie-запросов

- **Предварительная severity:** High.
- **Статус:** Closed.
- **Компоненты:** `Together.Api/Program.cs`, `Together.Client/Storage/ApiHttp.cs`,
  auth и trips endpoints.
- **Наблюдение:** приложение использует автоматически отправляемую browser cookie,
  но antiforgery service/token validation не настроены. SameSite=Lax и CORS
  уменьшают поверхность атаки, но не являются полной заменой CSRF-защиты для всех
  same-site/proxy/deployment сценариев.
- **Исправление:** API выдаёт synchronizer token; `ApiHttp` прикладывает его ко
  всем POST/PUT/PATCH/DELETE, middleware валидирует token. OAuth сохраняет штатные
  state/correlation cookies.
- **Проверка:** integration tests отклоняют отсутствующий/неверный token и
  выполняют CRUD с валидным token; GitHub Actions backend smoke успешно прошёл.

### SA-02 — Слабая политика новых паролей

- **Предварительная severity:** Medium.
- **Статус:** Accepted для учебного проекта.
- **Компонент:** `Together.Api/Program.cs`.
- **Наблюдение:** минимальная длина равна пяти символам, требования к классам
  символов отключены. Rate limit частично снижает риск, но короткие пароли
  упрощают credential stuffing и offline guessing при компрометации хэшей.
- **Решение:** по требованиям учебного проекта сохранена минимальная длина пять
  символов без требований к их классам; пароль из пяти цифр допустим. Lockout —
  пять ошибок на пять минут. Существующие hashes не изменяются.
- **Проверка:** API tests принимают пароль из пяти цифр, отклоняют пароль из
  четырёх символов и проверяют блокировку входа.

### SA-03 — Известная fallback-строка подключения допустима вне Development

- **Предварительная severity:** High при прямом production-запуске.
- **Статус:** Closed.
- **Компонент:** `Together.Api/Program.cs`.
- **Наблюдение:** при отсутствии `ConnectionStrings:Together` используется строка
  с известным паролем `together_dev` независимо от окружения.
- **Исправление:** fallback разрешён только в Development; другие окружения
  завершают запуск без вывода строки подключения.
- **Проверка:** запуск с `ASPNETCORE_ENVIRONMENT=Production` без
  `ConnectionStrings__Together` завершился ожидаемой конфигурационной ошибкой.
- **Проверка исправления:** startup test для Production без connection string и
  нормальный запуск с environment secret.

### SA-04 — Небезопасные значения в deployment env-шаблоне

- **Предварительная severity:** Medium.
- **Статус:** Partially closed; bind зависит от топологии стенда.
- **Компонент:** `deploy.env.example`.
- **Наблюдение:** исходный шаблон содержал `POSTGRES_PASSWORD=together`, bind на
  `0.0.0.0` и `SECURE_COOKIES=false`. Копирование такого шаблона без
  редактирования создавало небезопасный стенд.
- **Исправление:** обязательные значения оставлены пустыми, secure cookies —
  `true`, image требует точный digest. Текущий шаблон задаёт bind `0.0.0.0` для
  доступа из LAN; документация требует firewall. Для proxy на том же хосте
  рекомендуется `127.0.0.1`.
- **Проверка исправления:** `docker compose config` с production template и review
  итоговых port/cookie settings.

### SA-05 — GitHub Actions не закреплены по immutable commit SHA

- **Предварительная severity:** Medium.
- **Статус:** Closed.
- **Компоненты:** `.github/workflows/*.yml`.
- **Наблюдение:** сторонние actions подключаются по изменяемым major tags.
- **Исправление:** все сторонние actions закреплены по полным SHA; `packages:
  write` выдан только publish job.
- **Проверка исправления:** review workflow permissions и отсутствие `uses:` с
  изменяемым tag для стороннего action.

### SA-06 — Publish не имеет общего обязательного gate с CI и browser smoke

- **Предварительная severity:** Medium для supply chain/release integrity.
- **Статус:** Closed.
- **Компоненты:** `.github/workflows/*.yml`.
- **Наблюдение:** три workflow независимо запускаются на push. Publish может
  завершиться, даже если отдельный CI workflow упал. Дополнительно они слушают
  `master`, тогда как рабочая ветка называется `main`.
- **Исправление:** один workflow связывает quality → build/tests → backend smoke →
  publish → smoke опубликованного digest. Автоматическое production-развёртывание
  обеспечивают Komodo/Renovate.
- **Проверка исправления:** [успешный GitHub Actions run](https://github.com/Tipz/HT6/actions/runs/35828731866)
  подтверждает обязательную последовательность до опубликованного image.

### SA-07 — Production proxy/security headers не зафиксированы

- **Предварительная severity:** Medium.
- **Статус:** Partially closed; инфраструктурная конфигурация внешняя.
- **Компоненты:** API middleware, reverse proxy и deployment documentation.
- **Наблюдение:** production Compose ожидает внешний HTTPS proxy, но его
  конфигурации в репозитории нет. Не подтверждены forwarded headers, HSTS, CSP и
  другие response headers.
- **Исправление:** HSTS/security headers/CSP добавлены; forwarded headers включаются
  только для явно заданных `KnownProxies`. Владелец подтвердил HTTPS и OAuth.
- **Остаточная проверка:** конфигурация окружения и снимок production response
  headers не входят в репозиторий; их нужно проверять на целевом хосте.

### SA-08 — Глобальный auth rate limit может стать причиной отказа в обслуживании

- **Предварительная severity:** Low/Medium.
- **Статус:** Closed locally.
- **Компонент:** `Together.Api/Program.cs`.
- **Наблюдение:** fixed-window limiter зарегистрирован без видимого partition key;
  лимит может разделяться всеми пользователями экземпляра.
- **Исправление:** fixed-window limiter разделён по client IP и сочетается с
  Identity lockout; forwarded IP принимается только от trusted proxy.
- **Проверка:** API-тесты подтверждают `429` после исчерпания окна и блокировку
  password account после пяти неуспешных попыток.
- **Проверка исправления:** integration/load test независимых клиентов и тест
  возврата `429`.

### SA-09 — Отсутствует автоматический vulnerability/SAST gate

- **Предварительная severity:** Medium.
- **Статус:** с 12.10.2026 NuGet и CodeQL gates обязательны. CodeQL запускается первым, ошибки и неподавленные SARIF findings блокируют дальнейшие jobs. Настройка required checks для запрета merge остаётся отдельной настройкой репозитория.
- **Компоненты:** GitHub Actions и repository settings.
- **Наблюдение:** build может обнаружить часть NuGet audit warnings, но отдельного
  отчёта транзитивных уязвимостей, dependency review и CodeQL нет.
- **Исправление:** NuGet JSON gate падает при наличии vulnerability и ошибке команды;
  CodeQL анализирует C#/JavaScript, после анализа выполняется блокирующая проверка SARIF.
  Необязательный Dependency Review удалён; аудит NuGet сохранён для всех запусков.
- **Проверка:** NuGet audit и CodeQL успешно прошли в
  [GitHub Actions](https://github.com/Tipz/HT6/actions/runs/35828731866).
  В том сентябрьском запуске Dependency Review был пропущен для push.
  Новая конфигурация проверена actionlint и локальными сценариями SARIF gate;
  полный CodeQL на GitHub после ревизии не запускался. [Результаты](../ci_revision.md).

### SA-10 — OAuth credential в отслеживаемом env-шаблоне

- **Предварительная severity:** High, если значение было действующим.
- **Статус:** Значения удалены из текущего шаблона; ротация зависит от владельца.
- **Компонент:** `.env.example` и Git history.
- **Наблюдение:** шаблон содержал конкретные значения Client ID и Client Secret,
  похожие на credential реального OAuth-приложения.
- **Исправление:** в `.env.example` оставлены пустые placeholders; рабочие значения
  должны находиться только в исключённом из Git `.env`/`.env.deploy` или secret
  store.
- **Обязательное действие:** если прежний Client Secret когда-либо был
  действующим, его необходимо отозвать и выпустить заново. Удаление из текущего
  файла не удаляет значение из истории Git.

## 4. OAuth threat model и реализованные меры

Единственный разрешённый provider — Яндекс ID. Используется authorization code
flow через backend; OAuth token не передаётся в Blazor bundle и не записывается в
логи. Запрашивается только `login:email`; устойчивый `id` входит в стандартный
ответ API Яндекс ID. Для тестирования и production используются разные OAuth
приложения.
Redirect URI публичного сайта:
`https://together.2-26-49-210.sslip.io/signin-yandex`.
Scheme, host, port и path должны совпадать с настройками OAuth-приложения.
Для другого окружения требуется новая конфигурация URL и Redirect URI.

Следующие риски должны быть закрыты дизайном:

- open redirect через `returnUrl`;
- login CSRF и отсутствие проверки state/correlation;
- account takeover при автоматическом связывании по email;
- принятие неподтверждённого email;
- утечка Client Secret в Blazor bundle или logs;
- утечка provider access/refresh token;
- неверный callback URL за reverse proxy;
- подробная provider error-информация в пользовательском ответе;
- отсутствие rate limit на начало OAuth flow.

Для текущего сценария provider token не нужен после аутентификации и не должен
сохраняться.

## 5. Аналитика и privacy threat model

- Тег Яндекс Метрики нельзя загружать до согласия.
- Page location должен передаваться без query и fragment.
- API аналитики должен принимать только фиксированные цели и параметры.
- Нельзя передавать данные моделей, email, Identity user id и exception text.
- Снимки DevTools/DebugView используют только вымышленные данные.
- Отзыв consent должен останавливать новые события.
- Вебвизор, карты, e-commerce, user parameters и session parameters отключены.
- CSP allowlist ограничивается фактически используемыми доменами Яндекс Метрики;
  нельзя добавлять широкие wildcard-разрешения.

## 6. Автоматизированные проверки

```powershell
dotnet restore Together.slnx --locked-mode
dotnet format Together.slnx --verify-no-changes
dotnet build Together.slnx -c Release --no-restore
dotnet test tests/Together.Tests/Together.Tests.csproj -c Release
dotnet test tests/Together.Api.Tests/Together.Api.Tests.csproj -c Release
dotnet package list --project Together.slnx --include-transitive --vulnerable
```

Дополнительно реализованы:

- CodeQL для C# и JavaScript;
- GitHub dependency review;
- Compose/Chromium backend smoke;
- antiforgery positive/negative tests;
- OAuth callback tests с тестовой authentication scheme;
- проверка JSON-логов на секреты и персональные данные;
- ручной OWASP Top 10 checklist.

Production TLS подтверждён владельцем. Конфигурация окружения и воспроизводимый снимок всех
response headers в репозиторий не включены.

## 7. Использование AI в аудите

AI использован для предварительного review конфигурации, определения trust
boundaries и сопоставления кода с категориями OWASP. Каждый вывод выше основан на
конкретном участке репозитория, но severity и эксплуатируемость должны быть
подтверждены тестом или документированным анализом.

В репозитории сохранены:

- промпты в `docs/prompt_templates.md`;
- обезличенные входные данные;
- вывод AI;
- решение разработчика: принято, отклонено или требует проверки;
- команду/тест, подтвердившие решение.

Секреты, реальные логи пользователей и данные поездок нельзя передавать
AI-сервисам.

## 8. Реестр результатов

Таблица заполняется только после фактических проверок.

| Проверка | Дата | Результат | Evidence/команда |
| --- | --- | --- | --- |
| NuGet direct/transitive audit | 2026-09-20 | Passed, 0 vulnerable packages | `dotnet package list --project Together.slnx --include-transitive --vulnerable --format json` |
| CodeQL | 2026-09-23 | Passed | [GitHub Actions job](https://github.com/Tipz/HT6/actions/runs/35828731866) |
| Dependency review | 2026-09-23 | Skipped for push | Запускается только для pull request |
| OWASP manual review | 2026-09-20 | Completed locally | Раздел 3, SA-01–SA-09 |
| Antiforgery tests | 2026-09-20 | Passed | `Together.Api.Tests`, missing/invalid/valid token |
| OAuth security tests | 2026-09-20 | Passed with fake scheme | success/cancel/repeat/email conflict/open redirect/password login |
| Production TLS/OAuth | 2026-09-23 | Подтверждено владельцем | Конфигурация окружения управляется отдельно |
| Compose/Chromium smoke | 2026-09-23 | Passed | [GitHub Actions job](https://github.com/Tipz/HT6/actions/runs/35828731866) |
| Log privacy review | 2026-09-20 | Passed on synthetic/local sample | `GET /health?forbidden-marker`: JSON `Together.Request` contained only `/health`; `docs/log_analysis.md`; production logs were not used |

## 9. Критерии закрытия аудита

- Все Critical/High findings исправлены либо имеют явное утверждённое принятие
  риска.
- Medium findings имеют исправление, срок или обоснование.
- Dependency и SAST checks встроены в CI.
- CSRF, OAuth, ownership и open redirect покрыты тестами.
- Production TLS/OAuth проверены владельцем;
  proxy-конфигурация остаётся внешней по отношению к репозиторию.
- Логи и аналитика не содержат запрещённых данных.
- Реестр результатов содержит реальные даты, команды и ссылки на evidence.
- README и integration documentation не противоречат фактической конфигурации.
