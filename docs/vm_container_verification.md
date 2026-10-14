# Контейнерная проверка бюджетного планирования

Дата: 12.10.2026. Изолированное тестовое окружение, платформа `linux/amd64`.
Проверялись исходники commit `3e715f4` с исправлением Dockerfile ниже.
Публикация в GHCR и обновление production в рамках этой проверки не выполнялись.

## Обнаруженная проблема и исправление

Первый образ собрался, но мигратор завершился с кодом 139. Лог показал
`UnauthorizedAccessException: Access to the path '/app/appsettings.json' is denied`;
OOMKilled=false. Файл конфигурации имел права 0600 и после COPY принадлежал root,
поэтому пользователь app не мог его прочитать.

В Dockerfile копирование изменено на:

```dockerfile
COPY --from=build --chown=app:app /out/api .
```

После пересборки миграция и запуск прошли. Приложение продолжает работать
от непривилегированного пользователя с read-only root filesystem.

## Окружение и результаты

Использованы Docker Engine 28.3.3, Compose v2.39.1, BuildKit v0.23.2,
.NET SDK 10.0.303, ASP.NET runtime 10.0.11 и PostgreSQL.
Тестовые данные вымышлены, секреты не включены в репозиторий.

| Проверка | Фактический результат |
| --- | --- |
| Compose config --quiet | Успех |
| Docker build после исправления | Успех, linux/amd64 |
| Одноразовый migrate | Exited (0), app запускается после его завершения |
| Обновление старой схемы и CHECK-ограничения | PASS: defaults, trip/related data and constraints |
| Пользователь процесса | uid=1654(app), gid=1654(app) |
| Read-only root filesystem | ReadOnly=true |
| Data Protection volume | Доступен app для записи |
| /health и /health/ready | Healthy |
| Опубликованный Blazor bootstrap | Fingerprinted asset из index.html доступен |
| Browser --backend-smoke | PASS, Chromium 151.0.7922.34 |
| Остановка приложения SIGTERM | ExitCode=0 |
| Пересоздание app и PostgreSQL | Данные, настройки и revision сохранены |
| Cookie после пересоздания | Чтение владельца и поездки без повторного входа |

Браузерный smoke проверил регистрацию, сохранение лимита/резерва, уточнения,
черновик при 503, конфликт revision, подтверждение отбрасывания правок,
клавиатуру, ширину 360 px, CSS zoom 200% и выход.

Release-сборка завершилась без предупреждений и ошибок;
`dotnet test tests/Together.Tests -c Release --no-build --no-restore` — 58/58,
`dotnet test tests/Together.Api.Tests -c Release --no-build --no-restore` — 24/24.

Evidence: [browser JSON](evidence/vm-budget-check/backend-browser-results.json),
[persistence JSON](evidence/vm-budget-check/persistence-results.json),
[desktop](evidence/vm-budget-check/budget-desktop.png),
[mobile](evidence/vm-budget-check/budget-mobile.png),
[форма 200%](evidence/vm-budget-check/budget-form-200.png).

## Ограничения

Тестовое окружение удалено после проверки. В этом запуске не проверялись
`linux/arm64`, production HTTPS, внешний OAuth и публикация GHCR.
Последующее развёртывание описано в [документации выпуска](manual_production_deployment.md).
