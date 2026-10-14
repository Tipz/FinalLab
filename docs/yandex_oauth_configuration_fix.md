# Исправление конфигурации Яндекс OAuth

Дата исправления: 13.10.2026. Документация актуализирована 14.10.2026.

Публичный сайт:
[https://together.2-26-49-210.sslip.io/](https://together.2-26-49-210.sslip.io/).
Callback: `https://together.2-26-49-210.sslip.io/signin-yandex`.

## Конфигурация

На стенде обнаружено несовпадение Client ID с настройками OAuth-приложения.
Серверные параметры `YANDEX_CLIENT_ID` и `YANDEX_CLIENT_SECRET` обновлены
из secret store без вывода значений в логи или документацию.
Пользователь сообщил о добавлении публичного Redirect URI в консоль Яндекса.

Проверены готовность приложения, HTTP 401 для `/api/trips` без входа и
HTTP 302 для `/api/auth/yandex` с переходом к авторизации Яндекса.
Само перенаправление не подтверждает успешность всего OAuth flow.

## Исправление callback

`StartYandexLogin` создавал `AuthenticationProperties` только с `RedirectUri`,
без `Items["LoginProvider"]`. ASP.NET Core Identity
`GetExternalLoginInfoAsync` возвращал null, и callback перенаправлял
на `/login?oauthError=provider`.

Тестовый обработчик самостоятельно добавлял `LoginProvider`, скрывая дефект.
Он исправлен: в external cookie сохраняются свойства исходного challenge.
Тест `SuccessfulAndRepeatedLogin_UseSameUser` воспроизвёл ошибку на прежней
реализации: `/api/auth/me` возвращал 401.

Endpoint исправлен через
`signInManager.ConfigureExternalAuthenticationProperties(provider.Scheme, callback)`.
Тест проверяет первый и повторный OAuth-вход с тестовой authentication scheme.
Исправление развёрнуто 13.10.2026.

## Проверки

```powershell
dotnet build Together.slnx -c Release --nologo
dotnet test tests/Together.Tests/Together.Tests.csproj -c Release --no-build --nologo
dotnet test tests/Together.Api.Tests/Together.Api.Tests.csproj -c Release --no-build --nologo
$env:APP_URL = 'https://together.2-26-49-210.sslip.io'
$env:BROWSER_EVIDENCE_DIR = 'artifacts/yandex-oauth/deployment/backend-smoke'
dotnet run --project tests/Together.BrowserTests -c Release --no-build -- --backend-smoke
```

После исправления Release-сборка прошла без ошибок и предупреждений;
xUnit — 58/58 и 24/24, backend smoke после развёртывания — PASS.
Миграции выполнены отдельно; сохранение данных и ключей Data Protection подтверждено.

14.10.2026 сборка, оба xUnit-проекта и публичный backend smoke прошли повторно:
[журнал проверок](history/README.md#актуализация-публичного-стенда-14102026).

Backend smoke использует вымышленные данные и проверяет собственную регистрацию
и вход по паролю. Полный вход с реальным аккаунтом Яндекса после исправления
требует проверки пользователем.
