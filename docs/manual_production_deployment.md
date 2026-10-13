# Ручное production-развёртывание и публикация через Pangolin

Дата проверки: 13.10.2026. Исходники приложения: commit `ab24ec4`.

## Фактическая схема

- Публичный адрес: https://together.2-26-49-210.sslip.io.
- Pangolin 1.24.0 на VPS `play2g` (`2.26.49.210`), организация `casita`.
- Публичный HTTP-ресурс `Вместе в путь`: resourceId=14, targetId=14,
  домен `domain1`, subdomain=`together`, SSL включён.
- Target: Site `homelab` (siteId=4), `http://192.168.1.26:8080`.
- Site подключается через существующий Pangolin CLI/WSS-туннель на
  `homelab` (`192.168.1.25`). Новые входящие порты на домашнем роутере не открывались.
- Backend, опубликованный Blazor Client и PostgreSQL работают на
  `build-vm-sui` (`192.168.1.26`), Compose-проект `deploy-ht6`.
- Внутренний адрес https://together.home.lan сохраняет маршрут через Traefik
  и Compose-проект `together-edge` на `homelab`.

Pangolin SSO для нового ресурса отключён по назначению публичного приложения:
регистрация и аутентификация выполняются самим Together. Запрос к `/api/trips`
без входа возвращает 401. Существующие ресурсы Pangolin не изменялись.

## Образ и настройки

По явному указанию пользователя использован локальный образ без GHCR и
GitHub Actions. Это согласованное исключение из обычного production-процесса,
который требует multi-platform образ из GHCR по registry digest.

Приложение запущено по неизменяемому локальному Image ID:

```text
sha256:90ae159446ddab5a29344231f9db12186a4908dde2ead3f1459ff031ddf49ee8
```

Локальный тег: `together:manual-ab24ec4-amd64`. Это Image ID, а не registry digest.
Обе архитектуры собраны дополнительно в OCI-архив на ВМ; arm64 проверена
запуском `dotnet --info` под QEMU, полная браузерная проверка выполнена на amd64.
Публикация GHCR была отклонена из-за отсутствия `write:packages` у доступного токена.
Временная авторизация Docker в GHCR удалена после работы.

Production Compose и закрытый `stack.env` находятся только на сервере:

```text
/home/sui/deploy-ht6/releases/20261013-manual-ab24ec4-f92c/deployment/
```

Для локального образа `pull_policy=never`. Secure cookies включены.
Backend доверяет forwarded headers только от `192.168.1.25`, сохраняет
пользовательские PostgreSQL и Data Protection volumes.

Перед запуском приложения выполнен отдельный контейнер `migrate` с `--migrate`;
подтверждён ExitCode=0. Контейнер PostgreSQL при обновлении не пересоздавался.

## Резервная копия и откат

Резервные копии базы, ключей и предыдущих deployment-файлов сохранены на ВМ:

```text
/home/sui/deploy-ht6/releases/20261013-manual-ab24ec4-f92c/
```

`before-upgrade.dump` проверен через `pg_restore --list` и фактически
восстановлен в отдельную PostgreSQL с отключённой сетью. Тестовый контейнер удалён.
Для отката приложения нужно выбрать совместимый предыдущий образ; схема БД
автоматически назад не откатывается. Production volumes не удалять.

Запись созданного ресурса Pangolin сохранена на VPS:

```text
/home/tipz/pangolin-backups/together-created-20261013-f92c.json
```

Удаление именно нового ресурса выполняется через Integration API
`DELETE /v1/public-resource/14`; другие ресурсы и Site остаются нетронутыми.
API-ключ использован из существующего закрытого файла на VPS, в Git и логи не включён.

## Выполненные проверки

- Release-сборка: 0 ошибок и 0 предупреждений; xUnit: 58/58 и 24/24.
- Миграции: успешный отдельный запуск; app/database healthy.
- HTTPS и цепочка сертификата публичного адреса: успешная проверка curl
  с машины разработчика и с внешнего VPS.
- `/health/ready` публичного адреса: HTTP 200, `Healthy`.
- `/api/trips` без аутентификации: HTTP 401.
- Актуальный `--backend-smoke` на публичном адресе: PASS. Проверены регистрация,
  сохранение бюджета/резерва, уточнения, неуспешная запись, настоящий конфликт,
  мобильный UI/клавиатура и выход. Использованы вымышленные данные.
- Временный тестовый Compose-проект и его volumes удалены по точному project name.

Команда финального браузерного теста:

```powershell
$env:APP_URL = 'https://together.2-26-49-210.sslip.io'
$env:BROWSER_EVIDENCE_DIR = 'artifacts/manual-build-ab24ec4/pangolin-evidence'
dotnet run --project tests/Together.BrowserTests -c Release --no-build -- --backend-smoke
```

Evidence и лог сохранены локально в `artifacts/manual-build-ab24ec4/`.
API создания ресурса и target соответствует Swagger работающего Pangolin 1.24.0.
Попытка задать дополнительные health-check поля target была отклонена API (400);
target создан стандартным запросом без этих полей, доступность проверена отдельно.

Полный внешний OAuth-вход через Яндекс не проверялся. Для публичного адреса
нужно проверить регистрацию callback
`https://together.2-26-49-210.sslip.io/signin-yandex` в настройках Яндекс ID.
Браузерный smoke проверяет собственную регистрацию/аутентификацию приложения.
