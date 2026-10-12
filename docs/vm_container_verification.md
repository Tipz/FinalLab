# Контейнерная проверка бюджетного планирования

Дата: 12.10.2026. ВМ `192.168.1.26`, SSH-алиас `build-vm-sui`.
Проверялись исходники commit `3e715f4` с исправлением Dockerfile ниже.
Публикация в GHCR и обновление production не выполнялись.

## Обнаруженная проблема и исправление

Первый образ успешно собрался, но одноразовый мигратор завершился с кодом 139.
Лог показал `UnauthorizedAccessException: Access to the path '/app/appsettings.json'
is denied`; OOMKilled=false. Архив и исходники были подготовлены в каталоге с
umask 077, поэтому файл конфигурации имел права 0600. При исходном COPY он
принадлежал root и был недоступен пользователю app.

В Dockerfile копирование опубликованного приложения изменено на:

```dockerfile
COPY --from=build --chown=app:app /out/api .
```

Запуск остаётся непривилегированным. После пересборки app может читать конфигурацию
с сохранёнными ограниченными правами; миграция и запуск проходят. Read-only root
filesystem продолжает задаваться Compose.

## Изолированное окружение

- Docker Engine 28.3.3, Compose v2.39.1, BuildKit v0.23.2; ВМ x86_64.
- Сборка существующего Dockerfile: .NET SDK 10.0.303, ASP.NET runtime 10.0.11,
  платформа `linux/amd64`.
- Compose-проект `together-budget-check-20261012-b07485`; каталог
  `/tmp/together-budget-check-20261012-b07485`.
- Использован `docker-compose.deploy.yml` с тестовым override: pull_policy=never
  для локального образа, traefik.enable=false для всех трёх сервисов,
  restart=no для app/database. Отдельные сеть и два named volume.
- HTTP доступен только на `127.0.0.1:18086` ВМ. Browser smoke обращался через
  SSH-туннель `127.0.0.1:5190` на машине разработчика.
- Случайный пароль PostgreSQL сгенерирован `openssl rand -hex 32` на ВМ,
  `.env.test` имеет ограниченные права; секрет не передавался в build arguments,
  не выводился через `compose config` и не копировался в репозиторий.
- Яндекс ID/Метрика не настроены; использованы вымышленные данные.
- Только для локального HTTP-теста Secure cookies=false. Production-настройки
  и существующие контейнеры не изменялись.

Тестовый override ограничивал CPU каждого сервиса до 1, память app до 768 MiB,
database/migrate до 512 MiB. Снимок после тестов: app 67.56 MiB, PostgreSQL
24.38 MiB. Это единичное наблюдение, а не нагрузочный тест или подбор production-лимитов.

Итоговый локальный Image ID:
`sha256:03b5994398f07900701b98d0c43da15da544c242d1c5e6ab87bd0bb4d261a67c`.
Это ID локально собранного образа, а не опубликованный registry digest.

## Команды и результаты

С локальной машины исходники переданы архивом без Git, bin/obj, secrets.json,
*.user, .env, пользовательских данных и старых evidence:

```powershell
tar.exe -czf artifacts/vm-budget-check/source.tar.gz --exclude=bin --exclude=obj --exclude=secrets.json --exclude='*.user' .dockerignore Dockerfile Directory.Build.props global.json Together.slnx .config src database docker-compose.deploy.yml tests/Together.Api.Tests/MigrationFixtures
scp -q artifacts/vm-budget-check/source.tar.gz build-vm-sui:/tmp/together-budget-check-20261012-b07485/source.tar.gz
```

На ВМ временные скрипты prepare.sh/start.sh/resume.sh использовали следующие
команды из указанного каталога. Первый запуск остановился на ошибке прав;
после передачи исправленного Dockerfile сборка повторена в build-fixed.log,
а resume.sh продолжил проверку на той же тестовой БД старой схемы:

```sh
docker compose --project-name together-budget-check-20261012-b07485 --env-file .env.test -f docker-compose.deploy.yml -f compose.test.yml config --quiet
docker build --progress=plain --platform linux/amd64 --tag together-budget-check-20261012-b07485:amd64 .
docker compose --project-name together-budget-check-20261012-b07485 --env-file .env.test -f docker-compose.deploy.yml -f compose.test.yml up --detach --wait --wait-timeout 90 database
```

В отдельную PostgreSQL сначала применена начальная часть database/migrations.sql
до `TripBudgetSettings`, затем `MigrationFixtures/budget-before.sql`. После
запуска migrate/app применён `budget-after.sql`. psql выполнялся через
`docker compose exec -T database psql -U budget_test -d budget_test` с SQL на stdin.

```sh
docker compose --project-name together-budget-check-20261012-b07485 --env-file .env.test -f docker-compose.deploy.yml -f compose.test.yml up --detach --force-recreate --wait --wait-timeout 120 migrate app
```

| Проверка | Фактический результат |
| --- | --- |
| Compose config --quiet | Успех |
| Native Docker build после исправления | Успех, linux/amd64 |
| Одноразовый migrate | Exited (0), app запускается после его завершения |
| Обновление старой схемы и CHECK-ограничения | PASS migration: defaults, trip/related data and constraints |
| Пользователь процесса | uid=1654(app), gid=1654(app) |
| Read-only root filesystem | ReadOnly=true |
| Data Protection volume | Доступен app для записи |
| /health и /health/ready | Контейнер healthy, readiness возвращает Healthy |
| Опубликованный Blazor bootstrap | HTTP-запрос к fingerprinted asset из index.html успешен |
| Browser --backend-smoke | PASS, Chromium 151.0.7922.34 |
| Остановка приложения SIGTERM | ExitCode=0 |
| Пересоздание app и PostgreSQL | Данные, настройки и revision сохранены |
| Прежняя authentication cookie после пересоздания | /api/auth/me и чтение поездки успешны без повторного входа |

Проверка bootstrap использовала реальную ссылку
`_framework/blazor.webassembly.w3qd1tpl0e.js` из опубликованного index.html.
Проверка `blazor.webassembly.js` без суффикса в первоначальном вспомогательном
скрипте была заменена чтением фактической ссылки: .NET 10 публикует fingerprinted assets.

Browser smoke выполнен с локальной машины:

```powershell
ssh -N -o BatchMode=yes -o ExitOnForwardFailure=yes -o ServerAliveInterval=20 -L 127.0.0.1:5190:127.0.0.1:18086 build-vm-sui
$env:APP_URL = 'http://127.0.0.1:5190'
$env:BROWSER_EVIDENCE_DIR = 'docs/evidence/vm-budget-check'
dotnet run --project tests/Together.BrowserTests -c Release --no-build -- --backend-smoke
```

Проверены регистрация, сохранение и восстановление лимита/резерва, уточнения,
сохранение черновика при моделируемом 503, настоящий конфликт revision через второй
API-запрос, подтверждение отбрасывания правок, клавиатура, ширина 360 px,
форма при CSS zoom 200% и выход. Это не проверка нативного browser zoom.

Отдельный временный скрипт
`artifacts/vm-budget-check/verify-persistence.ps1 -RunName together-budget-check-20261012-b07485`
создал вымышленную поездку через API, затем recreate.sh выполнил:

```sh
docker compose --project-name together-budget-check-20261012-b07485 --env-file .env.test -f docker-compose.deploy.yml -f compose.test.yml stop app
docker compose --project-name together-budget-check-20261012-b07485 --env-file .env.test -f docker-compose.deploy.yml -f compose.test.yml up --detach --force-recreate --wait --wait-timeout 90 database
docker compose --project-name together-budget-check-20261012-b07485 --env-file .env.test -f docker-compose.deploy.yml -f compose.test.yml up --detach --no-deps --force-recreate --wait --wait-timeout 90 app
```

Тот же HTTP cookie-сеанс успешно прочитал владельца и поездку: лимит 13 500 000
копеек, резерв 15%, название, id и revision совпали. Миграционный fixture также
повторно прошёл после пересоздания.

Локально повторены Release-сборка (0 предупреждений/ошибок),
`dotnet test tests/Together.Tests -c Release --no-build --no-restore` — 58/58 и
`dotnet test tests/Together.Api.Tests -c Release --no-build --no-restore` — 24/24.

Evidence: [browser JSON](evidence/vm-budget-check/backend-browser-results.json),
[persistence JSON](evidence/vm-budget-check/persistence-results.json),
[desktop](evidence/vm-budget-check/budget-desktop.png),
[mobile](evidence/vm-budget-check/budget-mobile.png),
[форма 200%](evidence/vm-budget-check/budget-form-200.png).
Логи обеих сборок сохранены локально в artifacts/vm-budget-check.

## Очистка и ограничения

Временный Compose-проект удалён командой `down --volumes --remove-orphans` с
его точным project name. Удалены только его два локальных образа, временный
каталог с исходниками/секретом и SSH-туннель. Общий image/build cache не очищался.
Сравнение с начальным снимком подтвердило: все десять ранее работающих контейнеров
сохранили исходные IDs и продолжают работать; production app/database остаются healthy.

`linux/arm64` не проверялась: builder ВМ объявляет только x86-платформы,
ARM/QEMU-регистрация отсутствует. Конфигурация host-wide emulation не менялась.
HTTPS/Traefik, внешний OAuth и публикация GHCR также не проверялись.
Перед production-выпуском остаётся обычный CI/publish и отдельное migration job
по опубликованному immutable digest.
