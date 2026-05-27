# Major (WarehouseAutomatisaion)

Desktop .NET 8 / WPF приложение для замены 1С УНФ. Исполняемый файл — `Major.exe`.

## Архитектура: слои

```
WarehouseAutomatisaion.Domain          # POCO + бизнес-инварианты
WarehouseAutomatisaion.Application     # use-cases, контракты, сервисы (БЕЗ SQL)
WarehouseAutomatisaion.Infrastructure  # impls: MySQL, 1С CSV, file IO
WarehouseAutomatisaion.Desktop         # bootstrap, DI, конфигурация
WarehouseAutomatisaion.Desktop.Wpf     # XAML views, ViewModels, OutputType=WinExe -> Major.exe
```

**Жёсткое правило**: прямой SQL живёт ТОЛЬКО в `WarehouseAutomatisaion.Infrastructure/Persistence/`. В Application / Desktop / Desktop.Wpf — запрещён.

## Database schema (MySQL прод)

Сервер `147.45.108.97:3306`, БД `warehouse_automation`, юзер `majorwarehause_app`.

**UI читает ТОЛЬКО `app_*` таблицы**:

| Таблица | Назначение |
|---|---|
| `app_sales_documents` | **single table** + фильтр `document_kind` ∈ {SalesOrder, SalesInvoice, SalesShipment} |
| `app_sales_document_lines` | ТЧ документов (⚠ см. Sales lines gap ниже) |
| `app_customers`, `app_products`, `app_warehouses`, … | Справочники |

**ЗАПРЕЩЕНО в production коде (UI / Application)**:

| Таблица | Почему нельзя |
|---|---|
| `sales_orders` / `sales_invoices` / `sales_shipments` | Legacy отдельные таблицы, в проде не наполняются |
| `onec_*` | Raw staging для импорта из 1С CSV, читать только через `OneCExchangeService` в Infrastructure |

Subagent `sql-schema-reviewer` проверяет это автоматически при правке `Persistence/Sql/*.sql` или DAO в Infrastructure.

## Data flow

```
1С CSV  →  onec_* (raw staging)  →  app_* (operational)  →  Desktop через BackplaneService
```

`BackplaneService` — единая точка чтения для UI слоя. Кастомные SQL вне него = code smell.

## Sales lines gap (известная проблема, blocked на 1С)

В UI 96.7% заказов (`order`) и 99.7% счетов (`invoice`) **без строк** —
сумма документа = 0₽. Снимок 2026-05-27:

| document_kind | total | with_lines | gap |
|---|---|---|---|
| order | 21295 | 712 | **96.7%** |
| invoice | 711 | 2 | **99.7%** |

**Источника для backfill нет**:
- `onec_sales_document_lines` как таблица **не существует**. `onec_*` —
  generic snapshot store (`onec_object_snapshots`, `onec_tabular_section_rows`)
  и они пусты (0 строк).
- Legacy `sales_order_lines` / `sales_invoice_lines` / `sales_shipment_lines`
  тоже пустые (0 строк во всех).

Старый skill `/sales-lines-gap-diag` и упоминания «92k строк» из MEMORY.md
устарели. Подробности — в `tmp/sales_gap_diag_2026-05-27.md`.

**Пути закрытия gap'а** (все требуют решения вне Major):
1. Программист 1С опубликует OData / REST / View с тч → код для тяги.
2. Импорт ТЧ из Excel-выгрузок (если бухгалтерия их даёт).
3. AI распознавание расходных накладных (как для приходных в Sprint 5/8).

При предложении читать `app_sales_document_lines` — всегда упоминай что
таблица сейчас неполная, источник заблокирован публикацией 1С.

## Release process

Версия в `WarehouseAutomatisaion.Desktop.Wpf.csproj` (`<Version>` / `<AssemblyVersion>` / `<FileVersion>` / `<InformationalVersion>`) бампается ТОЛЬКО через:

```
/release-major X.Y.Z
```

Skill автоматически: bump csproj → git commit → annotated tag `vX.Y.Z` → готовит push. GitHub Actions workflow `release-major.yml` срабатывает на push тега `v*` и собирает `major-win-x64.zip`.

⚠ Прямые правки `<Version>` тегов в Desktop.Wpf.csproj **блокируются** хуком `.claude/hooks/block-version-edit.ps1` (PreToolUse Edit/Write).

Перед push'ем тега — `/wpf-publish` для локальной проверки, или запусти agent `release-checklist-reviewer`.

## Локальный dev environment

- .NET SDK 9 — `~/.dotnet/`
- .NET Runtime 8.0.27 — `C:\Program Files\dotnet\`
- `mysql.exe` — Laragon (`C:\laragon\bin\mysql\...\bin\mysql.exe`)
- Prod connection — `WarehouseAutomatisaion.Desktop.Wpf/appsettings.local.json` (**gitignored**, содержит пароль)
- Запуск: `StartDesktop.cmd` или `dotnet run --project WarehouseAutomatisaion.Desktop.Wpf`
- Publish: `pwsh WarehouseAutomatisaion.Desktop.Wpf/publish-win-x64.ps1` (или `/wpf-publish`)

## Зависимости (NuGet)

- **MySqlConnector** 2.4.0 — прямая работа с MySQL (без EF Core)
- **QRCoder** 1.8.0 + **ZXing.Net** 0.16.11 — штрихкоды/QR (генерация и сканирование)
- **System.Windows.Forms** (UseWindowsForms=true) — только ради `NotifyIcon` (минимизация в трей). Global usings `System.Drawing` / `System.Windows.Forms` отключены — конфликтуют с WPF.

## Установленные Claude Code автоматизации

| Тип | Имя | Назначение |
|---|---|---|
| Hook | `block-version-edit` | Блокирует прямые правки `<Version>` в Desktop.Wpf.csproj |
| Skill | `release-major` | Bump версии + git commit + tag |
| Skill | `wpf-publish` | Локальная publish-сборка перед push'ем тега |
| Skill | `sales-lines-gap-diag` | Диагностика gap onec/app |
| Skill | `project-conventions` | Background knowledge для Claude (этот файл в кратком виде) |
| Skill | `mysql-prod-explorer` | Wrapper над mysql.exe с bookmark-запросами |
| Skill | `ui-ux-pro-max` | UI/UX design intelligence |
| Agent | `wpf-binding-reviewer` | Проверка XAML binding paths против ViewModel |
| Agent | `sql-schema-reviewer` | Защита от legacy/raw таблиц в production |
| Agent | `release-checklist-reviewer` | Pre-release проверка csproj/tag/CI consistency |
| MCP | `mysql-prod` (read-only) | Прямой SQL в прод БД |
| MCP | `github` | Issues, PRs, releases, workflow status |
| MCP | `context7` | Live docs для MySqlConnector / WPF / .NET |
| MCP | `sentry` | Error tracking (когда подключите проект) |
| Plugin | `claude-code-setup` | Анализ codebase, рекомендации автоматизаций |
| Plugin | `commit-commands` | `/commit`, `/push`, `/pr` |
| Plugin | `code-review` | Автоматический PR review |
| Plugin | `security-guidance` | Warnings при правке security-sensitive паттернов |

## Язык

Общение и комментарии — **по-русски**. Доменная терминология — 1С УНФ:
- «контрагент» = customer / counterparty
- «документ продажи» = sales document (заказ / счёт / реализация)
- «ТЧ» = табличная часть документа (lines)
- «номенклатура» = product
- «остатки» = stock
- «склад» = warehouse

## Worktrees

`.claude/worktrees/<name>/` — отдельные git worktree для параллельных задач (создаются harness'ом). Внутри них дубли solution-структуры. При анализе **игнорируй**, кроме случая когда явно работаешь в worktree.

Также игнорируй: `_archive/`, `_temp_build/`, `_tmp_build/`, `app_data/`, `exports_*/`, `tools/`, `.tools/`, `tmp/`, `artifacts/`, `perf/`.
