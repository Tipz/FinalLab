# Материалы домашнего задания № 6

Проект «Вместе в путь» — standalone Blazor WebAssembly, ASP.NET Core Minimal API,
Identity, EF Core/Npgsql и PostgreSQL. В ДЗ № 6 добавлены CI/CD, security
hardening, OAuth 2.0 через Яндекс ID, Яндекс Метрика с согласием, health checks и
JSON-логирование.

Результаты сдачи ДЗ № 6 ниже зафиксированы 23 сентября 2026 года.
Актуализация от 14.10.2026: [публичный сайт](https://together.2-26-49-210.sslip.io/)
доступен по HTTPS; бюджетное дополнение и исправление OAuth развёрнуты
13.10.2026. Октябрьские проверки описаны в [отчёте развёртывания](../manual_production_deployment.md)
и [отчёте OAuth](../yandex_oauth_configuration_fix.md).

## Состав сдачи

| Требование | Реализация |
| --- | --- |
| CI/CD | `.github/workflows/ci.yml`: обязательный CodeQL → format/audit, build/tests и backend smoke → multi-platform publish → smoke опубликованного digest; актуальная ревизия от 12.10.2026 описана в [ci_revision.md](../ci_revision.md) |
| Container | `ghcr.io/tipz/together` для `linux/amd64` и `linux/arm64` |
| Deployment | Автоматическое развёртывание через GitHub Actions и Komodo/Renovate; отдельный `migrate`, persistent PostgreSQL и Data Protection |
| OAuth 2.0 | Backend Authorization Code Flow с PKCE через Яндекс ID; вход по паролю сохранён |
| Аналитика | Opt-in Яндекс Метрика, очищенные SPA paths и две фиксированные цели |
| Безопасность | Antiforgery, lockout/rate limit, CSP/security headers, ownership checks, pinned actions |
| Monitoring | `/health`, `/health/ready` и Docker healthcheck |
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

Общедоступная ссылка: **[https://together.2-26-49-210.sslip.io/](https://together.2-26-49-210.sslip.io/)**.
Автоматическое развёртывание выполняется через GitHub Actions и Komodo/Renovate.
GitHub Actions проверяет и публикует образ; Komodo/Renovate обеспечивают
его обновление и deployment приложения.

Ранее владельцем проверены Яндекс ID и Метрика.
Публичный callback — `https://together.2-26-49-210.sslip.io/signin-yandex`.
Полный вход с реальным аккаунтом после OAuth-исправления требует проверки
пользователем; backend smoke проверяет регистрацию и вход по паролю.

Миграции выполняются до запуска приложения отдельным одноразовым заданием;
PostgreSQL и ключи Data Protection сохраняются между обновлениями.

## Перенос в другое окружение

Для развёртывания потребуются:

- Docker Engine и Docker Compose;
- PostgreSQL из deployment Compose;
- HTTPS reverse proxy и сертификат;
- собственные `YANDEX_CLIENT_ID`, `YANDEX_CLIENT_SECRET` и зарегистрированный
  Redirect URI `<APP_URL>/signin-yandex`;
- собственный `YANDEX_METRIKA_COUNTER_ID`;
- секреты в исключённом из Git `.env.deploy` или secret store.

Инструкции находятся в [README](../../README.md),
[backend_documentation.md](../../backend_documentation.md) и
[docs/integration_documentation.md](../integration_documentation.md).

## Документы

- [Отчёт преподавателю](homework_6_report.md)
- [Документация интеграций](../integration_documentation.md)
- [Аудит безопасности](../security_audit.md)
- [AI-анализ синтетического лога](../log_analysis.md)
- [Шаблоны AI-промптов](prompt_templates.md)
- [Evidence браузерных проверок](../evidence)
