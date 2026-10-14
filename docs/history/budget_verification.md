# Проверка бюджетного дополнения — 12.10.2026

Историческая запись проверки US-14/US-15 до актуализации документации.
Текущие правила: [бюджетное планирование](../budget_planning.md).
Повторная проверка при актуализации документации: [журнал](README.md).

## Выполненные проверки

Среда: Windows, .NET SDK 10.0.303. Docker отсутствует. Для проверки использован
отдельный PostgreSQL 17.11 из [официального архива EDB](https://www.enterprisedb.com/download-postgresql-binaries),
каталог `%TEMP%/together-budget-verification`, адрес `127.0.0.1:55436`, БД
`together_budget`. Пароль случайный, передавался через переменные окружения и
временный файл вне репозитория. Учебный production-стенд не использовался.

| Команда | Результат |
| --- | --- |
| `dotnet build Together.slnx -c Release --no-restore` | Успех, 0 предупреждений, 0 ошибок |
| `dotnet test tests/Together.Tests -c Release --no-build --no-restore` | 58/58, включая расчёты, валидацию, старый Workspace и компоненты |
| `dotnet test tests/Together.Api.Tests -c Release --no-build --no-restore` | 24/24, включая настройки, повторный вход, валидацию, владельца и конфликт |
| `dotnet tool restore` | Успех |
| `dotnet tool run dotnet-ef migrations add TripBudgetSettings --project src/Together.Api --no-build --configuration Release` | Создана миграция |
| `dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Together.Api --configuration Release --no-build` | Неучтённых изменений модели нет |
| `dotnet tool run dotnet-ef migrations script --idempotent --project src/Together.Api --configuration Release --no-build --output database/migrations.sql` | Обновлён SQL-скрипт |

Обновление существующей БД проверено следующей последовательностью. `psql` был
запущен из временного каталога PostgreSQL с `-h 127.0.0.1 -p 55436 -U budget_smoke
-d together_budget`; `PGPASSWORD` и `TOGETHER_DB_CONNECTION` были заданы в окружении:

```powershell
dotnet tool run dotnet-ef database update InitialPostgreSql --project src/Together.Api --configuration Release --no-build
psql -h 127.0.0.1 -p 55436 -U budget_smoke -d together_budget -f tests/Together.Api.Tests/MigrationFixtures/budget-before.sql
dotnet tool run dotnet-ef database update --project src/Together.Api --configuration Release --no-build
psql -h 127.0.0.1 -p 55436 -U budget_smoke -d together_budget -f tests/Together.Api.Tests/MigrationFixtures/budget-after.sql
```

Результат: `PASS migration: defaults, trip/related data and constraints`.
Проверены сохранение поездки и revision, ребёнка, варианта, известных/null/нулевых
расходов, выбора варианта и критерия; значения по умолчанию и отклонение
отрицательного/слишком большого лимита и процента.

Опубликованный клиент и API подготовлены командами:

```powershell
dotnet publish src/Together.Client -c Release --no-restore -o artifacts/budget-smoke/client
dotnet publish src/Together.Api -c Release --no-restore -o artifacts/budget-smoke/api
New-Item -ItemType Directory -Path artifacts/budget-smoke/api/wwwroot -Force
Copy-Item -Path artifacts/budget-smoke/client/wwwroot/* -Destination artifacts/budget-smoke/api/wwwroot -Recurse -Force
```

API запущен управляемой командной сессией `dotnet Together.Api.dll` из
`artifacts/budget-smoke/api` с `ASPNETCORE_ENVIRONMENT=Production`,
`ASPNETCORE_URLS=http://127.0.0.1:5188`, тестовой строкой подключения,
`Authentication__SecureCookies=false` и временным каталогом Data Protection.
Отключение Secure cookie применялось только для изолированного локального HTTP-теста.

```powershell
$env:APP_URL = 'http://127.0.0.1:5188'
$env:BROWSER_EVIDENCE_DIR = 'docs/evidence/budget-planning'
dotnet run --project tests/Together.BrowserTests -c Release --no-build -- --backend-smoke
```

Smoke охватывает регистрацию, сохранение/перезагрузку настроек и расходов,
переход к полю уточнения, исчезновение заполненного пункта, моделируемый ответ 503
с сохранением черновика и повторной записью, настоящий 409 после второго запроса
к PostgreSQL, подтверждение отбрасывания правок, ширину 360 px, клавиатуру и выход.
Дополнительно проверяется форма при CSS zoom 200%; это не проверка нативного
масштабирования интерфейса браузера.

Результат: PASS backend-smoke, Chromium 151.0.7922.34. Изображения desktop/mobile
просмотрены; форма 200% также сохранена в evidence.
После проверки локальные API и PostgreSQL остановлены, временный файл пароля удалён.

Результаты и изображения: [JSON](../evidence/budget-planning/backend-browser-results.json),
[desktop](../evidence/budget-planning/budget-desktop.png),
[mobile](../evidence/budget-planning/budget-mobile.png),
[форма 200%](../evidence/budget-planning/budget-form-200.png).
Старые evidence не заменяются: каталог выбирается через `BROWSER_EVIDENCE_DIR`.

Ограничения: Docker/multi-platform image в этой среде не собирались; production HTTPS
и настоящий production-deployment не проверялись. Publish сообщил об отсутствии
опционального workload wasm-tools; выполненный smoke использует опубликованный
клиент без дополнительных оптимизаций WebAssembly. После новых изменений
проверки нужно повторить.
