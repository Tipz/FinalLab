# Материалы домашнего задания № 6

Проект «Вместе в путь» — standalone Blazor WebAssembly, ASP.NET Core Minimal API,
Identity, EF Core/Npgsql и PostgreSQL. В ДЗ № 6 добавлены CI/CD, security
hardening, OAuth 2.0 через Яндекс ID, Яндекс Метрика с согласием, health checks и
JSON-логирование.

Состояние сдачи ДЗ № 6 зафиксировано 23 сентября 2026 года. Более позднее бюджетное дополнение от 12.10.2026 описано в [budget_planning.md](docs/budget_planning.md); результаты сдачи ниже не подтверждают его production-выпуск.

## Состав сдачи

| Требование | Реализация |
| --- | --- |
| CI/CD | `.github/workflows/ci.yml`: обязательный CodeQL → format/audit, build/tests и backend smoke → multi-platform publish → smoke опубликованного digest; актуальная ревизия от 12.10.2026 описана в [ci_revision.md](docs/ci_revision.md) |
| Container | `ghcr.io/tipz/together` для `linux/amd64` и `linux/arm64` |
| Deployment | Ручной запуск `docker-compose.deploy.yml` по immutable digest; отдельный `migrate`, persistent PostgreSQL и Data Protection |
| OAuth 2.0 | Backend Authorization Code Flow с PKCE через Яндекс ID; вход по паролю сохранён |
| Аналитика | Opt-in Яндекс Метрика, очищенные SPA paths и две фиксированные цели |
| Безопасность | Antiforgery, lockout/rate limit, CSP/security headers, ownership checks, pinned actions |
| Monitoring | `/health`, `/health/ready`, Docker healthcheck и контроль локального стенда средствами Docker/Traefik |
| Логи | Однострочный JSON stdout с timestamp, level, category, Event ID и Trace ID |
| AI | Генерация/проверка CI, OWASP review и анализ синтетического JSON-лога |

Платёжная интеграция не реализована: шаг является опциональным и не требуется
предметной области проекта.

## Подтверждённые проверки

Локальная проверка 20 сентября 2026 года:

- `dotnet format Together.slnx --verify-no-changes --no-restore` — успешно;
- Release build — успешно без предупреждений;
- `Together.Tests` — 36/36;
- `Together.Api.Tests` — 15/15;
- NuGet direct/transitive audit — уязвимые пакеты не найдены;
- antiforgery, OAuth с тестовой scheme и privacy-ограничения аналитики покрыты
  автоматизированными тестами;
- `/health` возвращает HTTP 200, JSON stdout содержит требуемые структурные
  поля.

Публичный [GitHub Actions run от 23 сентября 2026 года](https://github.com/Tipz/HT6/actions/runs/35828731866)
успешно завершил:

- format и NuGet audit;
- Release build и оба xUnit-проекта;
- CodeQL;
- Docker Compose/PostgreSQL/Chromium backend smoke;
- публикацию AMD64/ARM64 image;
- запуск и smoke-проверку опубликованного digest.

Dependency Review запускается только для pull request и поэтому ожидаемо был
пропущен в указанном push run.

## Учебный production-стенд

Стенд развёрнут в приватной локальной сети на машине `192.168.1.26`.
Существующие Traefik и локальный центр сертификации предоставляют HTTPS.
На этом стенде владельцем проекта проверены вход через Яндекс ID и работа
настроенного счётчика Яндекс Метрики.

Публичного IP и внешнего хостинга нет, поэтому общедоступная ссылка на приложение
не предоставляется. Это инфраструктурное ограничение учебного стенда, а не
ограничение Docker image.

Production обновляется оператором через `docker-compose.deploy.yml`.
Автоматический deploy job для защищённого self-hosted GitHub runner был
подготовлен, но отключён в текущем универсальном workflow, поскольку зависел от
локального пути, runner labels, приватной сети, Traefik и PKI.

## Перенос в другое окружение

Для развёртывания потребуются:

- Docker Engine и Docker Compose;
- PostgreSQL из deployment Compose;
- HTTPS reverse proxy и сертификат;
- собственные `YANDEX_CLIENT_ID`, `YANDEX_CLIENT_SECRET` и зарегистрированный
  Redirect URI `<APP_URL>/signin-yandex`;
- собственный `YANDEX_METRIKA_COUNTER_ID`;
- секреты в исключённом из Git `.env.deploy` или secret store.

Инструкции находятся в [README](README.md),
[backend_documentation.md](backend_documentation.md) и
[docs/integration_documentation.md](docs/integration_documentation.md).

## Документы

- [Отчёт преподавателю](docs/homework_6_report.md)
- [Документация интеграций](docs/integration_documentation.md)
- [Аудит безопасности](docs/security_audit.md)
- [AI-анализ синтетического лога](docs/log_analysis.md)
- [Шаблоны AI-промптов](docs/prompt_templates.md)
- [Evidence браузерных проверок](docs/evidence)
