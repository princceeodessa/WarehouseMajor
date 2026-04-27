# Развертывание клиента MajorWarehause

## Цель
Один центральный MySQL на VPS и несколько WPF-клиентов MajorWarehause, которые работают с общей базой. Пользователю для первой установки отдается один файл `MajorWarehauseSetup.exe`, а последующие обновления ставятся кнопкой `Обновить` внутри приложения.

## 1. Что должно быть на VPS
- Linux VPS с SSH-доступом.
- MySQL 8+ или MariaDB с совместимой схемой.
- Открытый порт MySQL только для нужных IP или через защищенный контур.
- Отдельная база, например `warehouse_automation`.
- Отдельный пользователь приложения с правами на эту базу.

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

Скрипт применяет файл `WarehouseAutomatisaion.Infrastructure/Persistence/Sql/mysql-operational-schema.sql`.

## 3. Настройка клиента
Рядом с установленным `MajorWarehause.exe` должен лежать файл `appsettings.local.json`.

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
- Если `RemoteDatabase.Enabled = true`, клиент работает только в серверном режиме.
- Тихий откат в локальный JSON отключен.
- При недоступности сервера клиент не стартует.
- Внешний `mysql.exe` клиенту больше не нужен.
- Установщик и обновление сохраняют `appsettings.local.json` и папку `app_data`.

## 4. Сборка одного установщика
Локально установщик собирается командой:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-majorwarehause-setup.ps1 -Version 1.0.0
```

Скрипт сначала собирает WPF-клиент, затем создает артефакты:
- `artifacts/installers/MajorWarehauseSetup.exe` - один файл для первой установки пользователю.
- `artifacts/publish/majorwarehause-win-x64.zip` - архив, который приложение скачивает при обновлении.

Установщик ставит приложение в `%LOCALAPPDATA%\Programs\MajorWarehause`, создает ярлыки на рабочем столе и в меню Пуск, затем запускает приложение.

## 5. GitHub Releases
Основной release-flow:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

После пуша тега workflow `.github/workflows/release-majorwarehause.yml` собирает и прикрепляет к Release два файла:
- `MajorWarehauseSetup.exe` - для первой установки или ручной переустановки.
- `majorwarehause-win-x64.zip` - для кнопки `Обновить` внутри приложения.

Можно также запустить workflow вручную через GitHub Actions и указать версию в параметре `version`.

## 6. Что отправлять пользователю
Для первой установки отправляется только `MajorWarehauseSetup.exe`.

Для следующих версий вручную ничего отправлять не нужно: пользователь открывает MajorWarehause и нажимает `Обновить`. Клиент проверяет последний GitHub Release, скачивает `majorwarehause-win-x64.zip`, заменяет файлы приложения и перезапускается.

Практические правила:
- Лучше использовать публичный Release-канал без приватных токенов внутри клиента.
- Если репозиторий приватный, нужен отдельный безопасный сервер обновлений, а не GitHub token в приложении.
- Приложение должно лежать в папке, куда у пользователя есть права на запись; текущая схема использует `%LOCALAPPDATA%`.

## 7. Следующий этап данных
После поднятия VPS-базы туда нужно загрузить данные из 1С:
- Товары.
- Штрихкоды.
- Цены.
- Остатки.
- Контрагенты.
- Продажи.
- Закупки.

Только после этого клиенты начнут видеть рабочие данные из общего контура.
