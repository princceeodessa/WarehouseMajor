# MySQL Operational Schema

Первый SQL-контур вынесен в [`mysql-operational-schema.sql`](/C:/blagodar/WarehouseAutomatisaion/WarehouseAutomatisaion.Infrastructure/Persistence/Sql/mysql-operational-schema.sql).

Что в нем есть:

- нормализованный operational-layer для нового приложения: продажи, закупки, склад, номенклатура, цены, контрагенты и документы;
- ключевые связи между шапками документов, строками и справочниками;
- raw-layer `onec_*`, который сохраняет исходные поля 1С, табличные части и извлеченные ссылочные связи.

Зачем нужен raw-layer:

- не теряются редкие поля из старой 1С, которые пока не перенесены в нормализованную модель;
- можно поэтапно переносить формы и бизнес-логику, не теряя исходный источник правды;
- появляется место для автоматического анализа ссылок между объектами 1С.

Ближайший следующий шаг для интеграции:

1. создать bootstrap/initializer, который применяет этот SQL в MySQL;
2. выгружать `OneCImportSnapshot` не только в runtime desktop-приложения, но и в `onec_*` таблицы;
3. поверх raw-layer строить нормализованную загрузку в operational-layer.

Для ручного применения схемы уже добавлен скрипт [`apply-mysql-operational-schema.ps1`](/C:/blagodar/WarehouseAutomatisaion/scripts/apply-mysql-operational-schema.ps1).
