# Ревизия GitHub Actions — 12 октября 2026

Изменены `.github/workflows/ci.yml`, добавлен
`.github/scripts/Test-CodeQlResults.ps1`, обновлён раздел CI в README.
Описания pipeline синхронизированы также в документах сдачи, backend,
интеграций, отчёте ДЗ № 6 и аудите безопасности. Историческая сверка явно
отделена от более поздней ревизии workflow.

## Последовательность

`codeql → quality → publish`. Других независимых jobs нет.
CodeQL не допускает `continue-on-error`: сбой подготовки, анализа, загрузки
результатов или проверки SARIF блокирует последующие jobs.
Проверка SARIF отклоняет любые findings независимо от severity; записи
с явно принятым suppression и записи с `kind`, отличным от `fail`, не учитываются.
Отсутствующие или некорректные отчёты и неуспешные invocations также отклоняются.
GitHub-dismissed alerts автоматически не исключаются из локального SARIF gate.

Используется manual build с SDK из `global.json`, чтобы извлекать и
сгенерированный Razor-код. Эта сборка нужна для CodeQL; Release-сборка в
`quality` выполняется отдельно в другом runner. Перенос `bin/obj` между runners
не добавлен: это усложняет воспроизводимость ради экономии одной сборки.

Настройки `analyze` сверены с
[action.yml закреплённого CodeQL Action](https://github.com/github/codeql-action/blob/1c5b675653bb5c22dbe9b12b556ec555138e09fd/analyze/action.yml).

## Ревизия остальных этапов

| Этап | Решение и причина |
| --- | --- |
| Locked restore | Сохранён: воспроизводимые версии из lock-файлов |
| Format | Сохранён: соблюдение стиля без изменения файлов |
| NuGet audit | Сохранён для прямых и транзитивных зависимостей; ошибка команды блокирует job; добавлен `--no-restore` |
| Dependency Review | Удалён необязательный PR job; обязательный NuGet audit проверяет уязвимости на каждом запуске, проверки лицензий не было настроено |
| Release и оба xUnit-проекта | Сохранены: компиляция, Core/UI и API имеют отдельное покрытие |
| Backend smoke | Сохранён: браузер, backend, PostgreSQL, миграции и контейнеры; исторический IndexedDB-набор не запускается |
| Отдельные runners quality/build_test/backend_smoke | Объединены в `quality`: убраны два checkout/setup, два явных restore и повторная сборка решения перед smoke |
| Браузерные артефакты | Только каталог текущего запуска в runner temp; старые `docs/evidence` не выдаются за результат CI |
| GHCR multi-platform publish | Сохранён для amd64/arm64; PR не публикует, ручная публикация разрешена из main |
| Smoke опубликованного digest | Сохранён: проверяет полученный из GHCR образ через deployment Compose, а не локальную сборку |
| Неиспользуемый job output image_ref | Удалён; digest выводится в job summary для оператора |

SHA используемых Actions, registry, платформы, Buildx cache, SBOM и provenance
сохранены. Production deployment автоматически не запускается.

## Выполненные локальные проверки

Проверки выполнены на Windows, SDK `10.0.303`, с текущими изменениями пользователя
в рабочем дереве. Их исходные файлы не исправлялись в рамках ревизии CI.

| Команда / проверка | Результат |
| --- | --- |
| `dotnet restore Together.slnx --locked-mode` | Успешно для всех 7 проектов |
| `dotnet build Together.slnx --configuration Release --no-restore` | Успешно, 0 предупреждений, 0 ошибок |
| `dotnet test tests/Together.Tests/Together.Tests.csproj --configuration Release --no-build --no-restore` | 58 пройдено, 0 пропущено |
| `dotnet test tests/Together.Api.Tests/Together.Api.Tests.csproj --configuration Release --no-build --no-restore` | 24 пройдено, 0 пропущено |
| `dotnet package list --project Together.slnx --no-restore --include-transitive --vulnerable --format json` | Exit 0, уязвимые пакеты не найдены |
| `dotnet format Together.slnx --verify-no-changes --no-restore` | Exit 1: WHITESPACE в текущих TripEntity/TripContracts и ENDOFLINE/CHARSET в новой миграции; исходные изменения пользователя сохранены |
| `actionlint` 1.7.12 для `.github/workflows/ci.yml` | Успешно |
| `git diff --check -- .github/workflows/ci.yml README.md` | Успешно |
| SARIF gate: чистый отчёт; warning; принятое/непринятое suppression; failed invocation; нет runs; битый JSON; пустой каталог | Все 8 сценариев дали ожидаемое разрешение или блокировку; временные fixtures вне репозитория |
| `dotnet run --project tests/Together.BrowserTests --configuration Release --no-build -- --backend-smoke` с `APP_URL=http://localhost:8080`, `BROWSER_EVIDENCE_DIR` в temp | Exit 1, `ERR_CONNECTION_REFUSED`: локальный backend не запущен |

Docker отсутствует в PATH этой среды. Compose, сборка Docker-образов,
multi-platform publish и smoke опубликованного digest локально не выполнены.
Полный CodeQL анализ на GitHub не запускался; доступность Code Scanning и права
репозитория не проверены. Все эти этапы остаются обязательными в workflow.

Для запрета merge дополнительно нужны required status checks `CodeQL` и
`Format, audit, Release, xUnit and backend smoke` в GitHub ruleset/branch protection.
Настройки защиты ветки этой правкой файлов не меняются.
