# Публичный сайт и автоматическое развёртывание

Актуализация: 14.10.2026.

Приложение доступно по адресу
**[https://together.2-26-49-210.sslip.io/](https://together.2-26-49-210.sslip.io/)**.

## Процесс выпуска

Автоматическое развёртывание выполняется связкой GitHub Actions и Komodo/Renovate.
GitHub Actions выполняет проверки, собирает multi-platform образ и публикует
его в `ghcr.io/tipz/together`. Komodo/Renovate обеспечивают обновление образа
и развёртывание приложения. Их конфигурация управляется вне этого репозитория.

Workflow [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) содержит
CodeQL, format/NuGet audit, Release build, оба xUnit-проекта, Compose/Chromium
backend smoke, публикацию `linux/amd64` и `linux/arm64` и smoke опубликованного digest.
Pull request выполняет проверки без публикации образа.

Deployment использует `docker-compose.deploy.yml` и неизменяемую ссылку
на образ `ghcr.io/tipz/together@sha256:...`. До запуска приложения миграции
выполняются отдельным одноразовым заданием. PostgreSQL и ключи Data Protection
сохраняются в постоянных volumes. Секреты поступают только из переменных
окружения или secret store и не включаются в образ или Blazor bundle.

Команды самостоятельного запуска, резервного копирования и восстановления
приведены в [backend-документации](../backend_documentation.md).

## Проверки публичного сайта

14.10.2026 подтверждены:

- главная страница — HTTP 200;
- `/health/ready` — `Healthy`;
- `/api/trips` без входа — HTTP 401;
- актуальный Chromium `--backend-smoke` — PASS: регистрация, сохранение
  бюджета/резерва, уточнения, отказ записи, конфликт, мобильный UI и выход.

Команды и результаты: [журнал проверок](history/README.md#актуализация-публичного-стенда-14102026).
Для тестов использованы вымышленные данные.

Callback Яндекс ID:
`https://together.2-26-49-210.sslip.io/signin-yandex`.
Исправления OAuth и ограничения его проверки описаны в
[отчёте OAuth](yandex_oauth_configuration_fix.md).
