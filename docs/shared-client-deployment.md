# Shared client deployment

## Цель
Один центральный MySQL на VPS и несколько WPF-клиентов, которые работают с одной общей базой без локального fallback.

## 1. Что должно быть на VPS
- Linux VPS с SSH-доступом
- MySQL 8+ или MariaDB с совместимой схемой
- открытый порт MySQL только для нужных IP или через защищенный контур
- отдельная база, например `warehouse_automation`
- отдельный пользователь приложения с правами на эту базу

## 2. Развертывание схемы на сервере
Из рабочей машины с установленным `mysql.exe` можно применить схему так:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\apply-mysql-operational-schema.ps1 `
  -Host <db-host> `
  -Port 3306 `
  -DatabaseName warehouse_automation `
  -User warehouse_app `
  -Password <db-password>
```

Скрипт применяет файл:
- `WarehouseAutomatisaion.Infrastructure/Persistence/Sql/mysql-operational-schema.sql`

## 3. Настройка клиента
Рядом с `MajorWarehause.exe` должен лежать файл `appsettings.local.json`.

Пример:

```json
{
  "RemoteDatabase": {
    "Enabled": true,
    "Host": "db.example.com",
    "Port": 3306,
    "Database": "warehouse_automation",
    "User": "warehouse_app",
    "Password": "change-me",
    "MysqlExecutablePath": ""
  },
  "ApplicationUpdate": {
    "Enabled": true,
    "GitHubOwner": "your-github-org-or-user",
    "GitHubRepository": "MajorWarehause",
    "AssetName": "majorwarehause-win-x64.zip"
  }
}
```

Важно:
- если `Enabled = true`, клиент работает только в серверном режиме
- тихий откат в локальный JSON отключен
- при недоступности сервера клиент не стартует
- внешний `mysql.exe` клиенту больше не нужен

## 4. Публикация клиента
```powershell
powershell -ExecutionPolicy Bypass -File .\WarehouseAutomatisaion.Desktop.Wpf\publish-win-x64.ps1 -Version 1.0.0
```

Артефакты будут в:
- `artifacts/publish/majorwarehause-win-x64`
- `artifacts/publish/majorwarehause-win-x64.zip`

## 5. Что отправлять пользователю
Пользователю отправляется вся папка publish или zip-архив, а не только один exe-файл.

Минимальный состав:
- `MajorWarehause.exe`
- `appsettings.json`
- `appsettings.local.json`
- `README_DEPLOY.md`

## 6. GitHub Releases и кнопка "Обновить"
В клиент встроена кнопка ручного обновления. Она проверяет последний GitHub Release и скачивает архив `majorwarehause-win-x64.zip`.

Что нужно настроить один раз:

1. Создать GitHub-репозиторий и запушить туда этот проект.
2. Убедиться, что в репозитории есть workflow `.github/workflows/release-majorwarehause.yml`.
3. Заполнять `ApplicationUpdate` в `appsettings.local.json` на машине клиента.
4. Выпускать новую версию через git tag `vX.Y.Z`.
5. После пуша тега GitHub Actions соберет архив и прикрепит его к Release.
6. Пользователь нажимает `Обновить` в приложении и получает новую версию без ручной пересылки файлов.

Важно:
- лучше использовать публичный Release-канал без приватных токенов внутри клиента
- обновление сохраняет `appsettings.local.json` и папку `app_data`
- приложение должно лежать в папке, куда у пользователя есть права на запись

## 7. Следующий этап
После поднятия VPS-базы туда нужно загрузить данные из 1С:
- товары
- штрихкоды
- цены
- остатки
- контрагенты
- продажи
- закупки

Только после этого клиенты начнут видеть рабочие данные из общего контура.
