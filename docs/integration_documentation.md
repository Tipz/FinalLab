# Интеграции и CI/CD

Статус документа: актуальное состояние проекта.
Последнее обновление: 14 октября 2026 года. Результаты текущего CI — в [итоговом отчёте](../development_report.md).

Принятые решения и последовательность работ зафиксированы в
[историческом журнале](history/README.md); детали интеграций приведены
ниже.

## 1. Состав приложения

Приложение состоит из standalone Blazor WebAssembly, ASP.NET Core Minimal API и
PostgreSQL. Клиент публикуется в `wwwroot` API, а аутентификация выполняется через
ASP.NET Core Identity cookie.

В репозитории реализованы OAuth2 через Яндекс ID, opt-in Яндекс Метрика,
antiforgery, JSON-логи и единый CI/publish pipeline. Публичный production URL:
[https://together.2-26-49-210.sslip.io/](https://together.2-26-49-210.sslip.io/).
Автоматическое развёртывание обеспечивают GitHub Actions и Komodo/Renovate.

## 2. Принятые ограничения

- Поездки, направления, бюджеты, возраст детей, заметки и URL предложений не
  передаются сторонним сервисам.
- Рабочие секреты не помещаются в Git, Docker image, логи или Blazor bundle.
- PostgreSQL остаётся основным хранилищем.
- Миграции выполняются отдельным одноразовым контейнером до запуска приложения.
- Data Protection keys сохраняются вне writable layer контейнера.
- Password login сохраняется для обратной совместимости.
- Платежная интеграция в текущий объём не входит.

## 3. CI/CD

### Реализованное состояние

| Workflow | Фактическая функция | Ограничение |
| --- | --- | --- |
| `.github/workflows/ci.yml` | Обязательный CodeQL → format/NuGet audit, build/xUnit и Compose/Chromium → publish digest → smoke опубликованного образа | [Ревизия 12.10.2026](ci_revision.md); [результаты текущего CI](../development_report.md#текущий-ci) |

### Последовательность обязательных этапов

```text
CodeQL → quality (format/audit, build/tests, backend smoke) → publish → published-image smoke
```

CodeQL запускается первым без `continue-on-error`; `quality` зависит от CodeQL,
publish — от `quality`. Ошибки анализа и неподавленные SARIF findings блокируют
последующие jobs. Отдельный Dependency Review удалён; NuGet audit обязателен.
Pull request выполняет только проверки. Ручной запуск публикует только из `main`.
Текущая конфигурация запускает автоматическую публикацию по тегу вида
`v1.2.3`, чей коммит входит в историю `main`. Обычный push в `main` workflow
не запускает. Эта конфигурация ещё не проверена опубликованным run;
границы подтверждения указаны в [итоговом отчёте](../development_report.md#текущий-ci).
Обновление образа и автоматическое развёртывание
приложения обеспечивают Komodo/Renovate.

### Production secrets и variables

Секреты и production-параметры поступают из переменных окружения или secret store.
Значения секретов в репозиторий не включены. Для самостоятельного запуска
используется закрытый `.env.deploy`.

| Имя | Тип | Назначение |
| --- | --- | --- |
| `APP_URL` | Переменная shell оператора | Production URL для smoke после выбора схемы и порта |
| `YANDEX_CLIENT_ID` | `.env.deploy` | Client ID production-приложения Яндекс OAuth |
| `YANDEX_CLIENT_SECRET` | `.env.deploy` | Client Secret production-приложения Яндекс OAuth |
| `YANDEX_METRIKA_COUNTER_ID` | `.env.deploy` | Публичный номер счётчика |

Compose отображает `YANDEX_CLIENT_ID` и `YANDEX_CLIENT_SECRET` в server-side
параметры `Authentication__Yandex__ClientId` и
`Authentication__Yandex__ClientSecret`. Пароль PostgreSQL и OAuth client secret не
передаются в Blazor configuration и не включаются в Docker image.

### Deployment

Приложение доступно по публичному HTTPS-адресу; регистрация и вход выполняются
самим Together. GitHub Actions проверяет и публикует образ в GHCR,
Komodo/Renovate обеспечивают обновление образа и автоматическое развёртывание.
Настройки Komodo/Renovate управляются вне этого репозитория.

Deployment использует опубликованный `ghcr.io/tipz/together@<digest>`, выполняет
отдельный `migrate`, запускает `app` и проверяет `/health/ready`.
Порядок выпуска и проверки описаны в [документации развёртывания](manual_production_deployment.md),
исправление OAuth — в [отчёте OAuth](yandex_oauth_configuration_fix.md).

Compose default публикует порт на `127.0.0.1`, а текущий `deploy.env.example`
переопределяет bind на `0.0.0.0` для доступа из LAN. При таком режиме порт должен
быть ограничен firewall; для reverse proxy на том же хосте следует использовать
`127.0.0.1`.

При переносе в другое окружение необходимо определить:

- собственный production URL целевого сервера;
- HTTPS reverse proxy и доверенный сертификат;
- каталог Compose и доступ оператора к нему;
- доступ Docker host к `ghcr.io/tipz/together`, если package является приватным;
- backup и rollback procedure.

### Логи и rollback на Docker host

Production Compose использует Docker `json-file` с rotation: приложение и БД —
до пяти файлов по 10 MiB, migration job — до двух. Просмотр без раскрытия env:

```bash
cd "$DEPLOY_PATH"
docker compose --env-file .env.deploy --file docker-compose.deploy.yml logs --since 1h --tail 200 app
```

Ручной rollback не удаляет volumes. Возьмите предыдущий успешный digest из
GitHub run/package history, задайте его только для команды и обновите приложение:

```bash
export TOGETHER_IMAGE='ghcr.io/tipz/together@sha256:<previous-digest>'
docker compose --env-file .env.deploy --file docker-compose.deploy.yml pull app
docker compose --env-file .env.deploy --file docker-compose.deploy.yml up -d --no-deps app
curl --fail "$APP_URL/health/ready"
```

Схема БД автоматически назад не откатывается. Rollback приложения допустим только
при совместимости предыдущего образа с уже применённой схемой; PostgreSQL volume
и Data Protection keys не удаляются.

## 4. OAuth2

### Статус

Реализован стандартным ASP.NET Core OAuth handler. При отсутствии Client ID или
Client Secret приложение запускается с password login, а кнопка Яндекс ID скрыта.
Владелец проекта подтвердил реальный вход через Яндекс ID на локальном
HTTPS-стенде. Для другого окружения требуется собственное OAuth-приложение и
Redirect URI `<APP_URL>/signin-yandex`.

Для публичного сайта callback:
`https://together.2-26-49-210.sslip.io/signin-yandex`.
Пользователь сообщил о добавлении этого URI в консоль Яндекса. Исправление
свойств external authentication развёрнуто 13.10.2026; реальный вход после
обновления требует проверки пользователем. Автоматизированные проверки и
ограничения приведены в [отчёте OAuth](yandex_oauth_configuration_fix.md).

### Реализованный провайдер

Яндекс ID OAuth 2.0 через стандартный ASP.NET Core OAuth handler. Backend
выполняет authorization code flow, обменивает code на token и отдельно получает
профиль из API Яндекс ID.

Официальная документация:

- [регистрация приложения для авторизации](https://yandex.com/dev/id/doc/en/register-auth);
- [получение OAuth token](https://yandex.com/dev/id/doc/en/access);
- [authorization code flow](https://yandex.com/dev/id/doc/en/codes/code-url);
- [получение профиля пользователя](https://yandex.com/dev/id/doc/en/user-information);
- [рекомендации безопасности](https://yandex.com/dev/id/doc/en/tips).

Для production и тестирования создаются отдельные приложения Яндекс OAuth.
Production-приложение запрашивает только `login:email`: устойчивый `id` входит в
стандартный ответ, а email нужен для создания локальной Identity-записи.

### Конфигурация backend

```text
Authentication__Yandex__ClientId
Authentication__Yandex__ClientSecret
```

Client Secret является серверным секретом. Client ID допустим в конфигурации, но
для единообразия также поступает в backend через environment configuration.

### Реализованный flow

1. Клиент выполняет полную навигацию на backend challenge endpoint.
2. Backend создаёт authentication properties и correlation/state cookie и
   перенаправляет пользователя на Yandex OAuth authorization endpoint.
3. Яндекс возвращает браузер с authorization code на зарегистрированный Redirect
   URI. Его scheme, host, port и path совпадают с настройками production-app.
4. Backend проверяет state и обменивает code на token серверным запросом.
5. Backend получает минимальный профиль через API Яндекс ID. Token не передаётся
   в Blazor и не сохраняется после завершения входа.
6. Существующий Identity user находится по устойчивому provider key Яндекс ID
   либо безопасно создаётся. Совпадение email само по себе не связывает аккаунты.
7. Backend выдаёт существующую application cookie и возвращает браузер на
   локальный клиентский маршрут.
8. Клиент получает пользователя через `/api/auth/me`.

Password login, регистрация и logout остаются доступными.

### Обработка ошибок

- отмена входа пользователем;
- provider error;
- отсутствующий или неподтверждённый email;
- конфликт email с существующим password account;
- неверный/внешний return URL;
- несоответствие correlation/state;
- временная недоступность провайдера.

Токены и полные provider responses не логируются и не передаются в Blazor.

## 5. Аналитика

### Статус

Реализована и выключена при отсутствии `YandexMetrika__CounterId`. Тег загружается
динамически только после `granted`; хранится только строка consent в localStorage.

### Сервис

Яндекс Метрика для технических SPA page view и ограниченного набора целей.
Публичный номер счётчика передаётся в клиентскую конфигурацию, например через
`YandexMetrika__CounterId`; при его отсутствии аналитика выключена.

### Конфиденциальность

Тег Яндекс Метрики загружается только после явного согласия. До consent
приложение не должно загружать `tag.js` или отправлять запросы к
`mc.yandex.ru`. Вебвизор, карты, e-commerce, user parameters и session parameters
не включаются.

Разрешённые параметры должны быть закрытым списком технических констант. Нельзя
отправлять:

- идентификатор, email или имя пользователя;
- название, даты и направление поездки;
- состав семьи и возраст детей;
- суммы расходов;
- заметки и URL предложений;
- OAuth query string или exception text.

### Просмотры и цели

| Просмотр/цель | Допустимые параметры |
| --- | --- |
| `login_succeeded` | `method=password|yandex` |
| `trip_created` | нет |

SPA-навигация отправляется методом `hit` только с очищенным путём без query string
и fragment. Цели отправляются методом `reachGoal`. Unit-тесты подтверждают
allowlist и очистку URL. Владелец проекта подтвердил работу настроенного счётчика
на локальном стенде; в другом окружении требуется собственный номер счётчика.

## 6. Health checks и мониторинг

### Текущее состояние

- `GET /health` возвращает доступность процесса.
- `GET /health/ready` использует EF Core health check для PostgreSQL.
- Publish workflow уже ожидает `/health/ready` при smoke опубликованного image.

### Мониторинг

Отдельный scheduled GitHub uptime workflow не используется. Контейнер
контролируется Docker healthcheck, а CI проверяет `/health/ready`
на опубликованном образе. Публичный
`https://together.2-26-49-210.sslip.io/health/ready` доступен для внешней проверки;
отдельный внешний uptime monitor не настроен в репозитории. Недоступность
аналитики не влияет на readiness приложения.

## 7. Логирование

### Текущее состояние

Используется ASP.NET Core JSON console logging с уровнями `Information`,
`Warning` и `Error`. Централизованное хранилище не входит в репозиторий и
выбирается владельцем целевой инфраструктуры.

### Реализованное состояние

- однострочный JSON в stdout;
- timestamp, level, category, event id и trace id;
- структурированные auth и request outcomes;
- отсутствие request body, cookie, password, OAuth token, connection string,
  email и содержимого поездок;
- retention и просмотр через средства выбранного хостинга либо Docker host.

Промпты для AI-анализа работают только с обезличенными фрагментами логов.
Фактический синтетический пример и ручная проверка: [log_analysis.md](log_analysis.md).

## 8. Локальная конфигурация

OAuth и аналитика необязательны для локального запуска: при отсутствии их
конфигурации приложение сохраняет password login, а аналитика выключена.

Реальные Client Secret, password и SSH key нельзя добавлять в `.env.example`,
`deploy.env.example`, appsettings или документацию.

## 9. Проверка интеграций

Фактические команды и результаты текущей версии приведены в
[итоговом отчёте](../development_report.md). В CI проверяются CodeQL, NuGet,
Release-сборка, xUnit, Compose/PostgreSQL и Chromium backend smoke,
публикация AMD64/ARM64 и запуск опубликованного digest.

OAuth-тесты используют тестовую authentication scheme. Тесты аналитики
проверяют consent, очистку путей и отзыв согласия без отправки реальных событий.
Полный вход через Яндекс ID и получение событий Метрикой на текущем стенде
требуют отдельной проверки. Публичный стенд показал прежний единый экран,
поэтому его соответствие текущему коду не подтверждено.

Датированные результаты интеграций и материалы применения AI сохранены
в [архивном отчёте](history/integration_documentation.md).
