# Применение промпт-шаблонов из ДЗ 2

> **Исторический документ.** Разделы 1–5 описывают frontend-этап ДЗ № 4, а
> разделы 6–8 — промпты, использованные при подготовке ДЗ № 6. Некоторые пункты
> фиксируют первоначальный план автоматического deploy. Текущее состояние —
> GitHub Actions и автоматическое развёртывание через Komodo/Renovate — описано в
> [корневом README](../../README.md); рабочие правила приведены в
> [AGENTS.md](../../AGENTS.md).

Источник: соседний учебный репозиторий HT2, файл Отчет/prompt_templates.md.
Сохранена структура RTCF: Role, Task, Context, Format. Серверный контекст
Home Services Dashboard заменён на standalone Blazor WebAssembly и IndexedDB.

Это рабочие формулировки задач для текущей реализации. Они описывают применённую
декомпозицию работы агента; не являются вымышленной перепиской с пользователем.

## 1. Структура

- **Role:** архитектор C# / Blazor WebAssembly.
- **Task:** разделить модель, UI и хранение по требованиям US-01–US-10.
- **Context:** docs/technical_specification.md, выбранная концепция 3; на этапе ДЗ № 4 серверного API ещё нет.
- **Format:** готовые проекты Core и Client, проверяемая сборка, явные зависимости.

Результат: Together.Core с моделью и валидацией; Together.Client с компонентами и JS-адаптером IndexedDB.

## 2. Компоненты

- **Role:** разработчик доступных Blazor-интерфейсов.
- **Task:** реализовать формы поездки и варианта, бюджет и сравнение.
- **Context:** концепция «План поездки», русские подписи, неизвестные значения, сохранение и отмена.
- **Format:** небольшие компоненты, сообщения полей, состояния загрузки/ошибки/успеха.

Результат: TripEditor, VariantEditor, Field, AmenityField, BudgetView, Comparison, VariantCard, EditorDialog.

## 3. Проверки

- **Role:** инженер автоматизации тестирования.
- **Task:** проверить положительные, ошибочные и граничные сценарии бюджета, форм и хранилища.
- **Context:** критерии AC-01–AC-10; xUnit, bUnit, Playwright .NET; только вымышленные данные.
- **Format:** воспроизводимые тесты, фактический вывод и скриншоты.

Результат: тесты в tests/Together.Tests и tests/Together.BrowserTests.

## 4. Диагностика

- **Role:** инженер по диагностике UI.
- **Task:** воспроизвести обнаруженный дефект, найти причину, исправить и повторить проверку.
- **Context:** консоль браузера, скриншоты 360/768/1440, масштаб 200%, ошибочные транзакции.
- **Format:** доказательства в docs/evidence и описание решения в development_report.md.

## 5. Рефакторинг

- **Role:** C# code reviewer.
- **Task:** убрать подтверждённое дублирование и дорогие повторные вычисления без изменения поведения.
- **Context:** результаты тестов и контрольный набор 10 поездок по 50 вариантов.
- **Format:** локальные изменения, повтор затронутых тестов, измерения и ограничения.

## 6. GitHub Actions для ДЗ № 6

- **Role:** инженер CI/CD и supply-chain security.
- **Task:** связать format/audit, Release build, xUnit, Compose/Chromium smoke,
  multi-arch publish по digest и production deploy.
- **Context:** `main`, `ghcr.io/tipz/together`, GitHub Actions и Komodo/Renovate;
  pull request выполняет проверки без публикации и production deployment.
- **Format:** один dependency graph, минимальные permissions, actions по полным
  commit SHA и явное разделение автоматических, локальных и инфраструктурных
  проверок.

Результат: CI проверяет и публикует `ghcr.io/tipz/together@sha256:...`;
автоматическое обновление образа и deployment обеспечивают Komodo/Renovate.

## 7. OWASP review

- **Role:** reviewer ASP.NET Core/Blazor по OWASP Top 10.
- **Task:** искать эксплуатируемые проблемы в cookie-auth API, OAuth, конфигурации,
  логах, Docker и Actions; не считать finding подтверждённым без участка кода и
  проверки.
- **Context:** данные поездок приватны, OAuth только Яндекс ID, секреты server-side.
- **Format:** идентификатор, severity, evidence, исправление, тест, остаточный риск.

Приняты и проверены: antiforgery, отсутствие production fallback строки БД,
безопасные env defaults, account-linking не по email, local return URL, rate limit
по IP и Identity lockout. Реестр находится в `security_audit.md`.

## 8. Анализ логов

Фактический prompt, синтетический вход, AI-вывод и ручная проверка сохранены в
[log_analysis.md](../log_analysis.md). Production-логи AI не передавались.
