> **Архив завершённого этапа.** Формулировки сохранены на дату этапа; текущие требования и найденные позднее расхождения — в [индексе](../README.md) и [журнале](README.md).

# Реализация домашнего задания № 6

Документ фиксирует принятые решения и итоговую последовательность реализации.
Актуализация: 23 сентября 2026 года.

## 1. Исходная архитектура

До ДЗ № 6 проект уже включал:

- `Together.Core` с моделью, вычислениями и валидацией;
- `Together.Contracts` с DTO и маршрутами API;
- standalone `Together.Client` на Blazor WebAssembly и MudBlazor;
- `Together.Api` на ASP.NET Core Minimal API, Identity и EF Core/Npgsql;
- PostgreSQL, миграции и серверное владение данными;
- Dockerfile и Compose с отдельным migration job;
- модульные, компонентные, API и браузерные тесты.

В production опубликованный клиент раздаётся API с того же origin. IndexedDB
используется только для добровольного импорта данных старой версии.

## 2. Решения ДЗ № 6

### CI и supply chain

Три прежних workflow объединены в один dependency graph:

```text
format/NuGet audit
  → Release build и xUnit
  → CodeQL
  → Docker Compose/PostgreSQL/Chromium smoke
  → AMD64/ARM64 publish
  → smoke опубликованного digest
```

Actions закреплены полными commit SHA. Publish job получает только
`contents: read` и `packages: write`. Образ публикуется в
`ghcr.io/tipz/ht6` с OCI metadata, provenance и SBOM.

### Deployment

Учебный production-стенд находится на `192.168.1.26` в приватной сети.
Существующие Traefik и локальный центр сертификации обеспечивают HTTPS.

Автоматический deploy через защищённый self-hosted runner был подготовлен, но
отключён в универсальном workflow, поскольку зависел от локального пути, runner
labels, приватной сети, Traefik и PKI. Текущий deployment выполняется оператором
через `docker-compose.deploy.yml` по immutable image digest.

Миграции выполняются отдельным одноразовым контейнером. PostgreSQL и Data
Protection keys сохраняются в volumes. Приложение работает от пользователя
`app` с read-only root filesystem.

### OAuth 2.0

Выбран единственный разрешённый provider — Яндекс ID. Password login сохранён.
Используется backend Authorization Code Flow с PKCE и стандартными
state/correlation cookies.

Запрашивается только `login:email`. Provider token не передаётся в Blazor и не
сохраняется. Совпадение email не связывает аккаунты автоматически. Redirect URI:
`<APP_URL>/signin-yandex`.

### Аналитика

Выбрана Яндекс Метрика. Тег загружается только после согласия пользователя.
Разрешены очищенные SPA paths и две цели:

- `login_succeeded` с `method=password|yandex`;
- `trip_created` без пользовательских параметров.

Поездки, бюджеты, возраст детей, заметки, email, user id, query string и fragment
в аналитику не передаются.

### Безопасность

Добавлены:

- synchronizer antiforgery token для изменяющих API-запросов;
- CSP, HSTS, Referrer-Policy, Permissions-Policy и `nosniff`;
- auth rate limit по client IP и Identity lockout;
- безопасный local-only `returnUrl`;
- production fail-fast при отсутствии строки подключения;
- закрепление GitHub Actions по SHA;
- NuGet vulnerability gate и CodeQL;
- non-root container и read-only root filesystem.

Полный реестр находится в [security_audit.md](../security_audit.md).

### Monitoring и logging

`/health` проверяет процесс, `/health/ready` — процесс и PostgreSQL. Dockerfile и
Compose используют container healthcheck. Локальный стенд дополнительно
контролируется существующей инфраструктурой Docker/Traefik.

Логи выводятся однострочным JSON в stdout и содержат UTC timestamp, level,
category, Event ID и TraceId/SpanId. Docker ограничивает размер файлов.
Production logs и пользовательские данные не передаются AI.

## 3. Выполненные этапы

1. Сопоставлены требования ДЗ № 6 и существующая архитектура.
2. Проведён AI-assisted OWASP review и составлен реестр findings.
3. Объединён и усилен CI/publish pipeline.
4. Реализованы antiforgery и дополнительные security controls.
5. Реализован OAuth 2.0 через Яндекс ID.
6. Реализована opt-in Яндекс Метрика.
7. Добавлены JSON logging, health checks и Docker log rotation.
8. Расширены unit/component/API/browser tests.
9. Проверен опубликованный multi-platform image.
10. Учебный стенд развёрнут в приватной сети с существующими Traefik/PKI.
11. Документация синхронизирована с текущим кодом и эксплуатационной моделью.

## 4. Проверка результата

Локально 20 сентября 2026 года подтверждены:

- format и locked restore;
- Release build без предупреждений;
- 36 Core/UI-тестов;
- 15 API-тестов;
- NuGet audit без найденных vulnerable packages;
- antiforgery, OAuth с fake scheme и ограничения аналитики;
- health endpoint и формат JSON-логов.

[GitHub Actions run от 23 сентября 2026 года](https://github.com/Tipz/HT6/actions/runs/35828731866)
успешно выполнил build/tests, CodeQL, Compose/Chromium backend smoke,
multi-platform publish и smoke опубликованного digest.

Владелец проекта подтвердил на локальном HTTPS-стенде:

- ручной deployment `docker-compose.deploy.yml`;
- вход через Яндекс ID;
- работу настроенного счётчика Яндекс Метрики.

## 5. Переносимость и границы

Публичный IP и внешний хостинг отсутствуют. Репозиторий не содержит
конфигурацию существующих Traefik и локальной PKI.

Для другого окружения требуются:

- Docker Engine и Docker Compose;
- HTTPS reverse proxy и сертификат;
- собственные Яндекс ID Client ID/Secret и Redirect URI;
- собственный номер счётчика Метрики;
- секреты в исключённом из Git env-файле или secret store;
- правила firewall, соответствующие выбранному `APP_BIND_ADDRESS`.

DAST, penetration testing и централизованное хранилище логов не входят в
репозиторий. Отдельный scheduled GitHub uptime workflow отсутствует, поскольку
GitHub-hosted runner не видит приватный стенд.

## 6. Связанные документы

- [Основной README](../../README.md)
- [Материалы сдачи](../../SUBMISSION.md)
- [Отчёт преподавателю](../homework_6_report.md)
- [Документация интеграций](../integration_documentation.md)
- [Аудит безопасности](../security_audit.md)
- [AI-анализ логов](../log_analysis.md)
