-- project-stock-balances.sql
--
-- Проекция остатков для UI: stock_balances → app_warehouse_stock_balances.
--
-- ИСТОЧНИК (Sprint 1):
--   stock_balances — операционная таблица остатков, наполнялась
--   scripts/Import-UnfPricesStockToSystem.ps1 из TSV-выгрузок 1С УНФ.
--   В Sprint 2 источник заменится на pull через OData.
--
-- ЦЕЛЬ:
--   app_warehouse_stock_balances — UI-таблица. Денормализована
--   (item_name, warehouse_name внутри), чтобы UI рендерил без JOIN.
--   Гранулярность: одна строка на (item, warehouse). Storage_bin/batch
--   разбивка пока схлопывается через SUM — добавится в спринте с ячейками.
--
-- ВАЖНО:
--   stock_balances.item_id ссылается на nomenclature_items.id
--   (НЕ на app_catalog_items.id — это два параллельных каталога в проде).
--   Поэтому JOIN с nomenclature_items для name/code.
--
-- ИДЕМПОТЕНТНОСТЬ:
--   UPSERT по UNIQUE (item_id, warehouse_node_id). ON DUPLICATE KEY UPDATE
--   перезаписывает количество и projected_at_utc.
--
-- TOMBSTONE:
--   Записи которые исчезли из stock_balances (товар сняли, склад убрали)
--   удаляются вторым запросом ниже. Запускать ПОСЛЕ UPSERT.

INSERT INTO app_warehouse_stock_balances (
    id, item_id, item_code, item_name,
    warehouse_node_id, warehouse_name,
    quantity, reserved_quantity, last_movement_at_utc,
    projected_at_utc
)
SELECT
    UUID() AS id,
    sb.item_id,
    ni.code AS item_code,
    ni.name AS item_name,
    sb.warehouse_node_id,
    wn.name AS warehouse_name,
    SUM(sb.quantity) AS quantity,
    SUM(sb.reserved_quantity) AS reserved_quantity,
    MAX(sb.last_movement_at_utc) AS last_movement_at_utc,
    NOW(6) AS projected_at_utc
FROM stock_balances sb
LEFT JOIN nomenclature_items ni ON ni.id = sb.item_id
LEFT JOIN warehouse_nodes wn ON wn.id = sb.warehouse_node_id
GROUP BY sb.item_id, sb.warehouse_node_id, ni.code, ni.name, wn.name
ON DUPLICATE KEY UPDATE
    item_code = VALUES(item_code),
    item_name = VALUES(item_name),
    warehouse_name = VALUES(warehouse_name),
    quantity = VALUES(quantity),
    reserved_quantity = VALUES(reserved_quantity),
    last_movement_at_utc = VALUES(last_movement_at_utc),
    projected_at_utc = VALUES(projected_at_utc);

-- Tombstone: удалить записи которых больше нет в источнике
DELETE app FROM app_warehouse_stock_balances app
LEFT JOIN (
    SELECT item_id, warehouse_node_id
    FROM stock_balances
    GROUP BY item_id, warehouse_node_id
) src ON src.item_id = app.item_id AND src.warehouse_node_id = app.warehouse_node_id
WHERE src.item_id IS NULL;
