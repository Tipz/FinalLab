# Публичный сайт и автоматическое развёртывание

Актуализация: 15.10.2026.

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

Команды самостоятельного запуска приведены ниже; резервное копирование
и восстановление — в [backend-документации](../backend_documentation.md#миграции-и-резервное-копирование).

## Самостоятельное развёртывание готового образа

Для развёртывания без исходников и локальной сборки используйте
[`docker-compose.deploy.yml`](../docker-compose.deploy.yml):

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
[backend-документации](../backend_documentation.md).

## Проверки публичного сайта

Последняя проверка 14.10.2026: `/health/ready` вернул HTTP 200, но текущий
Chromium `--backend-smoke` завершился ошибкой после входа: вместо «Мои поездки»
показан прежний единый экран «Все варианты. Все расходы.».
CI для текущего SHA прошёл, включая smoke опубликованного digest;
это не подтверждает обновление публичного стенда через Komodo/Renovate.
Необходимо сверить deployment с текущим образом и повторить браузерную проверку.

Команды, SHA и результаты: [итоговый отчёт](../development_report.md).
Для тестов использованы вымышленные данные. Прежний успешный smoke сохранён
как датированная запись в [архиве](history/README.md#актуализация-публичного-стенда-14102026).

Callback Яндекс ID:
`https://together.2-26-49-210.sslip.io/signin-yandex`.
Исправления OAuth и ограничения его проверки описаны в
[отчёте OAuth](yandex_oauth_configuration_fix.md).
