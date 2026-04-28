# Отчет системного UI и логического тестирования MajorWarehause

Дата: 2026-04-28
Среда: Windows, .NET 8, WPF, локальные данные из `app_data`, publish `win-x64`

## 1. Объем проверки

Проверялись не отдельные вкладки, а весь текущий пользовательский контур:

- запуск WPF-приложения `MajorWarehause.exe`;
- все пункты боковой навигации: Главная, Заказы, Клиенты, Счета, Отгрузки, Закупки, Склад, Товары, Отчеты/Аналитика, Модель;
- основные кнопки создания документов/справочников;
- поиск в разделах с таблицами;
- сборка, тесты, publish, zip и setupper;
- логическая целостность локальных JSON-данных;
- остатки старой WinForms-архитектуры;
- доступность UI через UI Automation.

## 2. Проверенные команды

```powershell
dotnet build .\WarehouseAutomatisaion.sln
dotnet test .\WarehouseAutomatisaion.sln --no-build
.\scripts\build-majorwarehause-setup.ps1
```

Результат:

- `dotnet build` успешно, 0 warnings, 0 errors.
- `dotnet test` успешно, но содержательных тестов в solution фактически нет.
- `build-majorwarehause-setup.ps1` успешно создал:
  - `artifacts\publish\majorwarehause-win-x64\MajorWarehause.exe`
  - `artifacts\publish\majorwarehause-win-x64.zip`
  - `artifacts\installers\MajorWarehauseSetup.exe`

## 2.1. Повторная проверка после исправления P0

После исправления `ProductsWorkspaceView.xaml` повторно проверены Debug и publish-сборки.

Результат:

- Debug: `NavCatalogButton` открывает товары на 9 502 позициях, `Responding=True`, память около 432 MB, UIA-дерево доступно.
- Publish: `NavCatalogButton` открывает товары, `Responding=True`, память около 460 MB, UIA-дерево доступно.
- Внутренние вкладки товаров `Виды цен`, `Скидки`, `Установка цен`, `Журнал`, `Товары` переключаются без зависаний.
- Контрольная навигация по всем разделам после исправления прошла успешно.

Исправление:

- убран общий `ScrollViewer` вокруг всего экрана товаров;
- рабочая область товаров получила конечную высоту через `Grid` row `*`;
- `ProductsGrid` получил явные настройки virtualization/recycling;
- карточки таблицы/деталей больше не заставляют таблицу растягиваться до высоты всех строк.

## 2.2. Повторная проверка после исправления P1-миграций данных

После исправлений в `CatalogWorkspaceStore` и `PurchasingOperationalWorkspaceStore` выполнена миграция локальных данных. Перед миграцией созданы обратимые backup-файлы:

- `app_data\catalog-workspace.20260428-104122.bak.json`
- `app_data\purchasing-workspace.20260428-104122.bak.json`

Результат:

- `catalog-workspace.json` переписан с нормализованным русским текстом; известные mojibake-паттерны больше не найдены.
- Заказ поставщику `PO-260424-001` теперь ссылается на существующий `SupplierId`.
- Debug smoke: `Товары` и `Закупки` открываются, `Responding=True`.
- Release publish smoke: `Товары` и `Закупки` открываются, `Responding=True`.
- `build-majorwarehause-setup.ps1 -SkipPublish` завершился с `exit 0`; скрипт исправлен, чтобы успешный IExpress не возвращал старый native exit code.

## 3. Объем данных

Текущие локальные данные:

| Модуль | Количество |
|---|---:|
| Товары каталога | 9 502 |
| Виды цен | 20 |
| Клиенты | 2 077 |
| Заказы | 16 948 |
| Счета | 416 |
| Отгрузки | 0 |
| Поставщики | 1 |
| Заказы поставщику | 1 |
| Складские документы | 0 |

## 4. UI: навигация по разделам

Проверка выполнялась через UI Automation свежим процессом на каждый раздел.

| Раздел | Результат | Память после открытия |
|---|---|---:|
| Главная | OK | ~281 MB |
| Заказы | OK | ~369 MB |
| Клиенты | OK | ~359 MB |
| Счета | OK | ~361 MB |
| Отгрузки | OK | ~358 MB |
| Закупки | OK | ~356 MB |
| Склад | OK | ~372 MB |
| Товары | FAIL: зависание UI | 476 MB через 4 сек, 1.5 GB в publish через 6 сек |
| Отчеты/Аналитика | OK | ~317 MB |
| Модель | OK | ~368 MB |

Свежая publish-сборка, скопированная вне репозитория без `app_data`, открывает каталог нормально: `Responding=True`, ~259 MB. Значит критический дефект связан с рабочим объемом данных, а не с общим запуском приложения.

## 5. UI: основные действия

Основные кнопки создания открывают редакторы и не валят процесс:

| Раздел | Действие | Результат |
|---|---|---|
| Главная | Проверить обновления | Открывается информаальное окно `MajorWarehause` |
| Заказы | Новый заказ | Открывается окно `Новый заказ` |
| Клиенты | Новый клиент | Открывается окно `Новый клиент` |
| Счета | Новый счет | Открывается окно `Новый счет` |
| Отгрузки | Новая отгрузка | Открывается окно `Новая отгрузка` |
| Закупки | Новая закупка | Открывается окно `Новый документ: Заказ поставщику` |
| Склад | Создать | Открывается окно `Перемещение` |

Поиск `NO_MATCH_123456789` в разделах Заказы, Клиенты, Счета, Отгрузки, Закупки, Склад, Отчеты/Аналитика, Модель отрабатывает без зависаний.

## 6. Критические дефекты

### P0. Раздел "Товары" зависает на рабочих данных

Статус: исправлено в `ProductsWorkspaceView.xaml`, повторная проверка прошла.

Первичный симптом:

- при клике на `NavCatalogButton` процесс перестает отвечать;
- UI Automation больше не видит дерево элементов;
- publish-сборка через 6 секунд: `Responding=False`, память ~1.5 GB, CPU ~30 секунд;
- при более долгом ожидании Debug-процесс доходил примерно до 3 GB RAM и оставался `Responding=False`.

Затронутые места:

- `WarehouseAutomatisaion.Desktop.Wpf\MainWindow.xaml.cs:299` - `catalog` открывает `ProductsWorkspaceView`.
- `WarehouseAutomatisaion.Desktop.Wpf\ProductsWorkspaceView.xaml.cs:57` - конструктор `ProductsWorkspaceView`.
- `WarehouseAutomatisaion.Desktop.Wpf\ProductsWorkspaceView.xaml.cs:67` - полная нормализация визуального дерева через `WpfTextNormalizer.NormalizeTree(this)`.
- `WarehouseAutomatisaion.Desktop.Wpf\ProductsWorkspaceView.xaml.cs:159` - `RefreshAll()`.
- `WarehouseAutomatisaion.Desktop.Wpf\ProductsWorkspaceView.xaml.cs:262` - `ApplyFilters()`.
- `WarehouseAutomatisaion.Desktop.Wpf\ProductsWorkspaceView.xaml.cs:281` - полный `Products.Clear()` и добавление всех строк.
- `WarehouseAutomatisaion.Desktop.Wpf\ProductsWorkspaceView.xaml:193` - внешний `ScrollViewer`.
- `WarehouseAutomatisaion.Desktop.Wpf\ProductsWorkspaceView.xaml:454` - `ProductsGrid`.

Вероятная причина:

- экран товаров кладет `DataGrid` внутрь внешнего `ScrollViewer`;
- таблица получает неограниченную высоту и может потерять реальную виртуализацию строк;
- в `ObservableCollection` добавляются все 9 502 товара сразу;
- поверх этого выполняется runtime-нормализация текста и построение детальной карточки.

Первичный итог: до исправления на рабочих данных каталог был непригоден к использованию. После исправления повторная проверка Debug/publish прошла без зависания.

## 7. Логические проблемы данных

### P1. Каталог товаров содержит структурные проблемы

Статус: частично исправлено.

Проверка `app_data\catalog-workspace.json`:

- 9 502 товара;
- 609 дублирующихся кодов товара;
- 2 610 товаров с ценой `0`;
- 9 502 товара без поставщика;
- mojibake в сохраненном каталоге исправлен миграцией, известные bad-паттерны: `0`;
- дубликатов `Id` нет;
- пустых `Code` и `Name` нет.

Примеры дублей кодов после нормализации: `НФ-00006781`, `НФ-00006782`, `НФ-00006784`, `НФ-00006792`, `НФ-00006077`.

### P1. Ссылочная целостность продаж неполная

Проверка `app_data\sales-workspace.json`:

- 13 заказов с `CustomerId = 00000000-0000-0000-0000-000000000000` или без существующего клиента;
- 15 344 заказа без строк и с суммой `0`;
- 416 счетов не ссылаются на существующий заказ;
- у всех 416 счетов пустой `SalesOrderNumber`;
- отрицательных сумм по счетам нет;
- дубликатов `CustomerId`, `OrderId`, `OrderNumber` нет.

Примеры заказов без клиента: `КАНФ-001313`, `КАНФ-007560`, `МИНФ-000064`, `МИНФ-000068`, `МИНФ-000069`.

### P1. Закупка ссылается на несуществующий SupplierId

Статус: исправлено.

В `app_data\purchasing-workspace.json` поставщик:

- `351e2d86-f410-4914-a441-19b0d2c5c2ad`, `Новый поставщик`, `SUP-001`

Заказ поставщику `PO-260424-001` теперь ссылается на:

- `351e2d86-f410-4914-a441-19b0d2c5c2ad`, `Новый поставщик`

Исправление сделано безопасно: если `SupplierId` не найден, но `SupplierName` совпадает с единственным поставщиком, store перепривязывает документ на найденного поставщика.

## 8. UI/accessibility проблемы

### P2. Кнопки боковой навигации имеют пустой UI Automation Name

Статус: исправлено.

На главном экране найдено:

- всего кнопок: 36;
- до исправления кнопок без accessible name: 28;
- после исправления основные nav-кнопки без accessible name: 0;
- проверено через UI Automation: `NavDashboardButton`, `NavSalesButton`, `NavCustomersButton`, `NavInvoicesButton`, `NavShipmentsButton`, `NavPurchasingButton`, `NavWarehouseButton`, `NavCatalogButton`, `NavAuditButton`, `NavModelButton`.

Причина: в `MainWindow.xaml` кнопки содержат `StackPanel`, но нет `AutomationProperties.Name`.

Затронутые места:

- `WarehouseAutomatisaion.Desktop.Wpf\MainWindow.xaml:131`
- `WarehouseAutomatisaion.Desktop.Wpf\MainWindow.xaml:141`
- `WarehouseAutomatisaion.Desktop.Wpf\MainWindow.xaml:151`
- `WarehouseAutomatisaion.Desktop.Wpf\MainWindow.xaml:161`
- `WarehouseAutomatisaion.Desktop.Wpf\MainWindow.xaml:171`
- `WarehouseAutomatisaion.Desktop.Wpf\MainWindow.xaml:181`

## 9. Конфигурация обновлений

В `WarehouseAutomatisaion.Desktop.Wpf\appsettings.json`:

```json
"ApplicationUpdate": {
  "Enabled": false,
  "GitHubOwner": "",
  "GitHubRepository": "MajorWarehause",
  "AssetName": "majorwarehause-win-x64.zip"
}
```

Это безопасно для разработки, но в рабочем релизе кнопка обновления не сможет реально обновлять приложение, пока не будет создан `appsettings.local.json` или релизная конфигурация с:

- `Enabled: true`;
- `GitHubOwner: "princceeodessa"`;
- `GitHubRepository: "WarehouseMajor"` или фактическое имя репозитория с релизами;
- корректный `AssetName`.

## 10. Архитектура и старые хвосты

Активных ссылок на WinForms в коде не найдено:

- нет `System.Windows.Forms`;
- нет `UseWindowsForms`;
- нет проекта WinForms в solution;
- `WarehouseAutomatisaion.Desktop` используется как shared/data/printing слой, не как WinForms shell.

Локально остаются служебные/игнорируемые директории:

- `_archive`;
- `_temp_build`;
- `_tmp_build`;
- `.vs`;
- `artifacts`;
- `app_data`.

Они не являются активным WinForms-кодом, но `_temp_build` и `_tmp_build` можно удалить отдельной уборкой, если они больше не нужны.

## 11. Ограничения проверки

Не выполнялось:

- реальная установка `MajorWarehauseSetup.exe` с созданием ярлыков, чтобы не менять пользовательское окружение;
- реальная печать;
- реальный импорт/экспорт через системные диалоги файлов;
- проверка удаленной MySQL-синхронизации;
- GitHub Actions rerun в этом прогоне.

Выполнялось:

- сборка;
- `dotnet test`;
- сборка setupper;
- запуск Debug и publish exe;
- UI Automation по всем разделам;
- основные редакторы создания;
- поиск по разделам;
- логическая проверка JSON-данных.

## 12. Рекомендованный следующий порядок исправлений

1. P0 по зависанию товаров исправлен: внешний `ScrollViewer` убран, высота таблицы ограничена layout-ом, виртуализация включена явно.
2. P1 по mojibake каталога частично исправлен: локальный `catalog-workspace.json` нормализуется при загрузке и сохранении.
3. P1 по битому `SupplierId` закупки исправлен: документы перепривязываются по единственному совпадающему имени поставщика.
4. P2 по UI Automation names боковой навигации исправлен.
5. Исправлен exit code setupper-скрипта: успешное создание `MajorWarehauseSetup.exe` больше не возвращает failure.
6. Добавить автоматический smoke-тест WPF-навигации: приложение запускается, кликаются все nav-кнопки, проверяется `Responding=True`.
7. Добавить data-integrity check для `app_data`: дубликаты кодов, битые ссылки клиентов/заказов/счетов, пустые обязательные поля.
8. Настроить релизную конфигурацию обновлений под GitHub repo и asset name.

## 13. Дополнительный прогон после фикса оставшихся P1

После первичных исправлений добавлен встроенный аудит целостности данных в раздел `Связи данных`. Это не разрушительная автоправка: приложение показывает проблемные записи и рекомендации, но не объединяет дубли и не перепривязывает счета/заказы без бизнес-правил.

Что теперь видно в приложении:

- критичные проблемы: дубли ключей, разорванные связи заказов/счетов/отгрузок/закупок;
- важные проблемы: документы без строк, нулевые цены, некорректные количества/цены;
- плановые замечания: неполные справочные данные, например товары без поставщика;
- карта функциональных сценариев осталась в этом же разделе ниже диагностических строк.

Повторные проверки:

```powershell
dotnet build .\WarehouseAutomatisaion.sln
dotnet test .\WarehouseAutomatisaion.sln --no-build
.\WarehouseAutomatisaion.Desktop.Wpf\publish-win-x64.ps1
.\scripts\build-majorwarehause-setup.ps1 -SkipPublish
git diff --check
```

Результат:

- `dotnet build` успешно, 0 warnings, 0 errors.
- `dotnet test` успешно, но реальных тестовых проектов/кейсов в solution нет.
- UI-smoke `NavModelButton`: `MODEL_SMOKE_RESPONDING=True`, `MODEL_SMOKE_TEXT_FOUND=True`, `MODEL_SMOKE_TEXT_COUNT=132`.
- publish `win-x64` успешно пересобран.
- setupper успешно создал `artifacts\installers\MajorWarehauseSetup.exe`.
- `git diff --check` без whitespace errors; остались только предупреждения Git про будущую замену LF на CRLF.

Статус оставшихся проблем:

- Автоматически закрыты безопасные проблемы: зависание товаров, mojibake в catalog store, битая ссылка закупки на поставщика, exit code сетапера, доступные имена nav-кнопок.
- Не закрыты автоправкой дубли артикулов, нулевые цены, пустые строки документов и разорванные связи счетов с заказами, потому что для них нужны правила владельца данных: что удалять, что объединять, какой документ считать основным.
- Эти проблемы теперь не скрыты: они выводятся пользователю в `Связи данных` как рабочий список исправлений.

## 14. Следующий шаг: закрытие безопасных заглушек

Проведен повторный поиск активных заглушек и пустых действий в WPF-логике. После исправлений в рабочем коде не осталось найденных маркеров `TODO`, `FIXME`, `NotImplemented`, `NotSupported`, `не реализовано`, `в разработке`, `скоро`, `пока недоступно`, `заглушка`.

Что доработано:

- В каталоге товаров добавлена выгрузка прайс-листа через меню `Действия товаров` -> `Выгрузить прайс-лист`.
- Экспорт создает CSV в папке `exports` рядом с exe, учитывает типы цен, активные скидки, округление цены и текущую выборку товаров.
- Кнопка действий в товарах получила доступное имя `Действия товаров`, чтобы UI Automation и пользовательские ассистивные инструменты могли стабильно находить элемент.
- Карта функционального покрытия обновлена: старые формулировки `Пока не реализовано` заменены на фактические статусы, чтобы отчет системы не показывал устаревшие заглушки.
- Добавлен повторяемый smoke-скрипт `scripts\run-wpf-smoke.ps1`: запуск приложения, клики по всем основным разделам, проверка меню каталога и выгрузки прайс-листа.

Проверки после доработки:

```powershell
dotnet build .\WarehouseAutomatisaion.sln
dotnet test .\WarehouseAutomatisaion.sln --no-build
.\WarehouseAutomatisaion.Desktop.Wpf\publish-win-x64.ps1
.\scripts\build-majorwarehause-setup.ps1 -SkipPublish
git diff --check
```

Результат:

- `dotnet build` успешно, 0 warnings, 0 errors.
- `dotnet test --no-build` успешно, но полноценных тестовых проектов в solution пока нет.
- publish `win-x64` успешно создал `artifacts\publish\majorwarehause-win-x64` и `artifacts\publish\majorwarehause-win-x64.zip`.
- setupper успешно создал `artifacts\installers\MajorWarehauseSetup.exe`.
- `git diff --check` без whitespace errors; остались только предупреждения Git про будущую замену LF на CRLF.
- UI-smoke каталога: `CATALOG_PRICE_LIST_SMOKE_RESPONDING=True`, `CATALOG_ACTIONS_NAME=Действия товаров`, `CATALOG_PRICE_LIST_MENU_FOUND=True`.
- Smoke выгрузки: `PRICE_LIST_EXPORT_CREATED=True`, `PRICE_LIST_EXPORT_LINES=9503`.
- Автоматический smoke-скрипт `.\scripts\run-wpf-smoke.ps1` успешно завершился: `WPF_SMOKE_NAVIGATION_OK=True`, `WPF_SMOKE_PRICE_LIST_MENU_FOUND=True`, `WPF_SMOKE_PRICE_LIST_EXPORT_CREATED=True`, `WPF_SMOKE_PRICE_LIST_EXPORT_LINES=9503`.

Оставшееся не является безопасной автоправкой:

- роли и строгие права рабочих мест требуют бизнес-правил доступа;
- вложения требуют решения по хранилищу файлов;
- автоматическое объединение дублей и исправление разорванных связей требует правил владельца данных.
