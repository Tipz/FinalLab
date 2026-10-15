# Итоговый отчёт проекта «Вместе в путь»

Срез на 14.10.2026. Репозиторий: [Tipz/FinalLab](https://github.com/Tipz/FinalLab).
Версия приложения, проверенная CI: `c3db2bd53ec05f76e145b6ead73efd4e10ce4983`.
Стенд: **[together.2-26-49-210.sslip.io](https://together.2-26-49-210.sslip.io/)**.

Дополнение от 15.10.2026: [анализ конкурентов с AI](docs/competitor_analysis.md) —
сравнение Wanderlog, TripIt, TravelSpend и Яндекс Путешествий, официальные
источники, использованный промпт и выводы для «Вместе в путь».

## Итоговые возможности

Приложение помогает семье вручную собрать предложения и сравнить направления
и жильё. Реализованы несколько независимых поездок, параметры семьи и даты,
создание, изменение и удаление вариантов, сравнение по семи критериям.
Бюджет состоит из шести статей, различает неизвестные расходы и ноль;
лимит, резерв и список уточнений помогают оценить полноту расчёта.

Код содержит три адаптивных экрана: «Мои поездки», «Варианты поездки» и
«Сравнение». Доступны регистрация и вход по паролю, дополнительный вход через
Яндекс ID, серверное хранение с изоляцией владельцев и добровольный импорт
IndexedDB. Ошибки сохранения и конфликт `409` сохраняют черновик.
Метрика загружается после согласия; данные поездок сторонним сервисам не передаются.

Стек: C#/.NET 10, standalone Blazor WebAssembly, MudBlazor, Minimal API,
Identity, EF Core/Npgsql и PostgreSQL. GitHub Actions проверяет и публикует
`ghcr.io/tipz/together` для AMD64/ARM64; Komodo/Renovate управляют обновлением
и deployment. Миграции выполняются отдельным заданием, Data Protection keys
хранятся в отдельном volume.

## Текущий CI

[CI and publish #8](https://github.com/Tipz/FinalLab/actions/runs/38630705095),
push в `main`, SHA указан выше: **успех**. Состояние проверено
14.10.2026, 23:27 МСК; запуск завершился в 23:27 МСК:

| Этап | Результат |
| --- | --- |
| CodeQL: C# и JavaScript | Успех |
| Locked restore, format, NuGet audit | Успех |
| Release build, Core/UI и API xUnit | Успех |
| Compose/PostgreSQL и Chromium `--backend-smoke` | Успех |
| Публикация AMD64/ARM64 и smoke опубликованного digest | Успех |

Запуск для тега `v1.0.0` от 14.10.2026 [#6 завершился ошибкой](https://github.com/Tipz/FinalLab/actions/runs/38581341191).
Статус `main` и результат выпуска по тегу учитываются отдельно.

Текущая конфигурация workflow (`0829bb01ed4e68e373c0c43bf6828e42ee39b0cd`)
запускает автоматический выпуск только по тегам `vMAJOR.MINOR.PATCH`
на коммитах из истории `main`; PR и ручной запуск сохраняются.
Последний доступный run #8 предшествует этой правке и не подтверждает её.

## Матрица критериев

Основа: [ТЗ](docs/technical_specification.md), [критерии историй](docs/user_stories.md)
и [backend-требования](docs/backend_requirements.md). «Реализовано» относится
к указанному SHA; столбец проверки указывает покрывающий набор, а не утверждает
отдельное исполнение каждого Given–When–Then на публичном стенде.

| Критерий | Итог | Подтверждение |
| --- | --- | --- |
| AC-01 / US-01: параметры семьи, даты и ночи | Реализовано | [Валидация](tests/Together.Tests/ValidationTests.cs), CI backend smoke |
| AC-02 / US-02: добавление направления и жилья | Реализовано | [Создание/чтение API](tests/Together.Api.Tests/TripsApiTests.cs), CI backend smoke |
| AC-03 / US-03: правки, отмена и пересчёт | Реализовано | [Компоненты](tests/Together.Tests/ComponentTests.cs), навигация, CI backend smoke |
| AC-04 / US-04: удаление с подтверждением | Реализовано | [API](src/Together.Api/Endpoints/TripsEndpoints.cs); прежняя [браузерная проверка](docs/history/development_report.md) |
| AC-05 / US-05: шесть статей и точные копейки | Реализовано | [Расчёты бюджета](tests/Together.Tests/BudgetTests.cs), компоненты |
| AC-06 / US-06: неизвестные суммы и характеристики | Реализовано | Расчёты бюджета, API round-trip `null`/`0` |
| AC-07 / US-07: минимум два варианта, независимость поездок | Реализовано | [Навигация](tests/Together.Tests/ScreenNavigationTests.cs), CI backend smoke |
| AC-08 / US-08: семь переключаемых критериев | Реализовано | Навигация, API, CI backend smoke |
| AC-09 / US-09: мобильная таблица и клавиатура | Реализовано | CI backend smoke; прежние [проверки интерфейса](docs/screen_navigation.md) |
| AC-10 / US-10: вход, сохранение, импорт и черновик при отказе/409 | Реализовано | API, CI backend smoke; прежняя [проверка импорта](docs/history/development_report.md) |
| US-14: лимит и резерв; US-15: уточнения | Реализовано | [Бюджетное планирование](tests/Together.Tests/BudgetPlanningTests.cs), API, CI backend smoke |
| Владение, antiforgery, безопасные ссылки, OAuth и consent | Реализовано | [API-тесты](tests/Together.Api.Tests), [аналитика](tests/Together.Tests/AnalyticsTests.cs), валидация |
| CI, multi-platform image, health checks и логи | Реализовано | Текущий CI, [интеграции](docs/integration_documentation.md), [аудит](docs/security_audit.md) |
| US-11–US-13: копирование, сортировка, отметка выбранного | Отложено | В утверждённый обязательный объём не входят |

## Проверки рабочей копии и ограничения

14.10.2026, Windows, исходники приложения указанной версии.
Последующий коммит `0829bb0` меняет только workflow. В этой задаче обновлена документация:

| Фактически выполненная команда | Результат |
| --- | --- |
| `dotnet build Together.slnx --configuration Release --no-restore --nologo` | Успех, 0 предупреждений и ошибок |
| `dotnet test tests/Together.Tests/Together.Tests.csproj --configuration Release --no-build --no-restore --nologo` | 66/66 |
| `dotnet test tests/Together.Api.Tests/Together.Api.Tests.csproj --configuration Release --no-build --no-restore --nologo` | 25/25 |
| `dotnet run --project tests/Together.BrowserTests --configuration Release --no-build --no-restore -- --backend-smoke` | Оба запуска на публичном стенде завершились ошибкой: не найден заголовок «Мои поездки» |
| `curl.exe --fail --silent --show-error --max-time 30 --output NUL --write-out 'HTTP %{http_code}' https://together.2-26-49-210.sslip.io/health/ready` | HTTP 200 |

Для smoke заданы `APP_URL=https://together.2-26-49-210.sslip.io` и
`BROWSER_EVIDENCE_DIR=artifacts/final-project-docs-20261014`; использованы
отдельная тестовая учётная запись и вымышленные данные. После входа стенд показал
прежний единый экран «Все варианты. Все расходы.». Соответствие стенда текущему
коду не подтверждено. Повтор после завершения CI в 23:31 МСК дал ту же ошибку;
evidence сохранён отдельно в `artifacts/final-project-docs-20261014-after-ci`.
Требуется обновление deployment и повторный smoke.

API xUnit использует EF InMemory и тестовую OAuth-схему; реальный PostgreSQL
проверяется в CI smoke. В этой проверке реальный вход через Яндекс ID,
поступление событий в Метрику и масштаб 200% отдельно не проверялись.
Настройки Komodo/Renovate вне репозитория не инспектировались.

Проверки Markdown-ссылок и `git diff --check` фиксируются в
[журнале](docs/history/README.md#документация-финального-проекта-14102026).

Запуск и эксплуатация: [backend](backend_documentation.md),
[руководство разработчика](docs/development.md),
[развёртывание](docs/manual_production_deployment.md).
Прежние задания, отчёты и датированные результаты: [архив](docs/history/README.md).

## Перераспределение документации — 15.10.2026

Главный README сокращён до описания продукта, возможностей, скриншота,
краткого стека и ссылок. Команды локального запуска перенесены в backend-документацию,
CI и настройки Яндекса — в документацию интеграций, самостоятельный deployment —
в руководство развёртывания. Структура решения и команды проверок собраны в
[руководстве разработчика](docs/development.md). Индекс и ссылки обновлены.

Проверки выполнены на Windows с SDK `10.0.303`. Обычная Release-сборка
не завершилась: DLL заняты уже запущенным локальным API. Процесс не останавливался;
повторная сборка и xUnit выполнены в отдельном временном каталоге:

```powershell
$taskArtifacts = Join-Path $env:TEMP 'together-docs-verification-20261015'
```

| Фактически выполненная команда / проверка | Результат |
| --- | --- |
| `dotnet restore Together.slnx --locked-mode --nologo` | Успех |
| `dotnet build Together.slnx --configuration Release --artifacts-path $taskArtifacts --nologo -p:RestoreLockedMode=true` | Успех, 0 предупреждений и ошибок |
| `dotnet test tests/Together.Tests/Together.Tests.csproj --configuration Release --no-build --no-restore --artifacts-path $taskArtifacts --nologo` | 70/70 |
| `dotnet test tests/Together.Api.Tests/Together.Api.Tests.csproj --configuration Release --no-build --no-restore --artifacts-path $taskArtifacts --nologo` | 25/25 |
| `dotnet (Join-Path $taskArtifacts 'bin/Together.BrowserTests/release/Together.BrowserTests.dll') --backend-smoke` | Успех на уже запущенном локальном стенде |
| Проверка локальных Markdown-ссылок, якорей и парности ограждений кода в восьми изменённых документах | Успех |
| `git -c core.safecrlf=false diff --check` | Успех |

Для smoke заданы `APP_URL=http://localhost:5198` и
`BROWSER_EVIDENCE_DIR=Join-Path $taskArtifacts 'browser-evidence'`; использованы
вымышленные данные. Этот запуск проверяет существующий локальный стенд,
а не приложение из временного каталога сборки. Docker отсутствует в PATH:
Compose, миграции контейнерного стенда, публикация и production deployment
в рамках этой редакции документации не выполнялись. Публичный сайт и реальные
интеграции Яндекса повторно не проверялись.
