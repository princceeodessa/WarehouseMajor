# Major (WarehouseAutomatisaion)

Десктоп .NET 8 / WPF для замены 1С УНФ на одном предприятии. Общий MySQL на VPS + клиенты с авто-обновлением через GitHub Releases. Имя exe — `Major.exe`. Пользователь и UI — на русском, терминология 1С УНФ (Контрагенты, Заказы покупателей, Расходные накладные, Заказы поставщикам, Приходные накладные, Перемещения, Инвентаризации, Установка цен).

## Архитектура (5 проектов)

| Проект | Что в нём |
|---|---|
| `WarehouseAutomatisaion.Domain` | `record`-сущности 1С УНФ: `Organization`, `BusinessPartner`, `NomenclatureItem`, `WarehouseNode`, `StorageBin`, `SalesOrder/Invoice/Shipment`, `PurchaseOrder/Receipt`, `TransferOrder`, `InventoryCount`, `PriceRegistrationDocument` и др. |
| `WarehouseAutomatisaion.Application` | Legacy WMS-overview (Product/StorageCell/WarehouseTask) + `InMemoryRepositories`. Десктопом **не используется**, кандидат на снос. Тянет `Microsoft.AspNetCore.App`. |
| `WarehouseAutomatisaion.Infrastructure` | Импорт CSV 1С (`OneCImportService`), live-export через `cscript+VBS` (`OneCLiveRuntimeExportService`), raw-snapshot в MySQL (`OneCRawSnapshotMySqlSyncService`), проекция в operational (`OneCOperationalProjectionService`), SQL-схема [mysql-operational-schema.sql](WarehouseAutomatisaion.Infrastructure/Persistence/Sql/mysql-operational-schema.sql). |
| `WarehouseAutomatisaion.Desktop` | Сервисы для WPF: `DesktopMySqlBackplaneService` (auth, sales, catalog, purchasing, warehouse — 4 `partial`), `OperationalMySqlDesktopService`, `SalesWorkspace/Store/ImportMerger`, `CatalogWorkspace`, `OperationalPurchasingWorkspace`, `OperationalWarehouseWorkspace`, печать (`SalesDocumentPrintComposer`, `RussianPaymentQrBuilder`), [FunctionalCoverageCatalog](WarehouseAutomatisaion.Desktop/Data/FunctionalCoverageCatalog.cs) (roadmap в коде). |
| `WarehouseAutomatisaion.Desktop.Wpf` | UI: `MainWindow`, `LoginWindow`, `StartupLoadingWindow`, 39 XAML-окон. Сайдбар + табы, 3 раздела (Sales/Purchasing/Warehouse), `ApplicationUpdateService` (GitHub Releases), системный трей. |

Граф зависимостей: `Domain ← Application ← Infrastructure`, `Domain + Infrastructure ← Desktop ← Desktop.Wpf`.

## Поток данных

```
1С (файловая база C:\blagodar)
  → cscript + scripts/export-1c-csv.vbs
  → exports_*/ или app_data/one-c-live/  (CSV + manifest.csv)
  → OneCImportService                     (CSV → OneCImportSnapshot)
  → OneCRawSnapshotMySqlSyncService       (mysql.exe → onec_* raw таблицы)
  → OneCOperationalProjectionService      (raw → operational normalized)
  → MySQL warehouse_automation
  → DesktopMySqlBackplaneService / OperationalMySqlDesktopService
  → WPF Major.exe
```

## Локальная сборка

`dotnet 9.0.314` установлен в `~/.dotnet`, в PATH его нет — перед сборкой добавь:

```powershell
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
dotnet build WarehouseAutomatisaion.sln
```

Запуск десктопа:
```powershell
.\StartDesktop.cmd
# или
.\WarehouseAutomatisaion.Desktop.Wpf\bin\Debug\net8.0-windows\Major.exe
```

## Релиз

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-major-setup.ps1 -Version 1.0.134
```

Артефакты: `artifacts/installers/MajorSetup.exe` (первая установка) и `artifacts/publish/major-win-x64.zip` (auto-update). Подпись Authenticode опциональна (`-CodeSigningCertificateThumbprint` или PFX). Полный workflow в [docs/shared-client-deployment.md](docs/shared-client-deployment.md).

## Конфигурация

`appsettings.local.json` рядом с `Major.exe`:

```json
{
  "RemoteDatabase": {
    "Enabled": true,
    "Host": "...",
    "Port": 3306,
    "Database": "warehouse_automation",
    "User": "...",
    "Password": "..."
  },
  "ApplicationUpdate": {
    "Enabled": true,
    "GitHubOwner": "...",
    "GitHubRepository": "...",
    "AssetName": "major-win-x64.zip"
  }
}
```

С 1.0.106 серверная БД — единственный рабочий режим. Если remote недоступен, клиент **не стартует**. Локальный JSON-fallback убран намеренно — не возвращай его.

## Что важно знать при правках

- **Версионирование.** Сейчас релизы выпускаются как теги `v1.0.X`; версию в [WarehouseAutomatisaion.Desktop.Wpf.csproj](WarehouseAutomatisaion.Desktop.Wpf/WarehouseAutomatisaion.Desktop.Wpf.csproj) обновляй вместе с релизом (`Version`, `AssemblyVersion`, `FileVersion`, `InformationalVersion`).
- **UI язык 1С УНФ.** Не "Suppliers", а "Поставщики"; не "Sales Order", а "Заказ покупателя". Иконки — Segoe Fluent Icons (см. [NavigationCommandCatalog.cs](WarehouseAutomatisaion.Desktop.Wpf/NavigationCommandCatalog.cs)).
- **SQL.** Сейчас весь SQL генерируется `StringBuilder`-ом и исполняется через `mysql.exe` (см. `DesktopMySqlBackplaneService`, `OneCOperationalProjectionService`). Параметризации нет, экранирование самописное (`SqlUtf8TextExpression`). Безопасно только для значений из доверенных источников — не пускай туда сырой пользовательский ввод без валидации.
- **WinForms подключён только ради `NotifyIcon` (трей, 1.0.128).** В csproj явно удалены `System.Drawing` и `System.Windows.Forms` из ImplicitUsings, чтобы не ломать WPF-views. При работе с треем используй полные пути `System.Windows.Forms.NotifyIcon` / `System.Drawing.Icon`.
- **Производительность.** В Backplane стоит `LIMIT 2000`, в UI — cap 100 строк (1.0.132/133), refresh-timer 60 секунд (1.0.134). Если будешь править — учитывай, что фактическая база — 20k+ заказов и 90k+ строк.
- **Тестов нет.** Перед релизом — ручной smoke ([scripts/run-wpf-smoke.ps1](scripts/run-wpf-smoke.ps1)) и проверка ключевых вкладок: Контрагенты, Заказы покупателей, Расходные накладные, Заказы поставщикам, Приходные накладные, Перемещения, Товары.
- **CSV-импорт устойчив к мусору 1С.** [OneCTextNormalizer](WarehouseAutomatisaion.Infrastructure/Importing/OneCTextNormalizer.cs) и [TextMojibakeFixer](WarehouseAutomatisaion.Desktop/Text/TextMojibakeFixer.cs) исправляют кодировки; есть schema-probe fallback для документов без CSV.

## Документация

- [docs/1c-main-functional-contour.md](docs/1c-main-functional-contour.md) — обязательный функциональный контур замены 1С
- [docs/gap-analysis-1c-vs-majorwarehause-2026-05-03.md](docs/gap-analysis-1c-vs-majorwarehause-2026-05-03.md) — gap-анализ с реальными объёмами 1С и P0/P1/P2 списком
- [docs/one-c-domain-model.md](docs/one-c-domain-model.md) — модель данных по реальным объектам 1С
- [docs/one-c-import-runtime.md](docs/one-c-import-runtime.md) — как работает импорт
- [docs/mysql-operational-schema.md](docs/mysql-operational-schema.md) — описание SQL-схемы
- [docs/shared-client-deployment.md](docs/shared-client-deployment.md) — развёртывание VPS + клиента
- [README_WORKSPACE.md](README_WORKSPACE.md) — короткий обзор папок workspace
