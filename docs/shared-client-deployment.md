# Развертывание клиента Major

## Цель
Один центральный MySQL на VPS и несколько WPF-клиентов Major, которые работают с общей базой. Пользователю для первой установки отдается один файл `MajorSetup.exe`, а последующие обновления ставятся кнопкой `Обновить` внутри приложения.

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
Рядом с установленным `Major.exe` должен лежать файл `appsettings.local.json`.

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
    "GitHubRepository": "Major",
    "AssetName": "major-win-x64.zip"
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
powershell -ExecutionPolicy Bypass -File .\scripts\build-major-setup.ps1 -Version 1.0.0
```

Скрипт сначала собирает WPF-клиент, затем создает артефакты:
- `artifacts/installers/MajorSetup.exe` - один файл для первой установки пользователю.
- `artifacts/publish/major-win-x64.zip` - архив, который приложение скачивает при обновлении.

Установщик ставит приложение в `%LOCALAPPDATA%\Programs\Major`, создает ярлыки на рабочем столе и в меню Пуск, затем запускает приложение.

## 5. Подпись установщика и приложения
Подпись встроена в release-скрипты:
- `Major.exe` подписывается до упаковки в `major-win-x64.zip`.
- `MajorSetup.exe` подписывается после сборки установщика.

Для рабочей раздачи нужен настоящий OV/EV code-signing сертификат от доверенного центра сертификации. Self-signed сертификат подходит только для локального теста или закрытого контура, где сертификат заранее добавлен в доверенные.

Локальный тестовый сертификат:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\create-local-codesign-certificate.ps1 -TrustForCurrentUser
```

Сборка с сертификатом из хранилища Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-major-setup.ps1 `
  -Version 1.0.0 `
  -CodeSigningCertificateThumbprint <thumbprint>
```

Сборка с PFX-файлом:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-major-setup.ps1 `
  -Version 1.0.0 `
  -CodeSigningCertificatePath C:\certs\major-codesign.pfx `
  -CodeSigningCertificatePassword <pfx-password>
```

Проверка подписи:

```powershell
Get-AuthenticodeSignature .\artifacts\installers\MajorSetup.exe
Get-AuthenticodeSignature .\artifacts\publish\major-win-x64\Major.exe
```

## 6. GitHub Releases
Основной release-flow:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

После пуша тега workflow `.github/workflows/release-major.yml` собирает и прикрепляет к Release два файла:
- `MajorSetup.exe` - для первой установки или ручной переустановки.
- `major-win-x64.zip` - для кнопки `Обновить` внутри приложения.

Для подписи в GitHub Actions добавляются secrets:
- `MAJORWAREHAUSE_CODESIGN_PFX_BASE64` - PFX-файл, закодированный в base64.
- `MAJORWAREHAUSE_CODESIGN_PFX_PASSWORD` - пароль от PFX.

Base64 для PFX можно получить локально:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\certs\major-codesign.pfx")) | Set-Clipboard
```

Опционально можно задать repository variable `MAJORWAREHAUSE_CODESIGN_TIMESTAMP_SERVER`. Если переменная не задана, используется `http://timestamp.digicert.com`.

## 7. Что отправлять пользователю
Для первой установки отправляется только `MajorSetup.exe`.

Для следующих версий вручную ничего отправлять не нужно: пользователь открывает Major и нажимает `Обновить`. Клиент проверяет последний GitHub Release, скачивает `major-win-x64.zip`, заменяет файлы приложения и перезапускается.

Практические правила:
- Лучше использовать публичный Release-канал без приватных токенов внутри клиента.
- Если репозиторий приватный, нужен отдельный безопасный сервер обновлений, а не GitHub token в приложении.
- Приложение должно лежать в папке, куда у пользователя есть права на запись; текущая схема использует `%LOCALAPPDATA%`.
- Нормальная подпись уменьшает предупреждения Windows, но SmartScreen репутация набирается не мгновенно.

## 8. Следующий этап данных
После поднятия VPS-базы туда нужно загрузить данные из 1С:
- Товары.
- Штрихкоды.
- Цены.
- Остатки.
- Контрагенты.
- Продажи.
- Закупки.

Только после этого клиенты начнут видеть рабочие данные из общего контура.
