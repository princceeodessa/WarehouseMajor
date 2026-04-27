# Workspace Overview

Main working folders in this workspace:

- `StartDesktop.cmd`
  Main desktop launch file. Double-click it.
- `WarehouseAutomatisaion.sln`
  Solution for desktop + core libraries only.
- `WarehouseAutomatisaion.Application/`
  Application layer.
- `WarehouseAutomatisaion.Domain/`
  Domain model.
- `WarehouseAutomatisaion.Infrastructure/`
  Import, projection and persistence.
- `WarehouseAutomatisaion.Desktop/`
  Shared desktop services used by the WPF client: data access, printing, model metadata and text helpers. This project is no longer a WinForms executable.
- `WarehouseAutomatisaion.Desktop.Wpf/`
  Main WPF client application. The executable name is `MajorWarehause.exe`.
- `docs/`
  Project notes and migration docs.
- `scripts/`
  Helper scripts for MySQL, 1C import and MajorWarehause installer builds.
- `model_schema/`
  Extracted 1C object schemas.
- `exports_sales_docs_20260323/`
  Current sales order export from 1C.
- `exports_sales_invoices_20260323/`
  Current sales invoice export from 1C.
- `app_data/`
  Generated runtime artifacts, SQL scripts and desktop state.
- `_archive/`
  Old experiments, probe exports, temporary files, logs and legacy 1C artifacts.

Archived maintenance launcher:

- `_archive\\2026-03-24\\desktop-only\\WarehouseAutomatisaion`
  Old non-desktop launcher and maintenance CLI moved out of the main workspace.

Operational flow right now:

1. Raw import reads current `exports_*` folders.
2. Projection writes normalized data into MySQL.
3. Desktop client reads operational MySQL.

How to run the desktop:

1. Double-click `StartDesktop.cmd`
2. Or run `WarehouseAutomatisaion.Desktop.Wpf\\bin\\Debug\\net8.0-windows\\MajorWarehause.exe`

How to build a release:

1. Run `powershell -ExecutionPolicy Bypass -File .\\scripts\\build-majorwarehause-setup.ps1 -Version 1.0.0`
2. Send `artifacts\\installers\\MajorWarehauseSetup.exe` to the user for first install.
3. Publish `artifacts\\publish\\majorwarehause-win-x64.zip` in GitHub Releases for in-app updates.
