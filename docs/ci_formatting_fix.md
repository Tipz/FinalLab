# Исправление WHITESPACE в CI — 13 октября 2026

В `TripEntity.cs`, `VariantEntity.cs` и `TripContracts.cs` блоки аксессоров
свойств с инициализаторами развёрнуты на несколько строк в соответствии
с `csharp_preserve_single_line_blocks = false` из `.editorconfig` и сообщением
formatter на GitHub runner. Изменения касаются только пробелов и переносов строк.
Workflow и правила форматирования сохранены.

Проверки выполнены локально на Windows с SDK `10.0.303`, с уже существующими
изменениями пользователя в рабочем дереве:

| Команда | Фактический результат |
| --- | --- |
| `dotnet format Together.slnx --verify-no-changes --no-restore` | Exit 0 после исправления; до исправления локально тоже возвращала 0, ошибка Linux runner локально не воспроизвелась |
| `dotnet build Together.slnx --configuration Release --no-restore` | 0 ошибок, 0 предупреждений |
| `dotnet test tests/Together.Tests/Together.Tests.csproj --configuration Release --no-build --no-restore` | 58/58 |
| `dotnet test tests/Together.Api.Tests/Together.Api.Tests.csproj --configuration Release --no-build --no-restore` | 24/24 |
| `dotnet run --project tests/Together.BrowserTests --configuration Release --no-build -- --backend-smoke` | Exit 1: `ERR_CONNECTION_REFUSED` при `APP_URL=http://localhost:8080`; локальный backend не запущен, evidence направлен во временный каталог |
| `git diff --check` | Exit 0 |

Повторный запуск GitHub Actions не выполнялся. Успешный локальный formatter
не подтверждает результат на Linux runner; его нужно получить в CI.
