-- WarehouseAutomatisaion operational schema for MySQL 8+
-- This schema keeps two layers:
-- 1. normalized operational tables used by the new application
-- 2. raw 1C snapshot tables preserving original fields, tabular sections and extracted links

SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS organizations (
    id CHAR(36) NOT NULL,
    code VARCHAR(64) NOT NULL,
    name VARCHAR(256) NOT NULL,
    tax_id VARCHAR(64) NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_organizations PRIMARY KEY (id),
    CONSTRAINT uq_organizations_code UNIQUE (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS employees (
    id CHAR(36) NOT NULL,
    code VARCHAR(64) NOT NULL,
    full_name VARCHAR(256) NOT NULL,
    email VARCHAR(256) NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_employees PRIMARY KEY (id),
    CONSTRAINT uq_employees_code UNIQUE (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS business_partners (
    id CHAR(36) NOT NULL,
    code VARCHAR(64) NOT NULL,
    name VARCHAR(256) NOT NULL,
    roles INT UNSIGNED NOT NULL,
    parent_id CHAR(36) NULL,
    head_partner_id CHAR(36) NULL,
    default_bank_account_id CHAR(36) NULL,
    settlement_currency_code VARCHAR(16) NULL,
    country_id CHAR(36) NULL,
    responsible_employee_id CHAR(36) NULL,
    primary_contact_id CHAR(36) NULL,
    is_archived TINYINT(1) NOT NULL DEFAULT 0,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_business_partners PRIMARY KEY (id),
    CONSTRAINT uq_business_partners_code UNIQUE (code),
    CONSTRAINT fk_business_partners_parent
        FOREIGN KEY (parent_id) REFERENCES business_partners (id),
    CONSTRAINT fk_business_partners_head_partner
        FOREIGN KEY (head_partner_id) REFERENCES business_partners (id),
    CONSTRAINT fk_business_partners_responsible_employee
        FOREIGN KEY (responsible_employee_id) REFERENCES employees (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS bank_accounts (
    id CHAR(36) NOT NULL,
    organization_id CHAR(36) NULL,
    business_partner_id CHAR(36) NULL,
    account_number VARCHAR(64) NOT NULL,
    bank_name VARCHAR(256) NULL,
    currency_code VARCHAR(16) NOT NULL,
    is_default TINYINT(1) NOT NULL DEFAULT 0,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_bank_accounts PRIMARY KEY (id),
    CONSTRAINT uq_bank_accounts_account_number UNIQUE (account_number),
    CONSTRAINT fk_bank_accounts_organization
        FOREIGN KEY (organization_id) REFERENCES organizations (id),
    CONSTRAINT fk_bank_accounts_business_partner
        FOREIGN KEY (business_partner_id) REFERENCES business_partners (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS partner_contacts (
    id CHAR(36) NOT NULL,
    business_partner_id CHAR(36) NOT NULL,
    full_name VARCHAR(256) NOT NULL,
    phone VARCHAR(64) NULL,
    email VARCHAR(256) NULL,
    is_primary TINYINT(1) NOT NULL DEFAULT 0,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_partner_contacts PRIMARY KEY (id),
    CONSTRAINT fk_partner_contacts_business_partner
        FOREIGN KEY (business_partner_id) REFERENCES business_partners (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS partner_contracts (
    id CHAR(36) NOT NULL,
    number VARCHAR(64) NOT NULL,
    business_partner_id CHAR(36) NOT NULL,
    organization_id CHAR(36) NOT NULL,
    settlement_currency_code VARCHAR(16) NULL,
    requires_prepayment TINYINT(1) NOT NULL DEFAULT 0,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_partner_contracts PRIMARY KEY (id),
    CONSTRAINT uq_partner_contracts_number UNIQUE (number),
    CONSTRAINT fk_partner_contracts_business_partner
        FOREIGN KEY (business_partner_id) REFERENCES business_partners (id),
    CONSTRAINT fk_partner_contracts_organization
        FOREIGN KEY (organization_id) REFERENCES organizations (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS units_of_measure (
    id CHAR(36) NOT NULL,
    code VARCHAR(64) NOT NULL,
    name VARCHAR(128) NOT NULL,
    symbol VARCHAR(32) NULL,
    CONSTRAINT pk_units_of_measure PRIMARY KEY (id),
    CONSTRAINT uq_units_of_measure_code UNIQUE (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS item_categories (
    id CHAR(36) NOT NULL,
    parent_id CHAR(36) NULL,
    code VARCHAR(64) NOT NULL,
    name VARCHAR(256) NOT NULL,
    CONSTRAINT pk_item_categories PRIMARY KEY (id),
    CONSTRAINT uq_item_categories_code UNIQUE (code),
    CONSTRAINT fk_item_categories_parent
        FOREIGN KEY (parent_id) REFERENCES item_categories (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS price_groups (
    id CHAR(36) NOT NULL,
    code VARCHAR(64) NOT NULL,
    name VARCHAR(256) NOT NULL,
    CONSTRAINT pk_price_groups PRIMARY KEY (id),
    CONSTRAINT uq_price_groups_code UNIQUE (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS warehouse_nodes (
    id CHAR(36) NOT NULL,
    parent_id CHAR(36) NULL,
    code VARCHAR(64) NOT NULL,
    name VARCHAR(256) NOT NULL,
    type SMALLINT UNSIGNED NOT NULL,
    is_reserve_area TINYINT(1) NOT NULL DEFAULT 0,
    CONSTRAINT pk_warehouse_nodes PRIMARY KEY (id),
    CONSTRAINT uq_warehouse_nodes_code UNIQUE (code),
    CONSTRAINT fk_warehouse_nodes_parent
        FOREIGN KEY (parent_id) REFERENCES warehouse_nodes (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS storage_bins (
    id CHAR(36) NOT NULL,
    warehouse_node_id CHAR(36) NOT NULL,
    parent_bin_id CHAR(36) NULL,
    code VARCHAR(64) NOT NULL,
    name VARCHAR(256) NOT NULL,
    CONSTRAINT pk_storage_bins PRIMARY KEY (id),
    CONSTRAINT uq_storage_bins_code UNIQUE (code),
    CONSTRAINT fk_storage_bins_warehouse_node
        FOREIGN KEY (warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_storage_bins_parent_bin
        FOREIGN KEY (parent_bin_id) REFERENCES storage_bins (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS nomenclature_items (
    id CHAR(36) NOT NULL,
    parent_id CHAR(36) NULL,
    code VARCHAR(64) NOT NULL,
    sku VARCHAR(128) NOT NULL,
    name VARCHAR(256) NOT NULL,
    unit_of_measure_id CHAR(36) NULL,
    category_id CHAR(36) NULL,
    default_supplier_id CHAR(36) NULL,
    default_warehouse_node_id CHAR(36) NULL,
    default_storage_bin_id CHAR(36) NULL,
    price_group_id CHAR(36) NULL,
    item_kind VARCHAR(128) NULL,
    vat_rate_code VARCHAR(32) NULL,
    tracks_batches TINYINT(1) NOT NULL DEFAULT 0,
    tracks_serials TINYINT(1) NOT NULL DEFAULT 0,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_nomenclature_items PRIMARY KEY (id),
    CONSTRAINT uq_nomenclature_items_code UNIQUE (code),
    CONSTRAINT uq_nomenclature_items_sku UNIQUE (sku),
    CONSTRAINT fk_nomenclature_items_parent
        FOREIGN KEY (parent_id) REFERENCES nomenclature_items (id),
    CONSTRAINT fk_nomenclature_items_unit_of_measure
        FOREIGN KEY (unit_of_measure_id) REFERENCES units_of_measure (id),
    CONSTRAINT fk_nomenclature_items_category
        FOREIGN KEY (category_id) REFERENCES item_categories (id),
    CONSTRAINT fk_nomenclature_items_default_supplier
        FOREIGN KEY (default_supplier_id) REFERENCES business_partners (id),
    CONSTRAINT fk_nomenclature_items_default_warehouse_node
        FOREIGN KEY (default_warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_nomenclature_items_default_storage_bin
        FOREIGN KEY (default_storage_bin_id) REFERENCES storage_bins (id),
    CONSTRAINT fk_nomenclature_items_price_group
        FOREIGN KEY (price_group_id) REFERENCES price_groups (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS price_types (
    id CHAR(36) NOT NULL,
    code VARCHAR(64) NOT NULL,
    name VARCHAR(256) NOT NULL,
    currency_code VARCHAR(16) NOT NULL,
    base_price_type_id CHAR(36) NULL,
    is_manual_entry_only TINYINT(1) NOT NULL DEFAULT 0,
    uses_psychological_rounding TINYINT(1) NOT NULL DEFAULT 0,
    CONSTRAINT pk_price_types PRIMARY KEY (id),
    CONSTRAINT uq_price_types_code UNIQUE (code),
    CONSTRAINT fk_price_types_base_price_type
        FOREIGN KEY (base_price_type_id) REFERENCES price_types (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS price_type_rounding_rules (
    id CHAR(36) NOT NULL,
    price_type_id CHAR(36) NOT NULL,
    threshold_amount DECIMAL(18, 4) NOT NULL,
    precision_digits INT NOT NULL,
    step_amount DECIMAL(18, 4) NOT NULL,
    CONSTRAINT pk_price_type_rounding_rules PRIMARY KEY (id),
    CONSTRAINT fk_price_type_rounding_rules_price_type
        FOREIGN KEY (price_type_id) REFERENCES price_types (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS discount_policies (
    id CHAR(36) NOT NULL,
    code VARCHAR(64) NOT NULL,
    name VARCHAR(256) NOT NULL,
    price_type_id CHAR(36) NULL,
    currency_code VARCHAR(16) NOT NULL,
    starts_on DATE NULL,
    ends_on DATE NULL,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    kind SMALLINT UNSIGNED NOT NULL,
    scope SMALLINT UNSIGNED NOT NULL,
    value_amount DECIMAL(18, 4) NOT NULL,
    CONSTRAINT pk_discount_policies PRIMARY KEY (id),
    CONSTRAINT uq_discount_policies_code UNIQUE (code),
    CONSTRAINT fk_discount_policies_price_type
        FOREIGN KEY (price_type_id) REFERENCES price_types (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS discount_policy_partners (
    discount_policy_id CHAR(36) NOT NULL,
    business_partner_id CHAR(36) NOT NULL,
    CONSTRAINT pk_discount_policy_partners PRIMARY KEY (discount_policy_id, business_partner_id),
    CONSTRAINT fk_discount_policy_partners_policy
        FOREIGN KEY (discount_policy_id) REFERENCES discount_policies (id),
    CONSTRAINT fk_discount_policy_partners_partner
        FOREIGN KEY (business_partner_id) REFERENCES business_partners (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS discount_policy_warehouse_nodes (
    discount_policy_id CHAR(36) NOT NULL,
    warehouse_node_id CHAR(36) NOT NULL,
    CONSTRAINT pk_discount_policy_warehouse_nodes PRIMARY KEY (discount_policy_id, warehouse_node_id),
    CONSTRAINT fk_discount_policy_warehouse_nodes_policy
        FOREIGN KEY (discount_policy_id) REFERENCES discount_policies (id),
    CONSTRAINT fk_discount_policy_warehouse_nodes_warehouse_node
        FOREIGN KEY (warehouse_node_id) REFERENCES warehouse_nodes (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS discount_policy_item_categories (
    discount_policy_id CHAR(36) NOT NULL,
    item_category_id CHAR(36) NOT NULL,
    CONSTRAINT pk_discount_policy_item_categories PRIMARY KEY (discount_policy_id, item_category_id),
    CONSTRAINT fk_discount_policy_item_categories_policy
        FOREIGN KEY (discount_policy_id) REFERENCES discount_policies (id),
    CONSTRAINT fk_discount_policy_item_categories_item_category
        FOREIGN KEY (item_category_id) REFERENCES item_categories (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS discount_policy_price_groups (
    discount_policy_id CHAR(36) NOT NULL,
    price_group_id CHAR(36) NOT NULL,
    CONSTRAINT pk_discount_policy_price_groups PRIMARY KEY (discount_policy_id, price_group_id),
    CONSTRAINT fk_discount_policy_price_groups_policy
        FOREIGN KEY (discount_policy_id) REFERENCES discount_policies (id),
    CONSTRAINT fk_discount_policy_price_groups_price_group
        FOREIGN KEY (price_group_id) REFERENCES price_groups (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS stock_balances (
    id CHAR(36) NOT NULL,
    item_id CHAR(36) NOT NULL,
    warehouse_node_id CHAR(36) NOT NULL,
    storage_bin_id CHAR(36) NULL,
    batch_id CHAR(36) NULL,
    quantity DECIMAL(18, 4) NOT NULL,
    reserved_quantity DECIMAL(18, 4) NOT NULL DEFAULT 0,
    last_movement_at_utc DATETIME(6) NOT NULL,
    CONSTRAINT pk_stock_balances PRIMARY KEY (id),
    CONSTRAINT uq_stock_balances_location UNIQUE (item_id, warehouse_node_id, storage_bin_id, batch_id),
    CONSTRAINT fk_stock_balances_item
        FOREIGN KEY (item_id) REFERENCES nomenclature_items (id),
    CONSTRAINT fk_stock_balances_warehouse_node
        FOREIGN KEY (warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_stock_balances_storage_bin
        FOREIGN KEY (storage_bin_id) REFERENCES storage_bins (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS sales_orders (
    id CHAR(36) NOT NULL,
    number VARCHAR(64) NOT NULL,
    document_date DATETIME(6) NOT NULL,
    posting_state SMALLINT UNSIGNED NOT NULL,
    organization_id CHAR(36) NOT NULL,
    author_id CHAR(36) NULL,
    responsible_employee_id CHAR(36) NULL,
    comment_text TEXT NULL,
    base_document_id CHAR(36) NULL,
    project_id CHAR(36) NULL,
    currency_code VARCHAR(16) NOT NULL,
    customer_id CHAR(36) NOT NULL,
    contract_id CHAR(36) NULL,
    price_type_id CHAR(36) NULL,
    warehouse_node_id CHAR(36) NULL,
    reserve_warehouse_node_id CHAR(36) NULL,
    storage_bin_id CHAR(36) NULL,
    lifecycle_status SMALLINT UNSIGNED NOT NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_sales_orders PRIMARY KEY (id),
    CONSTRAINT uq_sales_orders_number UNIQUE (number),
    CONSTRAINT fk_sales_orders_organization FOREIGN KEY (organization_id) REFERENCES organizations (id),
    CONSTRAINT fk_sales_orders_author FOREIGN KEY (author_id) REFERENCES employees (id),
    CONSTRAINT fk_sales_orders_responsible_employee FOREIGN KEY (responsible_employee_id) REFERENCES employees (id),
    CONSTRAINT fk_sales_orders_customer FOREIGN KEY (customer_id) REFERENCES business_partners (id),
    CONSTRAINT fk_sales_orders_contract FOREIGN KEY (contract_id) REFERENCES partner_contracts (id),
    CONSTRAINT fk_sales_orders_price_type FOREIGN KEY (price_type_id) REFERENCES price_types (id),
    CONSTRAINT fk_sales_orders_warehouse_node FOREIGN KEY (warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_sales_orders_reserve_warehouse_node FOREIGN KEY (reserve_warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_sales_orders_storage_bin FOREIGN KEY (storage_bin_id) REFERENCES storage_bins (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS sales_order_lines (
    id CHAR(36) NOT NULL,
    sales_order_id CHAR(36) NOT NULL,
    line_no INT UNSIGNED NOT NULL,
    item_id CHAR(36) NOT NULL,
    characteristic_id CHAR(36) NULL,
    batch_id CHAR(36) NULL,
    unit_of_measure_id CHAR(36) NULL,
    quantity DECIMAL(18, 4) NOT NULL,
    price DECIMAL(18, 4) NOT NULL,
    discount_percent DECIMAL(9, 4) NOT NULL DEFAULT 0,
    discount_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    amount DECIMAL(18, 4) NOT NULL,
    vat_rate_code VARCHAR(32) NULL,
    tax_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    total DECIMAL(18, 4) NOT NULL,
    content_text VARCHAR(512) NULL,
    CONSTRAINT pk_sales_order_lines PRIMARY KEY (id),
    CONSTRAINT uq_sales_order_lines_order_line UNIQUE (sales_order_id, line_no),
    CONSTRAINT fk_sales_order_lines_order FOREIGN KEY (sales_order_id) REFERENCES sales_orders (id) ON DELETE CASCADE,
    CONSTRAINT fk_sales_order_lines_item FOREIGN KEY (item_id) REFERENCES nomenclature_items (id),
    CONSTRAINT fk_sales_order_lines_unit_of_measure FOREIGN KEY (unit_of_measure_id) REFERENCES units_of_measure (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS sales_order_payment_schedule (
    id CHAR(36) NOT NULL,
    sales_order_id CHAR(36) NOT NULL,
    line_no INT UNSIGNED NOT NULL,
    due_date DATE NOT NULL,
    payment_percent DECIMAL(9, 4) NOT NULL DEFAULT 0,
    amount DECIMAL(18, 4) NOT NULL,
    tax_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    CONSTRAINT pk_sales_order_payment_schedule PRIMARY KEY (id),
    CONSTRAINT uq_sales_order_payment_schedule_line UNIQUE (sales_order_id, line_no),
    CONSTRAINT fk_sales_order_payment_schedule_order FOREIGN KEY (sales_order_id) REFERENCES sales_orders (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS sales_invoices (
    id CHAR(36) NOT NULL,
    number VARCHAR(64) NOT NULL,
    document_date DATETIME(6) NOT NULL,
    posting_state SMALLINT UNSIGNED NOT NULL,
    organization_id CHAR(36) NOT NULL,
    author_id CHAR(36) NULL,
    responsible_employee_id CHAR(36) NULL,
    comment_text TEXT NULL,
    base_document_id CHAR(36) NULL,
    project_id CHAR(36) NULL,
    currency_code VARCHAR(16) NOT NULL,
    customer_id CHAR(36) NOT NULL,
    contract_id CHAR(36) NULL,
    price_type_id CHAR(36) NULL,
    company_bank_account_id CHAR(36) NULL,
    cashbox_id CHAR(36) NULL,
    lifecycle_status SMALLINT UNSIGNED NOT NULL,
    total_amount DECIMAL(18, 4) NOT NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_sales_invoices PRIMARY KEY (id),
    CONSTRAINT uq_sales_invoices_number UNIQUE (number),
    CONSTRAINT fk_sales_invoices_organization FOREIGN KEY (organization_id) REFERENCES organizations (id),
    CONSTRAINT fk_sales_invoices_author FOREIGN KEY (author_id) REFERENCES employees (id),
    CONSTRAINT fk_sales_invoices_responsible_employee FOREIGN KEY (responsible_employee_id) REFERENCES employees (id),
    CONSTRAINT fk_sales_invoices_customer FOREIGN KEY (customer_id) REFERENCES business_partners (id),
    CONSTRAINT fk_sales_invoices_contract FOREIGN KEY (contract_id) REFERENCES partner_contracts (id),
    CONSTRAINT fk_sales_invoices_price_type FOREIGN KEY (price_type_id) REFERENCES price_types (id),
    CONSTRAINT fk_sales_invoices_company_bank_account FOREIGN KEY (company_bank_account_id) REFERENCES bank_accounts (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS sales_invoice_lines (
    id CHAR(36) NOT NULL,
    sales_invoice_id CHAR(36) NOT NULL,
    line_no INT UNSIGNED NOT NULL,
    item_id CHAR(36) NOT NULL,
    characteristic_id CHAR(36) NULL,
    batch_id CHAR(36) NULL,
    unit_of_measure_id CHAR(36) NULL,
    quantity DECIMAL(18, 4) NOT NULL,
    price DECIMAL(18, 4) NOT NULL,
    discount_percent DECIMAL(9, 4) NOT NULL DEFAULT 0,
    discount_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    amount DECIMAL(18, 4) NOT NULL,
    vat_rate_code VARCHAR(32) NULL,
    tax_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    total DECIMAL(18, 4) NOT NULL,
    content_text VARCHAR(512) NULL,
    CONSTRAINT pk_sales_invoice_lines PRIMARY KEY (id),
    CONSTRAINT uq_sales_invoice_lines_invoice_line UNIQUE (sales_invoice_id, line_no),
    CONSTRAINT fk_sales_invoice_lines_invoice FOREIGN KEY (sales_invoice_id) REFERENCES sales_invoices (id) ON DELETE CASCADE,
    CONSTRAINT fk_sales_invoice_lines_item FOREIGN KEY (item_id) REFERENCES nomenclature_items (id),
    CONSTRAINT fk_sales_invoice_lines_unit_of_measure FOREIGN KEY (unit_of_measure_id) REFERENCES units_of_measure (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS sales_invoice_payment_schedule (
    id CHAR(36) NOT NULL,
    sales_invoice_id CHAR(36) NOT NULL,
    line_no INT UNSIGNED NOT NULL,
    due_date DATE NOT NULL,
    payment_percent DECIMAL(9, 4) NOT NULL DEFAULT 0,
    amount DECIMAL(18, 4) NOT NULL,
    tax_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    CONSTRAINT pk_sales_invoice_payment_schedule PRIMARY KEY (id),
    CONSTRAINT uq_sales_invoice_payment_schedule_line UNIQUE (sales_invoice_id, line_no),
    CONSTRAINT fk_sales_invoice_payment_schedule_invoice FOREIGN KEY (sales_invoice_id) REFERENCES sales_invoices (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS sales_shipments (
    id CHAR(36) NOT NULL,
    number VARCHAR(64) NOT NULL,
    document_date DATETIME(6) NOT NULL,
    posting_state SMALLINT UNSIGNED NOT NULL,
    organization_id CHAR(36) NOT NULL,
    author_id CHAR(36) NULL,
    responsible_employee_id CHAR(36) NULL,
    comment_text TEXT NULL,
    base_document_id CHAR(36) NULL,
    project_id CHAR(36) NULL,
    currency_code VARCHAR(16) NOT NULL,
    customer_id CHAR(36) NOT NULL,
    contract_id CHAR(36) NULL,
    sales_order_id CHAR(36) NULL,
    price_type_id CHAR(36) NULL,
    warehouse_node_id CHAR(36) NULL,
    storage_bin_id CHAR(36) NULL,
    carrier_id CHAR(36) NULL,
    total_amount DECIMAL(18, 4) NOT NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_sales_shipments PRIMARY KEY (id),
    CONSTRAINT uq_sales_shipments_number UNIQUE (number),
    CONSTRAINT fk_sales_shipments_organization FOREIGN KEY (organization_id) REFERENCES organizations (id),
    CONSTRAINT fk_sales_shipments_author FOREIGN KEY (author_id) REFERENCES employees (id),
    CONSTRAINT fk_sales_shipments_responsible_employee FOREIGN KEY (responsible_employee_id) REFERENCES employees (id),
    CONSTRAINT fk_sales_shipments_customer FOREIGN KEY (customer_id) REFERENCES business_partners (id),
    CONSTRAINT fk_sales_shipments_contract FOREIGN KEY (contract_id) REFERENCES partner_contracts (id),
    CONSTRAINT fk_sales_shipments_sales_order FOREIGN KEY (sales_order_id) REFERENCES sales_orders (id),
    CONSTRAINT fk_sales_shipments_price_type FOREIGN KEY (price_type_id) REFERENCES price_types (id),
    CONSTRAINT fk_sales_shipments_warehouse_node FOREIGN KEY (warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_sales_shipments_storage_bin FOREIGN KEY (storage_bin_id) REFERENCES storage_bins (id),
    CONSTRAINT fk_sales_shipments_carrier FOREIGN KEY (carrier_id) REFERENCES business_partners (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS sales_shipment_lines (
    id CHAR(36) NOT NULL,
    sales_shipment_id CHAR(36) NOT NULL,
    line_no INT UNSIGNED NOT NULL,
    item_id CHAR(36) NOT NULL,
    characteristic_id CHAR(36) NULL,
    batch_id CHAR(36) NULL,
    unit_of_measure_id CHAR(36) NULL,
    quantity DECIMAL(18, 4) NOT NULL,
    price DECIMAL(18, 4) NOT NULL,
    discount_percent DECIMAL(9, 4) NOT NULL DEFAULT 0,
    discount_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    amount DECIMAL(18, 4) NOT NULL,
    vat_rate_code VARCHAR(32) NULL,
    tax_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    total DECIMAL(18, 4) NOT NULL,
    content_text VARCHAR(512) NULL,
    CONSTRAINT pk_sales_shipment_lines PRIMARY KEY (id),
    CONSTRAINT uq_sales_shipment_lines_shipment_line UNIQUE (sales_shipment_id, line_no),
    CONSTRAINT fk_sales_shipment_lines_shipment FOREIGN KEY (sales_shipment_id) REFERENCES sales_shipments (id) ON DELETE CASCADE,
    CONSTRAINT fk_sales_shipment_lines_item FOREIGN KEY (item_id) REFERENCES nomenclature_items (id),
    CONSTRAINT fk_sales_shipment_lines_unit_of_measure FOREIGN KEY (unit_of_measure_id) REFERENCES units_of_measure (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS purchase_orders (
    id CHAR(36) NOT NULL,
    number VARCHAR(64) NOT NULL,
    document_date DATETIME(6) NOT NULL,
    posting_state SMALLINT UNSIGNED NOT NULL,
    organization_id CHAR(36) NOT NULL,
    author_id CHAR(36) NULL,
    responsible_employee_id CHAR(36) NULL,
    comment_text TEXT NULL,
    base_document_id CHAR(36) NULL,
    project_id CHAR(36) NULL,
    currency_code VARCHAR(16) NOT NULL,
    supplier_id CHAR(36) NOT NULL,
    contract_id CHAR(36) NULL,
    partner_price_type_id CHAR(36) NULL,
    linked_sales_order_id CHAR(36) NULL,
    warehouse_node_id CHAR(36) NULL,
    reserve_warehouse_node_id CHAR(36) NULL,
    lifecycle_status SMALLINT UNSIGNED NOT NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_purchase_orders PRIMARY KEY (id),
    CONSTRAINT uq_purchase_orders_number UNIQUE (number),
    CONSTRAINT fk_purchase_orders_organization FOREIGN KEY (organization_id) REFERENCES organizations (id),
    CONSTRAINT fk_purchase_orders_author FOREIGN KEY (author_id) REFERENCES employees (id),
    CONSTRAINT fk_purchase_orders_responsible_employee FOREIGN KEY (responsible_employee_id) REFERENCES employees (id),
    CONSTRAINT fk_purchase_orders_supplier FOREIGN KEY (supplier_id) REFERENCES business_partners (id),
    CONSTRAINT fk_purchase_orders_contract FOREIGN KEY (contract_id) REFERENCES partner_contracts (id),
    CONSTRAINT fk_purchase_orders_partner_price_type FOREIGN KEY (partner_price_type_id) REFERENCES price_types (id),
    CONSTRAINT fk_purchase_orders_linked_sales_order FOREIGN KEY (linked_sales_order_id) REFERENCES sales_orders (id),
    CONSTRAINT fk_purchase_orders_warehouse_node FOREIGN KEY (warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_purchase_orders_reserve_warehouse_node FOREIGN KEY (reserve_warehouse_node_id) REFERENCES warehouse_nodes (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS purchase_order_lines (
    id CHAR(36) NOT NULL,
    purchase_order_id CHAR(36) NOT NULL,
    line_no INT UNSIGNED NOT NULL,
    item_id CHAR(36) NOT NULL,
    characteristic_id CHAR(36) NULL,
    batch_id CHAR(36) NULL,
    unit_of_measure_id CHAR(36) NULL,
    quantity DECIMAL(18, 4) NOT NULL,
    price DECIMAL(18, 4) NOT NULL,
    discount_percent DECIMAL(9, 4) NOT NULL DEFAULT 0,
    discount_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    amount DECIMAL(18, 4) NOT NULL,
    vat_rate_code VARCHAR(32) NULL,
    tax_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    total DECIMAL(18, 4) NOT NULL,
    content_text VARCHAR(512) NULL,
    CONSTRAINT pk_purchase_order_lines PRIMARY KEY (id),
    CONSTRAINT uq_purchase_order_lines_order_line UNIQUE (purchase_order_id, line_no),
    CONSTRAINT fk_purchase_order_lines_order FOREIGN KEY (purchase_order_id) REFERENCES purchase_orders (id) ON DELETE CASCADE,
    CONSTRAINT fk_purchase_order_lines_item FOREIGN KEY (item_id) REFERENCES nomenclature_items (id),
    CONSTRAINT fk_purchase_order_lines_unit_of_measure FOREIGN KEY (unit_of_measure_id) REFERENCES units_of_measure (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS purchase_order_payment_schedule (
    id CHAR(36) NOT NULL,
    purchase_order_id CHAR(36) NOT NULL,
    line_no INT UNSIGNED NOT NULL,
    due_date DATE NOT NULL,
    payment_percent DECIMAL(9, 4) NOT NULL DEFAULT 0,
    amount DECIMAL(18, 4) NOT NULL,
    tax_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    CONSTRAINT pk_purchase_order_payment_schedule PRIMARY KEY (id),
    CONSTRAINT uq_purchase_order_payment_schedule_line UNIQUE (purchase_order_id, line_no),
    CONSTRAINT fk_purchase_order_payment_schedule_order FOREIGN KEY (purchase_order_id) REFERENCES purchase_orders (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS supplier_invoices (
    id CHAR(36) NOT NULL,
    number VARCHAR(64) NOT NULL,
    document_date DATETIME(6) NOT NULL,
    posting_state SMALLINT UNSIGNED NOT NULL,
    organization_id CHAR(36) NOT NULL,
    author_id CHAR(36) NULL,
    responsible_employee_id CHAR(36) NULL,
    comment_text TEXT NULL,
    base_document_id CHAR(36) NULL,
    project_id CHAR(36) NULL,
    currency_code VARCHAR(16) NOT NULL,
    supplier_id CHAR(36) NOT NULL,
    contract_id CHAR(36) NULL,
    purchase_order_id CHAR(36) NULL,
    company_bank_account_id CHAR(36) NULL,
    supplier_bank_account_id CHAR(36) NULL,
    cashbox_id CHAR(36) NULL,
    partner_price_type_id CHAR(36) NULL,
    total_amount DECIMAL(18, 4) NOT NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_supplier_invoices PRIMARY KEY (id),
    CONSTRAINT uq_supplier_invoices_number UNIQUE (number),
    CONSTRAINT fk_supplier_invoices_organization FOREIGN KEY (organization_id) REFERENCES organizations (id),
    CONSTRAINT fk_supplier_invoices_author FOREIGN KEY (author_id) REFERENCES employees (id),
    CONSTRAINT fk_supplier_invoices_responsible_employee FOREIGN KEY (responsible_employee_id) REFERENCES employees (id),
    CONSTRAINT fk_supplier_invoices_supplier FOREIGN KEY (supplier_id) REFERENCES business_partners (id),
    CONSTRAINT fk_supplier_invoices_contract FOREIGN KEY (contract_id) REFERENCES partner_contracts (id),
    CONSTRAINT fk_supplier_invoices_purchase_order FOREIGN KEY (purchase_order_id) REFERENCES purchase_orders (id),
    CONSTRAINT fk_supplier_invoices_company_bank_account FOREIGN KEY (company_bank_account_id) REFERENCES bank_accounts (id),
    CONSTRAINT fk_supplier_invoices_supplier_bank_account FOREIGN KEY (supplier_bank_account_id) REFERENCES bank_accounts (id),
    CONSTRAINT fk_supplier_invoices_partner_price_type FOREIGN KEY (partner_price_type_id) REFERENCES price_types (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS supplier_invoice_lines (
    id CHAR(36) NOT NULL,
    supplier_invoice_id CHAR(36) NOT NULL,
    line_no INT UNSIGNED NOT NULL,
    item_id CHAR(36) NOT NULL,
    characteristic_id CHAR(36) NULL,
    batch_id CHAR(36) NULL,
    unit_of_measure_id CHAR(36) NULL,
    quantity DECIMAL(18, 4) NOT NULL,
    price DECIMAL(18, 4) NOT NULL,
    discount_percent DECIMAL(9, 4) NOT NULL DEFAULT 0,
    discount_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    amount DECIMAL(18, 4) NOT NULL,
    vat_rate_code VARCHAR(32) NULL,
    tax_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    total DECIMAL(18, 4) NOT NULL,
    content_text VARCHAR(512) NULL,
    CONSTRAINT pk_supplier_invoice_lines PRIMARY KEY (id),
    CONSTRAINT uq_supplier_invoice_lines_invoice_line UNIQUE (supplier_invoice_id, line_no),
    CONSTRAINT fk_supplier_invoice_lines_invoice FOREIGN KEY (supplier_invoice_id) REFERENCES supplier_invoices (id) ON DELETE CASCADE,
    CONSTRAINT fk_supplier_invoice_lines_item FOREIGN KEY (item_id) REFERENCES nomenclature_items (id),
    CONSTRAINT fk_supplier_invoice_lines_unit_of_measure FOREIGN KEY (unit_of_measure_id) REFERENCES units_of_measure (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS supplier_invoice_payment_schedule (
    id CHAR(36) NOT NULL,
    supplier_invoice_id CHAR(36) NOT NULL,
    line_no INT UNSIGNED NOT NULL,
    due_date DATE NOT NULL,
    payment_percent DECIMAL(9, 4) NOT NULL DEFAULT 0,
    amount DECIMAL(18, 4) NOT NULL,
    tax_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    CONSTRAINT pk_supplier_invoice_payment_schedule PRIMARY KEY (id),
    CONSTRAINT uq_supplier_invoice_payment_schedule_line UNIQUE (supplier_invoice_id, line_no),
    CONSTRAINT fk_supplier_invoice_payment_schedule_invoice FOREIGN KEY (supplier_invoice_id) REFERENCES supplier_invoices (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS purchase_receipts (
    id CHAR(36) NOT NULL,
    number VARCHAR(64) NOT NULL,
    document_date DATETIME(6) NOT NULL,
    posting_state SMALLINT UNSIGNED NOT NULL,
    organization_id CHAR(36) NOT NULL,
    author_id CHAR(36) NULL,
    responsible_employee_id CHAR(36) NULL,
    comment_text TEXT NULL,
    base_document_id CHAR(36) NULL,
    project_id CHAR(36) NULL,
    currency_code VARCHAR(16) NOT NULL,
    supplier_id CHAR(36) NOT NULL,
    contract_id CHAR(36) NULL,
    purchase_order_id CHAR(36) NULL,
    warehouse_node_id CHAR(36) NULL,
    storage_bin_id CHAR(36) NULL,
    partner_price_type_id CHAR(36) NULL,
    total_amount DECIMAL(18, 4) NOT NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_purchase_receipts PRIMARY KEY (id),
    CONSTRAINT uq_purchase_receipts_number UNIQUE (number),
    CONSTRAINT fk_purchase_receipts_organization FOREIGN KEY (organization_id) REFERENCES organizations (id),
    CONSTRAINT fk_purchase_receipts_author FOREIGN KEY (author_id) REFERENCES employees (id),
    CONSTRAINT fk_purchase_receipts_responsible_employee FOREIGN KEY (responsible_employee_id) REFERENCES employees (id),
    CONSTRAINT fk_purchase_receipts_supplier FOREIGN KEY (supplier_id) REFERENCES business_partners (id),
    CONSTRAINT fk_purchase_receipts_contract FOREIGN KEY (contract_id) REFERENCES partner_contracts (id),
    CONSTRAINT fk_purchase_receipts_purchase_order FOREIGN KEY (purchase_order_id) REFERENCES purchase_orders (id),
    CONSTRAINT fk_purchase_receipts_warehouse_node FOREIGN KEY (warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_purchase_receipts_storage_bin FOREIGN KEY (storage_bin_id) REFERENCES storage_bins (id),
    CONSTRAINT fk_purchase_receipts_partner_price_type FOREIGN KEY (partner_price_type_id) REFERENCES price_types (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS purchase_receipt_lines (
    id CHAR(36) NOT NULL,
    purchase_receipt_id CHAR(36) NOT NULL,
    line_no INT UNSIGNED NOT NULL,
    item_id CHAR(36) NOT NULL,
    characteristic_id CHAR(36) NULL,
    batch_id CHAR(36) NULL,
    unit_of_measure_id CHAR(36) NULL,
    quantity DECIMAL(18, 4) NOT NULL,
    price DECIMAL(18, 4) NOT NULL,
    discount_percent DECIMAL(9, 4) NOT NULL DEFAULT 0,
    discount_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    amount DECIMAL(18, 4) NOT NULL,
    vat_rate_code VARCHAR(32) NULL,
    tax_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    total DECIMAL(18, 4) NOT NULL,
    content_text VARCHAR(512) NULL,
    CONSTRAINT pk_purchase_receipt_lines PRIMARY KEY (id),
    CONSTRAINT uq_purchase_receipt_lines_receipt_line UNIQUE (purchase_receipt_id, line_no),
    CONSTRAINT fk_purchase_receipt_lines_receipt FOREIGN KEY (purchase_receipt_id) REFERENCES purchase_receipts (id) ON DELETE CASCADE,
    CONSTRAINT fk_purchase_receipt_lines_item FOREIGN KEY (item_id) REFERENCES nomenclature_items (id),
    CONSTRAINT fk_purchase_receipt_lines_unit_of_measure FOREIGN KEY (unit_of_measure_id) REFERENCES units_of_measure (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS purchase_receipt_additional_charges (
    id CHAR(36) NOT NULL,
    purchase_receipt_id CHAR(36) NOT NULL,
    line_no INT UNSIGNED NOT NULL,
    charge_name VARCHAR(256) NOT NULL,
    amount DECIMAL(18, 4) NOT NULL,
    allocation_rule VARCHAR(128) NULL,
    CONSTRAINT pk_purchase_receipt_additional_charges PRIMARY KEY (id),
    CONSTRAINT uq_purchase_receipt_additional_charges_line UNIQUE (purchase_receipt_id, line_no),
    CONSTRAINT fk_purchase_receipt_additional_charges_receipt FOREIGN KEY (purchase_receipt_id) REFERENCES purchase_receipts (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS transfer_orders (
    id CHAR(36) NOT NULL,
    number VARCHAR(64) NOT NULL,
    document_date DATETIME(6) NOT NULL,
    posting_state SMALLINT UNSIGNED NOT NULL,
    organization_id CHAR(36) NOT NULL,
    author_id CHAR(36) NULL,
    responsible_employee_id CHAR(36) NULL,
    comment_text TEXT NULL,
    base_document_id CHAR(36) NULL,
    project_id CHAR(36) NULL,
    customer_order_id CHAR(36) NULL,
    source_warehouse_node_id CHAR(36) NOT NULL,
    target_warehouse_node_id CHAR(36) NOT NULL,
    requested_transfer_date DATE NOT NULL,
    lifecycle_status SMALLINT UNSIGNED NOT NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_transfer_orders PRIMARY KEY (id),
    CONSTRAINT uq_transfer_orders_number UNIQUE (number),
    CONSTRAINT fk_transfer_orders_organization FOREIGN KEY (organization_id) REFERENCES organizations (id),
    CONSTRAINT fk_transfer_orders_author FOREIGN KEY (author_id) REFERENCES employees (id),
    CONSTRAINT fk_transfer_orders_responsible_employee FOREIGN KEY (responsible_employee_id) REFERENCES employees (id),
    CONSTRAINT fk_transfer_orders_customer_order FOREIGN KEY (customer_order_id) REFERENCES sales_orders (id),
    CONSTRAINT fk_transfer_orders_source_warehouse_node FOREIGN KEY (source_warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_transfer_orders_target_warehouse_node FOREIGN KEY (target_warehouse_node_id) REFERENCES warehouse_nodes (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS transfer_order_lines (
    id CHAR(36) NOT NULL,
    transfer_order_id CHAR(36) NOT NULL,
    line_no INT UNSIGNED NOT NULL,
    item_id CHAR(36) NOT NULL,
    characteristic_id CHAR(36) NULL,
    batch_id CHAR(36) NULL,
    unit_of_measure_id CHAR(36) NULL,
    quantity DECIMAL(18, 4) NOT NULL,
    source_warehouse_node_id CHAR(36) NULL,
    source_storage_bin_id CHAR(36) NULL,
    target_warehouse_node_id CHAR(36) NULL,
    target_storage_bin_id CHAR(36) NULL,
    reserved_quantity DECIMAL(18, 4) NOT NULL DEFAULT 0,
    collected_quantity DECIMAL(18, 4) NOT NULL DEFAULT 0,
    CONSTRAINT pk_transfer_order_lines PRIMARY KEY (id),
    CONSTRAINT uq_transfer_order_lines_order_line UNIQUE (transfer_order_id, line_no),
    CONSTRAINT fk_transfer_order_lines_order FOREIGN KEY (transfer_order_id) REFERENCES transfer_orders (id) ON DELETE CASCADE,
    CONSTRAINT fk_transfer_order_lines_item FOREIGN KEY (item_id) REFERENCES nomenclature_items (id),
    CONSTRAINT fk_transfer_order_lines_unit_of_measure FOREIGN KEY (unit_of_measure_id) REFERENCES units_of_measure (id),
    CONSTRAINT fk_transfer_order_lines_source_warehouse_node FOREIGN KEY (source_warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_transfer_order_lines_source_storage_bin FOREIGN KEY (source_storage_bin_id) REFERENCES storage_bins (id),
    CONSTRAINT fk_transfer_order_lines_target_warehouse_node FOREIGN KEY (target_warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_transfer_order_lines_target_storage_bin FOREIGN KEY (target_storage_bin_id) REFERENCES storage_bins (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS stock_transfers (
    id CHAR(36) NOT NULL,
    number VARCHAR(64) NOT NULL,
    document_date DATETIME(6) NOT NULL,
    posting_state SMALLINT UNSIGNED NOT NULL,
    organization_id CHAR(36) NOT NULL,
    author_id CHAR(36) NULL,
    responsible_employee_id CHAR(36) NULL,
    comment_text TEXT NULL,
    base_document_id CHAR(36) NULL,
    project_id CHAR(36) NULL,
    transfer_order_id CHAR(36) NULL,
    source_warehouse_node_id CHAR(36) NOT NULL,
    target_warehouse_node_id CHAR(36) NOT NULL,
    source_storage_bin_id CHAR(36) NULL,
    target_storage_bin_id CHAR(36) NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_stock_transfers PRIMARY KEY (id),
    CONSTRAINT uq_stock_transfers_number UNIQUE (number),
    CONSTRAINT fk_stock_transfers_organization FOREIGN KEY (organization_id) REFERENCES organizations (id),
    CONSTRAINT fk_stock_transfers_author FOREIGN KEY (author_id) REFERENCES employees (id),
    CONSTRAINT fk_stock_transfers_responsible_employee FOREIGN KEY (responsible_employee_id) REFERENCES employees (id),
    CONSTRAINT fk_stock_transfers_transfer_order FOREIGN KEY (transfer_order_id) REFERENCES transfer_orders (id),
    CONSTRAINT fk_stock_transfers_source_warehouse_node FOREIGN KEY (source_warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_stock_transfers_target_warehouse_node FOREIGN KEY (target_warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_stock_transfers_source_storage_bin FOREIGN KEY (source_storage_bin_id) REFERENCES storage_bins (id),
    CONSTRAINT fk_stock_transfers_target_storage_bin FOREIGN KEY (target_storage_bin_id) REFERENCES storage_bins (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS stock_transfer_lines (
    id CHAR(36) NOT NULL,
    stock_transfer_id CHAR(36) NOT NULL,
    line_no INT UNSIGNED NOT NULL,
    item_id CHAR(36) NOT NULL,
    characteristic_id CHAR(36) NULL,
    batch_id CHAR(36) NULL,
    unit_of_measure_id CHAR(36) NULL,
    quantity DECIMAL(18, 4) NOT NULL,
    source_warehouse_node_id CHAR(36) NULL,
    source_storage_bin_id CHAR(36) NULL,
    target_warehouse_node_id CHAR(36) NULL,
    target_storage_bin_id CHAR(36) NULL,
    reserved_quantity DECIMAL(18, 4) NOT NULL DEFAULT 0,
    collected_quantity DECIMAL(18, 4) NOT NULL DEFAULT 0,
    CONSTRAINT pk_stock_transfer_lines PRIMARY KEY (id),
    CONSTRAINT uq_stock_transfer_lines_transfer_line UNIQUE (stock_transfer_id, line_no),
    CONSTRAINT fk_stock_transfer_lines_transfer FOREIGN KEY (stock_transfer_id) REFERENCES stock_transfers (id) ON DELETE CASCADE,
    CONSTRAINT fk_stock_transfer_lines_item FOREIGN KEY (item_id) REFERENCES nomenclature_items (id),
    CONSTRAINT fk_stock_transfer_lines_unit_of_measure FOREIGN KEY (unit_of_measure_id) REFERENCES units_of_measure (id),
    CONSTRAINT fk_stock_transfer_lines_source_warehouse_node FOREIGN KEY (source_warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_stock_transfer_lines_source_storage_bin FOREIGN KEY (source_storage_bin_id) REFERENCES storage_bins (id),
    CONSTRAINT fk_stock_transfer_lines_target_warehouse_node FOREIGN KEY (target_warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_stock_transfer_lines_target_storage_bin FOREIGN KEY (target_storage_bin_id) REFERENCES storage_bins (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS inventory_counts (
    id CHAR(36) NOT NULL,
    number VARCHAR(64) NOT NULL,
    document_date DATETIME(6) NOT NULL,
    posting_state SMALLINT UNSIGNED NOT NULL,
    organization_id CHAR(36) NOT NULL,
    author_id CHAR(36) NULL,
    responsible_employee_id CHAR(36) NULL,
    comment_text TEXT NULL,
    base_document_id CHAR(36) NULL,
    project_id CHAR(36) NULL,
    warehouse_node_id CHAR(36) NOT NULL,
    storage_bin_id CHAR(36) NULL,
    finished_on DATE NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_inventory_counts PRIMARY KEY (id),
    CONSTRAINT uq_inventory_counts_number UNIQUE (number),
    CONSTRAINT fk_inventory_counts_organization FOREIGN KEY (organization_id) REFERENCES organizations (id),
    CONSTRAINT fk_inventory_counts_author FOREIGN KEY (author_id) REFERENCES employees (id),
    CONSTRAINT fk_inventory_counts_responsible_employee FOREIGN KEY (responsible_employee_id) REFERENCES employees (id),
    CONSTRAINT fk_inventory_counts_warehouse_node FOREIGN KEY (warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_inventory_counts_storage_bin FOREIGN KEY (storage_bin_id) REFERENCES storage_bins (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS inventory_count_lines (
    id CHAR(36) NOT NULL,
    inventory_count_id CHAR(36) NOT NULL,
    line_no INT UNSIGNED NOT NULL,
    item_id CHAR(36) NOT NULL,
    characteristic_id CHAR(36) NULL,
    batch_id CHAR(36) NULL,
    unit_of_measure_id CHAR(36) NULL,
    book_quantity DECIMAL(18, 4) NOT NULL,
    actual_quantity DECIMAL(18, 4) NOT NULL,
    difference_quantity DECIMAL(18, 4) NOT NULL,
    CONSTRAINT pk_inventory_count_lines PRIMARY KEY (id),
    CONSTRAINT uq_inventory_count_lines_count_line UNIQUE (inventory_count_id, line_no),
    CONSTRAINT fk_inventory_count_lines_count FOREIGN KEY (inventory_count_id) REFERENCES inventory_counts (id) ON DELETE CASCADE,
    CONSTRAINT fk_inventory_count_lines_item FOREIGN KEY (item_id) REFERENCES nomenclature_items (id),
    CONSTRAINT fk_inventory_count_lines_unit_of_measure FOREIGN KEY (unit_of_measure_id) REFERENCES units_of_measure (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS stock_reservations (
    id CHAR(36) NOT NULL,
    number VARCHAR(64) NOT NULL,
    document_date DATETIME(6) NOT NULL,
    posting_state SMALLINT UNSIGNED NOT NULL,
    organization_id CHAR(36) NOT NULL,
    author_id CHAR(36) NULL,
    responsible_employee_id CHAR(36) NULL,
    comment_text TEXT NULL,
    base_document_id CHAR(36) NULL,
    project_id CHAR(36) NULL,
    sales_order_id CHAR(36) NULL,
    source_place SMALLINT UNSIGNED NOT NULL,
    target_place SMALLINT UNSIGNED NOT NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_stock_reservations PRIMARY KEY (id),
    CONSTRAINT uq_stock_reservations_number UNIQUE (number),
    CONSTRAINT fk_stock_reservations_organization FOREIGN KEY (organization_id) REFERENCES organizations (id),
    CONSTRAINT fk_stock_reservations_author FOREIGN KEY (author_id) REFERENCES employees (id),
    CONSTRAINT fk_stock_reservations_responsible_employee FOREIGN KEY (responsible_employee_id) REFERENCES employees (id),
    CONSTRAINT fk_stock_reservations_sales_order FOREIGN KEY (sales_order_id) REFERENCES sales_orders (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS stock_reservation_lines (
    id CHAR(36) NOT NULL,
    stock_reservation_id CHAR(36) NOT NULL,
    line_no INT UNSIGNED NOT NULL,
    item_id CHAR(36) NOT NULL,
    characteristic_id CHAR(36) NULL,
    batch_id CHAR(36) NULL,
    unit_of_measure_id CHAR(36) NULL,
    quantity DECIMAL(18, 4) NOT NULL,
    source_warehouse_node_id CHAR(36) NULL,
    source_storage_bin_id CHAR(36) NULL,
    target_warehouse_node_id CHAR(36) NULL,
    target_storage_bin_id CHAR(36) NULL,
    reserved_quantity DECIMAL(18, 4) NOT NULL DEFAULT 0,
    collected_quantity DECIMAL(18, 4) NOT NULL DEFAULT 0,
    CONSTRAINT pk_stock_reservation_lines PRIMARY KEY (id),
    CONSTRAINT uq_stock_reservation_lines_reservation_line UNIQUE (stock_reservation_id, line_no),
    CONSTRAINT fk_stock_reservation_lines_reservation FOREIGN KEY (stock_reservation_id) REFERENCES stock_reservations (id) ON DELETE CASCADE,
    CONSTRAINT fk_stock_reservation_lines_item FOREIGN KEY (item_id) REFERENCES nomenclature_items (id),
    CONSTRAINT fk_stock_reservation_lines_unit_of_measure FOREIGN KEY (unit_of_measure_id) REFERENCES units_of_measure (id),
    CONSTRAINT fk_stock_reservation_lines_source_warehouse_node FOREIGN KEY (source_warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_stock_reservation_lines_source_storage_bin FOREIGN KEY (source_storage_bin_id) REFERENCES storage_bins (id),
    CONSTRAINT fk_stock_reservation_lines_target_warehouse_node FOREIGN KEY (target_warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_stock_reservation_lines_target_storage_bin FOREIGN KEY (target_storage_bin_id) REFERENCES storage_bins (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS stock_write_offs (
    id CHAR(36) NOT NULL,
    number VARCHAR(64) NOT NULL,
    document_date DATETIME(6) NOT NULL,
    posting_state SMALLINT UNSIGNED NOT NULL,
    organization_id CHAR(36) NOT NULL,
    author_id CHAR(36) NULL,
    responsible_employee_id CHAR(36) NULL,
    comment_text TEXT NULL,
    base_document_id CHAR(36) NULL,
    project_id CHAR(36) NULL,
    currency_code VARCHAR(16) NOT NULL,
    warehouse_node_id CHAR(36) NOT NULL,
    storage_bin_id CHAR(36) NULL,
    inventory_count_id CHAR(36) NULL,
    price_type_id CHAR(36) NULL,
    reason_text VARCHAR(512) NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_stock_write_offs PRIMARY KEY (id),
    CONSTRAINT uq_stock_write_offs_number UNIQUE (number),
    CONSTRAINT fk_stock_write_offs_organization FOREIGN KEY (organization_id) REFERENCES organizations (id),
    CONSTRAINT fk_stock_write_offs_author FOREIGN KEY (author_id) REFERENCES employees (id),
    CONSTRAINT fk_stock_write_offs_responsible_employee FOREIGN KEY (responsible_employee_id) REFERENCES employees (id),
    CONSTRAINT fk_stock_write_offs_warehouse_node FOREIGN KEY (warehouse_node_id) REFERENCES warehouse_nodes (id),
    CONSTRAINT fk_stock_write_offs_storage_bin FOREIGN KEY (storage_bin_id) REFERENCES storage_bins (id),
    CONSTRAINT fk_stock_write_offs_inventory_count FOREIGN KEY (inventory_count_id) REFERENCES inventory_counts (id),
    CONSTRAINT fk_stock_write_offs_price_type FOREIGN KEY (price_type_id) REFERENCES price_types (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS stock_write_off_lines (
    id CHAR(36) NOT NULL,
    stock_write_off_id CHAR(36) NOT NULL,
    line_no INT UNSIGNED NOT NULL,
    item_id CHAR(36) NOT NULL,
    characteristic_id CHAR(36) NULL,
    batch_id CHAR(36) NULL,
    unit_of_measure_id CHAR(36) NULL,
    quantity DECIMAL(18, 4) NOT NULL,
    price DECIMAL(18, 4) NOT NULL,
    discount_percent DECIMAL(9, 4) NOT NULL DEFAULT 0,
    discount_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    amount DECIMAL(18, 4) NOT NULL,
    vat_rate_code VARCHAR(32) NULL,
    tax_amount DECIMAL(18, 4) NOT NULL DEFAULT 0,
    total DECIMAL(18, 4) NOT NULL,
    content_text VARCHAR(512) NULL,
    CONSTRAINT pk_stock_write_off_lines PRIMARY KEY (id),
    CONSTRAINT uq_stock_write_off_lines_write_off_line UNIQUE (stock_write_off_id, line_no),
    CONSTRAINT fk_stock_write_off_lines_write_off FOREIGN KEY (stock_write_off_id) REFERENCES stock_write_offs (id) ON DELETE CASCADE,
    CONSTRAINT fk_stock_write_off_lines_item FOREIGN KEY (item_id) REFERENCES nomenclature_items (id),
    CONSTRAINT fk_stock_write_off_lines_unit_of_measure FOREIGN KEY (unit_of_measure_id) REFERENCES units_of_measure (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS price_registration_documents (
    id CHAR(36) NOT NULL,
    number VARCHAR(64) NOT NULL,
    document_date DATETIME(6) NOT NULL,
    posting_state SMALLINT UNSIGNED NOT NULL,
    organization_id CHAR(36) NOT NULL,
    author_id CHAR(36) NULL,
    responsible_employee_id CHAR(36) NULL,
    comment_text TEXT NULL,
    base_document_id CHAR(36) NULL,
    project_id CHAR(36) NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_price_registration_documents PRIMARY KEY (id),
    CONSTRAINT uq_price_registration_documents_number UNIQUE (number),
    CONSTRAINT fk_price_registration_documents_organization FOREIGN KEY (organization_id) REFERENCES organizations (id),
    CONSTRAINT fk_price_registration_documents_author FOREIGN KEY (author_id) REFERENCES employees (id),
    CONSTRAINT fk_price_registration_documents_responsible_employee FOREIGN KEY (responsible_employee_id) REFERENCES employees (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS price_registration_lines (
    id CHAR(36) NOT NULL,
    price_registration_document_id CHAR(36) NOT NULL,
    line_no INT UNSIGNED NOT NULL,
    item_id CHAR(36) NOT NULL,
    characteristic_id CHAR(36) NULL,
    unit_of_measure_id CHAR(36) NULL,
    price_type_id CHAR(36) NOT NULL,
    new_price DECIMAL(18, 4) NOT NULL,
    previous_price DECIMAL(18, 4) NULL,
    currency_code VARCHAR(16) NOT NULL,
    CONSTRAINT pk_price_registration_lines PRIMARY KEY (id),
    CONSTRAINT uq_price_registration_lines_document_line UNIQUE (price_registration_document_id, line_no),
    CONSTRAINT fk_price_registration_lines_document FOREIGN KEY (price_registration_document_id) REFERENCES price_registration_documents (id) ON DELETE CASCADE,
    CONSTRAINT fk_price_registration_lines_item FOREIGN KEY (item_id) REFERENCES nomenclature_items (id),
    CONSTRAINT fk_price_registration_lines_unit_of_measure FOREIGN KEY (unit_of_measure_id) REFERENCES units_of_measure (id),
    CONSTRAINT fk_price_registration_lines_price_type FOREIGN KEY (price_type_id) REFERENCES price_types (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS onec_import_batches (
    id BIGINT NOT NULL AUTO_INCREMENT,
    started_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    finished_at_utc DATETIME(6) NULL,
    source_folders_json JSON NOT NULL,
    note_text VARCHAR(512) NULL,
    created_by VARCHAR(128) NULL,
    CONSTRAINT pk_onec_import_batches PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS onec_schema_definitions (
    id BIGINT NOT NULL AUTO_INCREMENT,
    batch_id BIGINT NULL,
    schema_kind VARCHAR(32) NOT NULL,
    object_name VARCHAR(160) NOT NULL,
    source_file_name VARCHAR(260) NOT NULL,
    imported_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_onec_schema_definitions PRIMARY KEY (id),
    CONSTRAINT uq_onec_schema_definitions_object UNIQUE (object_name, source_file_name),
    CONSTRAINT fk_onec_schema_definitions_batch FOREIGN KEY (batch_id) REFERENCES onec_import_batches (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS onec_schema_columns (
    id BIGINT NOT NULL AUTO_INCREMENT,
    schema_definition_id BIGINT NOT NULL,
    ordinal_position INT UNSIGNED NOT NULL,
    column_name VARCHAR(160) NOT NULL,
    CONSTRAINT pk_onec_schema_columns PRIMARY KEY (id),
    CONSTRAINT uq_onec_schema_columns_position UNIQUE (schema_definition_id, ordinal_position),
    CONSTRAINT fk_onec_schema_columns_definition FOREIGN KEY (schema_definition_id) REFERENCES onec_schema_definitions (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS onec_schema_tabular_sections (
    id BIGINT NOT NULL AUTO_INCREMENT,
    schema_definition_id BIGINT NOT NULL,
    section_name VARCHAR(160) NOT NULL,
    CONSTRAINT pk_onec_schema_tabular_sections PRIMARY KEY (id),
    CONSTRAINT uq_onec_schema_tabular_sections_name UNIQUE (schema_definition_id, section_name),
    CONSTRAINT fk_onec_schema_tabular_sections_definition FOREIGN KEY (schema_definition_id) REFERENCES onec_schema_definitions (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS onec_schema_tabular_section_columns (
    id BIGINT NOT NULL AUTO_INCREMENT,
    schema_tabular_section_id BIGINT NOT NULL,
    ordinal_position INT UNSIGNED NOT NULL,
    column_name VARCHAR(160) NOT NULL,
    CONSTRAINT pk_onec_schema_tabular_section_columns PRIMARY KEY (id),
    CONSTRAINT uq_onec_schema_tabular_section_columns_position UNIQUE (schema_tabular_section_id, ordinal_position),
    CONSTRAINT fk_onec_schema_tabular_section_columns_section FOREIGN KEY (schema_tabular_section_id) REFERENCES onec_schema_tabular_sections (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS onec_object_snapshots (
    id BIGINT NOT NULL AUTO_INCREMENT,
    batch_id BIGINT NOT NULL,
    object_name VARCHAR(160) NOT NULL,
    reference_code VARCHAR(160) NOT NULL,
    code_value VARCHAR(160) NULL,
    number_value VARCHAR(160) NULL,
    title_text VARCHAR(512) NULL,
    subtitle_text VARCHAR(512) NULL,
    status_text VARCHAR(160) NULL,
    record_date DATETIME(6) NULL,
    source_folder VARCHAR(512) NULL,
    record_hash CHAR(64) NULL,
    payload_json JSON NULL,
    imported_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_onec_object_snapshots PRIMARY KEY (id),
    CONSTRAINT uq_onec_object_snapshots_reference UNIQUE (batch_id, object_name, reference_code),
    CONSTRAINT fk_onec_object_snapshots_batch FOREIGN KEY (batch_id) REFERENCES onec_import_batches (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS onec_field_snapshots (
    id BIGINT NOT NULL AUTO_INCREMENT,
    object_snapshot_id BIGINT NOT NULL,
    field_name VARCHAR(160) NOT NULL,
    raw_value LONGTEXT NULL,
    display_value LONGTEXT NULL,
    CONSTRAINT pk_onec_field_snapshots PRIMARY KEY (id),
    CONSTRAINT uq_onec_field_snapshots_field UNIQUE (object_snapshot_id, field_name),
    CONSTRAINT fk_onec_field_snapshots_object FOREIGN KEY (object_snapshot_id) REFERENCES onec_object_snapshots (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS onec_tabular_section_snapshots (
    id BIGINT NOT NULL AUTO_INCREMENT,
    object_snapshot_id BIGINT NOT NULL,
    section_name VARCHAR(160) NOT NULL,
    CONSTRAINT pk_onec_tabular_section_snapshots PRIMARY KEY (id),
    CONSTRAINT uq_onec_tabular_section_snapshots_name UNIQUE (object_snapshot_id, section_name),
    CONSTRAINT fk_onec_tabular_section_snapshots_object FOREIGN KEY (object_snapshot_id) REFERENCES onec_object_snapshots (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS onec_tabular_section_rows (
    id BIGINT NOT NULL AUTO_INCREMENT,
    tabular_section_snapshot_id BIGINT NOT NULL,
    `row_number` INT UNSIGNED NOT NULL,
    payload_json JSON NULL,
    CONSTRAINT pk_onec_tabular_section_rows PRIMARY KEY (id),
    CONSTRAINT uq_onec_tabular_section_rows_row UNIQUE (tabular_section_snapshot_id, `row_number`),
    CONSTRAINT fk_onec_tabular_section_rows_section FOREIGN KEY (tabular_section_snapshot_id) REFERENCES onec_tabular_section_snapshots (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS onec_tabular_section_fields (
    id BIGINT NOT NULL AUTO_INCREMENT,
    tabular_section_row_id BIGINT NOT NULL,
    field_name VARCHAR(160) NOT NULL,
    raw_value LONGTEXT NULL,
    display_value LONGTEXT NULL,
    CONSTRAINT pk_onec_tabular_section_fields PRIMARY KEY (id),
    CONSTRAINT uq_onec_tabular_section_fields_field UNIQUE (tabular_section_row_id, field_name),
    CONSTRAINT fk_onec_tabular_section_fields_row FOREIGN KEY (tabular_section_row_id) REFERENCES onec_tabular_section_rows (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS onec_reference_links (
    id BIGINT NOT NULL AUTO_INCREMENT,
    object_snapshot_id BIGINT NOT NULL,
    section_name VARCHAR(160) NULL,
    `row_number` INT UNSIGNED NULL,
    field_name VARCHAR(160) NOT NULL,
    target_object_name VARCHAR(160) NULL,
    target_reference_code VARCHAR(160) NULL,
    target_display_value VARCHAR(512) NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_onec_reference_links PRIMARY KEY (id),
    CONSTRAINT fk_onec_reference_links_object FOREIGN KEY (object_snapshot_id) REFERENCES onec_object_snapshots (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS app_users (
    id CHAR(36) NOT NULL,
    user_name VARCHAR(128) NOT NULL,
    display_name VARCHAR(256) NOT NULL,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    last_seen_at_utc DATETIME(6) NULL,
    CONSTRAINT pk_app_users PRIMARY KEY (id),
    CONSTRAINT uq_app_users_user_name UNIQUE (user_name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS app_roles (
    id CHAR(36) NOT NULL,
    role_code VARCHAR(64) NOT NULL,
    display_name VARCHAR(128) NOT NULL,
    description_text VARCHAR(512) NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_app_roles PRIMARY KEY (id),
    CONSTRAINT uq_app_roles_role_code UNIQUE (role_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS app_user_roles (
    user_id CHAR(36) NOT NULL,
    role_id CHAR(36) NOT NULL,
    assigned_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    assigned_by VARCHAR(128) NULL,
    CONSTRAINT pk_app_user_roles PRIMARY KEY (user_id, role_id),
    CONSTRAINT fk_app_user_roles_user FOREIGN KEY (user_id) REFERENCES app_users (id) ON DELETE CASCADE,
    CONSTRAINT fk_app_user_roles_role FOREIGN KEY (role_id) REFERENCES app_roles (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS app_module_snapshots (
    module_code VARCHAR(64) NOT NULL,
    payload_json JSON NOT NULL,
    payload_hash CHAR(64) NOT NULL,
    version_no INT UNSIGNED NOT NULL DEFAULT 1,
    updated_by VARCHAR(128) NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_app_module_snapshots PRIMARY KEY (module_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS app_audit_events (
    id CHAR(36) NOT NULL,
    module_code VARCHAR(64) NOT NULL,
    module_caption VARCHAR(128) NOT NULL,
    logged_at_utc DATETIME(6) NOT NULL,
    actor_user_name VARCHAR(128) NOT NULL,
    entity_type VARCHAR(128) NOT NULL,
    entity_id CHAR(36) NOT NULL,
    entity_number VARCHAR(128) NULL,
    action_text VARCHAR(256) NOT NULL,
    result_text VARCHAR(128) NOT NULL,
    message_text TEXT NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_app_audit_events PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS app_document_attachments (
    id CHAR(36) NOT NULL,
    module_code VARCHAR(64) NOT NULL,
    entity_type VARCHAR(128) NOT NULL,
    entity_id CHAR(36) NOT NULL,
    entity_number VARCHAR(128) NULL,
    original_file_name VARCHAR(260) NOT NULL,
    storage_path VARCHAR(1024) NOT NULL,
    content_type VARCHAR(128) NULL,
    content_length BIGINT NOT NULL DEFAULT 0,
    checksum_sha256 CHAR(64) NULL,
    created_by VARCHAR(128) NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    is_deleted TINYINT(1) NOT NULL DEFAULT 0,
    CONSTRAINT pk_app_document_attachments PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS app_saved_exports (
    id CHAR(36) NOT NULL,
    module_code VARCHAR(64) NOT NULL,
    export_kind VARCHAR(64) NOT NULL,
    file_name VARCHAR(260) NOT NULL,
    storage_path VARCHAR(1024) NOT NULL,
    created_by VARCHAR(128) NULL,
    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    CONSTRAINT pk_app_saved_exports PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE INDEX ix_business_partners_roles ON business_partners (roles);
CREATE INDEX ix_business_partners_name ON business_partners (name);
CREATE INDEX ix_nomenclature_items_name ON nomenclature_items (name);
CREATE INDEX ix_stock_balances_item_warehouse ON stock_balances (item_id, warehouse_node_id);
CREATE INDEX ix_sales_orders_customer_date ON sales_orders (customer_id, document_date);
CREATE INDEX ix_sales_invoices_customer_date ON sales_invoices (customer_id, document_date);
CREATE INDEX ix_sales_shipments_customer_date ON sales_shipments (customer_id, document_date);
CREATE INDEX ix_purchase_orders_supplier_date ON purchase_orders (supplier_id, document_date);
CREATE INDEX ix_supplier_invoices_supplier_date ON supplier_invoices (supplier_id, document_date);
CREATE INDEX ix_purchase_receipts_supplier_date ON purchase_receipts (supplier_id, document_date);
CREATE INDEX ix_transfer_orders_date ON transfer_orders (requested_transfer_date);
CREATE INDEX ix_inventory_counts_warehouse_date ON inventory_counts (warehouse_node_id, document_date);
CREATE INDEX ix_stock_reservations_sales_order ON stock_reservations (sales_order_id);
CREATE INDEX ix_onec_object_snapshots_object_date ON onec_object_snapshots (object_name, record_date);
CREATE INDEX ix_onec_field_snapshots_field_name ON onec_field_snapshots (field_name);
CREATE INDEX ix_onec_reference_links_target ON onec_reference_links (target_object_name, target_reference_code);
CREATE INDEX ix_app_users_display_name ON app_users (display_name);
CREATE INDEX ix_app_module_snapshots_updated_at ON app_module_snapshots (updated_at_utc);
CREATE INDEX ix_app_audit_events_module_logged_at ON app_audit_events (module_code, logged_at_utc);
CREATE INDEX ix_app_audit_events_actor_logged_at ON app_audit_events (actor_user_name, logged_at_utc);
CREATE INDEX ix_app_document_attachments_entity ON app_document_attachments (module_code, entity_type, entity_id);
CREATE INDEX ix_app_saved_exports_module_created_at ON app_saved_exports (module_code, created_at_utc);

