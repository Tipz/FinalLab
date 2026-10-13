# Исправление конфигурации Яндекс OAuth

Дата: 13.10.2026.

На публичном стенде `https://together.2-26-49-210.sslip.io` передаваемый
`client_id` отличался от Client ID в OpenBao `homelab/shared/yandex`.
Секрет прочитан через AppRole из `/home/tipz/keys/.api_openbao` на `homelab`.
Значения токенов и OAuth credentials в отчёт и исходники не включены.

Поля OpenBao `ClientID` и `Client secret` перенесены соответственно в
`YANDEX_CLIENT_ID` и `YANDEX_CLIENT_SECRET` закрытого серверного файла:

```text
/home/sui/deploy-ht6/releases/20261013-manual-ab24ec4-f92c/deployment/stack.env
```

Перед изменением создана резервная копия
`stack.env.before-yandex-20261013T194914Z`; оба файла имеют права `0600`.
Из каталога deployment выполнена команда:

```bash
docker compose -p deploy-ht6 --env-file stack.env -f docker-compose.yml up -d --no-deps --force-recreate app
```

Команда завершилась с кодом 0. Проверка окружения пересозданного контейнера
подтвердила совпадение обоих OAuth-параметров с OpenBao без вывода значений.
Используется прежний Image ID
`sha256:90ae159446ddab5a29344231f9db12186a4908dde2ead3f1459ff031ddf49ee8`.
Приложение уже читает credentials из серверной конфигурации в
`src/Together.Api/Authentication/YandexAuthentication.cs`; изменение C# не требовалось.

Проверено после обновления:

- Контейнер `deploy-ht6-app-1`: `healthy`.
- Публичный `/health/ready`: HTTP 200.
- `/api/trips` без входа: HTTP 401.
- `/api/auth/yandex`: HTTP 302 на OAuth Яндекса.
- Первый ответ OAuth: HTTP 302 на `passport.yandex.com`; в этом ответе
  сообщения `Unknown client with such client_id` нет.
- `dotnet build Together.slnx -c Release --nologo`: 0 ошибок, 0 предупреждений.
- `dotnet test tests/Together.Tests/Together.Tests.csproj -c Release --no-build --nologo`: 58/58.
- `dotnet test tests/Together.Api.Tests/Together.Api.Tests.csproj -c Release --no-build --nologo`: 24/24.
- Актуальный `--backend-smoke` на публичном адресе: PASS. Использованы
  вымышленные данные; evidence в `artifacts/yandex-oauth/backend-smoke/`.

Команда smoke-теста:

```powershell
$env:APP_URL = 'https://together.2-26-49-210.sslip.io'
$env:BROWSER_EVIDENCE_DIR = 'artifacts/yandex-oauth/backend-smoke'
dotnet run --project tests/Together.BrowserTests -c Release --no-build -- --backend-smoke
```

В OpenBao поле Redirect URI содержит `https://together.home.lan/signin-yandex`.
Публичный backend формирует
`https://together.2-26-49-210.sslip.io/signin-yandex`.
Наличие публичного URI в консоли Яндекс OAuth не проверено: его необходимо
добавить или подтвердить в настройках приложения Яндекса.
Полный OAuth-вход с аккаунтом пользователя не выполнялся; перенаправление
на страницу входа само по себе не подтверждает успешность всего OAuth flow.

## Анализ ошибки после OAuth callback

После добавления публичного Redirect URI пользователем проанализированы логи
`deploy-ht6-app-1` за последние 30 минут. Для фильтрации использованы
`docker logs --since 30m --tail 350 deploy-ht6-app-1` и затем полный интервал
`docker logs --since 25m deploy-ht6-app-1`; вывод отфильтрован до OAuth-запросов
и предупреждений без параметров query и credentials.

В 19:53:52–19:53:53 UTC (22:53:52–22:53:53 МСК) видна последовательность:

```text
GET /api/auth/yandex          -> 302
GET /signin-yandex            -> 302
GET /api/auth/yandex/callback  -> 302
```

Ошибок обмена токена в проверенном интервале не зарегистрировано. Сами коды
302 не показывают адрес конечного редиректа, поэтому причина установлена
дополнительно по исходникам и воспроизводящему тесту.

`StartYandexLogin` создавал `AuthenticationProperties` только с `RedirectUri`,
без `Items["LoginProvider"]`. ASP.NET Core Identity
`GetExternalLoginInfoAsync` возвращает null, если такого свойства нет:
https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Core/src/SignInManager.cs.
В результате callback перенаправляет на `/login?oauthError=provider`, для
которого UI показывает общее сообщение о неудачном входе.

Тестовый обработчик самостоятельно добавлял `LoginProvider`, скрывая дефект.
Он исправлен: в external cookie сохраняются свойства исходного challenge.
На исходном endpoint тест
`SuccessfulAndRepeatedLogin_UseSameUser` воспроизвёл ошибку: `/api/auth/me`
вернул 401. Команда воспроизведения:

```powershell
dotnet test tests/Together.Api.Tests/Together.Api.Tests.csproj -c Release --nologo --filter FullyQualifiedName~SuccessfulAndRepeatedLogin_UseSameUser
```

Endpoint исправлен через
`signInManager.ConfigureExternalAuthenticationProperties(provider.Scheme, callback)`.
Существующий тест также проверяет успешный OAuth-редирект при первом и
повторном входе. После исправления Release-сборка завершилась без ошибок
и предупреждений; оба xUnit-проекта прошли: 58/58 и 24/24. Команды сборки
и тестов приведены выше.

Повторный `--backend-smoke` на текущем публичном стенде: PASS, evidence в
`artifacts/yandex-oauth/provider-fix/backend-smoke/`. Выполнена приведённая
выше команда с этим значением `BROWSER_EVIDENCE_DIR`. Этот запуск подтверждает
работоспособность текущего стенда, но не проверяет ещё не развёрнутый C# fix.

На этапе анализа исправление C# ещё не было развёрнуто. Следующий раздел
фиксирует последующее обновление по явному запросу пользователя.

## Локальная сборка и развёртывание исправления

По запросу пользователя образ собран локально на `build-vm-sui`, поскольку
на Windows-машине Docker отсутствует. Сохранён существующий ручной процесс
без публикации в GHCR; используется локальный Image ID, не registry digest.
Архив текущих исходников сформирован без `bin`/`obj`, серверных конфигураций
и секретов. SHA256 архива:

```text
0ce0458f77f6b8c321a3c150c700050ce4a7ea8414c8ce7977895b119c13de91
```

Сборка на ВМ завершилась успешно:

```bash
docker buildx build --platform linux/amd64 --load --tag together:yandex-provider-20261013 --metadata-file /tmp/together-yandex-provider-20261013/build-metadata.json /tmp/together-yandex-provider-20261013
```

Фактически развёрнутый Image ID:

```text
sha256:a39f2c7a51493dca907d0462888cef4874f3ae67c61c7e4a84b0013fe1591ac3
```

В закрытом серверном `stack.env` изменён только `TOGETHER_IMAGE`. Предыдущий
образ сохранён. Конфигурация и резервная копия PostgreSQL в custom format
сохранены в `/home/sui/deploy-ht6/releases/20261013-yandex-provider/`;
`before-stack.env` и `before-upgrade.dump` имеют права `0600`.

Из действующего каталога deployment последовательно выполнены:

```bash
docker compose -p deploy-ht6 --env-file stack.env -f docker-compose.yml run --rm --no-deps migrate
docker compose -p deploy-ht6 --env-file stack.env -f docker-compose.yml up -d --no-deps --force-recreate app
```

Обе команды завершились с кодом 0. Проверено совпадение Image ID нового
контейнера с собранным образом, статус `healthy`, сохранение прежнего
контейнера базы, тома Data Protection и обоих OAuth-параметров.
Приложение работает от `uid=1654(app)`, в его логах после обновления
не обнаружены сообщения уровня Error/Critical.

Публичные проверки: `/` и `/health/ready` — HTTP 200; `/api/trips` без входа —
401; `/api/auth/yandex` — 302 с callback публичного адреса.
Актуальный `--backend-smoke` после развёртывания — PASS. Выполнена команда
выше с `BROWSER_EVIDENCE_DIR=artifacts/yandex-oauth/deployment/backend-smoke`.
Локальная Release-сборка и оба xUnit-проекта для этих исходников ранее
прошли: 0 ошибок/предупреждений, 58/58 и 24/24.

Пользователь сообщил о добавлении публичного Redirect URI в консоль Яндекса.
Реальный OAuth-вход после обновления требует проверки пользователем:
авторизованной сессии его аккаунта Яндекса у агента нет.

Дополнительно успешно собран `linux/arm64`:

```bash
docker buildx build --platform linux/arm64 --load --tag together:yandex-provider-20261013-arm64 --metadata-file /tmp/together-yandex-provider-20261013/arm64-metadata.json /tmp/together-yandex-provider-20261013
docker run --rm --platform linux/arm64 --entrypoint dotnet together:yandex-provider-20261013-arm64 --info
```

Image ID arm64:
`sha256:0e7b685bb00dac005279cec95fecd74056aacf18bbe19121330f7e6246cfb059`.
Запуск под QEMU завершился с кодом 0, runtime 10.0.11, architecture arm64.
Полная браузерная проверка выполнена на развёрнутом amd64.
Попытка общего OCI-exporter для двух архитектур завершилась ошибкой:
он не поддерживается текущим `docker` driver. Поэтому обе архитектуры
собраны раздельно и загружены в локальное Docker-хранилище; общий OCI-архив
не создан. Логи сборок находятся в
`/tmp/together-yandex-provider-20261013/`.

Для отката из действующего каталога deployment восстановить `stack.env`
из закрытой копии `before-stack.env` указанного release и выполнить ту же
команду `up -d --no-deps --force-recreate app`. Предыдущий Image ID сохранён:
`sha256:90ae159446ddab5a29344231f9db12186a4908dde2ead3f1459ff031ddf49ee8`.
