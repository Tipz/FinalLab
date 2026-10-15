# Исправление некликабельного меню — 15.10.2026

Проблема воспроизведена в видимом Chromium на текущем локальном приложении.
У вымышленного нового пользователя серверный список пуст, а в IndexedDB есть
старый Workspace. Пример в этом случае не создаётся, поскольку доступен
добровольный импорт. Компонент выводил пункты «2. Варианты поездки» и
«3. Сравнение» как `span aria-disabled="true"`, поэтому нажатие не меняло URL.
В браузере до исправления зафиксированы одна ссылка и два отключённых пункта.

Теперь все три пункта всегда являются ссылками. Без выбранной поездки маршруты
`/variants` и `/comparison` открывают соответствующий раздел с предложением
создать поездку; найденные локальные данные можно импортировать существующим
действием с подтверждением. Сохранённые поездки открываются по прежним маршрутам
с GUID. Недоступный GUID не подменяется другой поездкой. Переходы не создают
поездки и не отправляют IndexedDB автоматически.

Изменены TripWorkspace.razor, TripWorkspace.razor.cs, TripVariants.razor и
TripComparison.razor. Добавлены четыре bUnit-проверки разделов без GUID с пустым
и заполненным списком. BackendSmoke дополнен кликами по меню при пустом серверном
списке и наличии старых локальных данных, проверкой неизменности IndexedDB и
отсутствия автоматической записи в PostgreSQL, созданием поездки из сравнения.

Выполнено на локальном стенде с опубликованным frontend, настоящими API и
PostgreSQL; тестовые данные вымышленные. После публикации frontend локальный
сервер перезапущен. Команды и результаты:

```powershell
dotnet build Together.slnx -c Release --no-restore
dotnet publish src/Together.Client -c Release --no-restore -o artifacts/ui-audit/client
dotnet test tests/Together.Tests/Together.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/Together.Api.Tests/Together.Api.Tests.csproj -c Release --no-build --no-restore
dotnet format Together.slnx --verify-no-changes --no-restore
$env:APP_URL='http://localhost:5198'
$env:BROWSER_EVIDENCE_DIR="$PWD/artifacts/menu-fix/backend-smoke"
dotnet run --project tests/Together.BrowserTests -c Release --no-build --no-restore -- --backend-smoke --headed
git diff --check
```

Release — 0 ошибок и предупреждений; Together.Tests — **70/70**;
Together.Api.Tests — **25/25**; format и backend smoke — успешно.
Обычные клики по обоим пунктам проверены с поездками и без поездок при
1440, 768 и 360 px. Полный smoke сохраняет проверки истории, прямых ссылок,
черновиков, 503/409 и клавиатуры. Локальное приложение доступно по
`http://localhost:5198`. Дополнительный видимый браузер оставлен открытым
на пустом разделе сравнения с вымышленным аккаунтом для проверки меню.

Логи и локальный браузерный инструмент находятся в `artifacts/menu-fix`.
Скриншоты: [до](evidence/menu-fix-20261015/before.png),
[после, десктоп](evidence/menu-fix-20261015/after-desktop.png),
[после, телефон](evidence/menu-fix-20261015/after-mobile.png).
[Итог backend smoke](evidence/menu-fix-20261015/backend-browser-results.json).

Ограничения: Docker в окружении отсутствует, контейнеры и публичное развёртывание
не проверялись. На публичном сайте изменений не выполнялось.
