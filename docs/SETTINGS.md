# Настройки RestartVPN

> В репозитории нет готового файла настроек: приложение создаёт `settings.json`
> при первом запуске. Здесь описан формат и значения по умолчанию.

Файл по умолчанию: `%APPDATA%\RestartVPN\settings.json`.
Портативный режим: `<папка exe>\data\settings.json`.
Альтернативный файл: `RestartVPN.exe --config <путь>`.

Имена полей регистронезависимы; приложение записывает их в PascalCase.
Неизвестные поля игнорируются (совместимость вперёд). Комментарии в JSON
не допускаются. Ошибки показываются с путём, например `targets[0].url`.

## Минимальный пример

~~~json
{
  "Version": 1,
  "Mode": "MonitorOnly",
  "Vpn": {
    "Backend": "openvpn-gui",
    "ProfileName": "my-profile"
  },
  "Targets": [
    { "Name": "example", "Url": "https://example.com/" }
  ],
  "Policy": {
    "Quorum": "All",
    "FailureThreshold": 3
  }
}
~~~

## Корень

| Поле | Тип | По умолчанию | Смысл |
|---|---|---|---|
| Version | int | 1 | версия формата |
| Mode | string | MonitorOnly | `MonitorOnly` или `AutoRestart` |
| Vpn | object | — | см. ниже |
| Targets | array | [] | сайты для проверки |
| Policy | object | — | политика решений |
| Probe | object | — | параметры HTTP |
| Logging | object | — | журнал |
| Notifications | object | — | уведомления |
| Ui | object | — | поведение окна |

## Vpn

| Поле | По умолчанию | Смысл |
|---|---|---|
| Backend | openvpn-gui | только этот бэкенд в 0.1 |
| ProfileName | "" | имя профиля = имя .ovpn без расширения |
| ConfigPath | "" | необязательный полный путь к .ovpn |
| OpenVpnGuiPath | C:\Program Files\OpenVPN\bin\openvpn-gui.exe | путь к GUI |
| ConnectTimeoutSec | 20 | ожидание появления процесса после connect |
| CommandTimeoutSec | 10 | таймаут команды GUI |
| ConnectOnStart | true | в AutoRestart подключать профиль при старте |
| ReadOpenVpnLog | true | читать %USERPROFILE%\OpenVPN\log\<profile>.log |
| LogDirectory | "" | переопределить каталог журнала OpenVPN |

Профиль ищется в `%USERPROFILE%\OpenVPN\config` (плоско и в подпапке
`<name>\<name>.ovpn`), затем в `%ProgramFiles%\OpenVPN\config`.
Файлы .ovpn приложение только читает.

## Targets[]

| Поле | По умолчанию | Смысл |
|---|---|---|
| Name | "" | отображаемое имя (иначе — хост) |
| Url | — | абсолютный http(s)-адрес, обязателен |
| Enabled | true | выключенные цели не опрашиваются |
| Method | GET | GET или HEAD |
| TimeoutMs | 6000 | таймаут одной попытки, 500–120000 |
| RetryTimeoutMs | 4000 | таймаут повторной попытки |
| ExpectedStatus | null | массив кодов; null = 1xx–4xx успех, 5xx по `Treat5xxAsFailure` |
| Treat5xxAsFailure | false | считать 5xx недоступностью для этой цели |
| BodyPattern | "" | regex по первым `MaxBodyBytes` байтам тела |
| Headers | {} | дополнительные HTTP-заголовки |

Важно: 5xx по умолчанию считается недоступностью **сервиса**, а не VPN.
Решение о перезапуске принимает политика, а не код ответа.

## Policy

| Поле | По умолчанию | Смысл |
|---|---|---|
| Quorum | All | All / Majority / Any / Count:N |
| FailureThreshold | 3 | неудач подряд до «недоступен» |
| CorrelationWindowSec | 30 | окно свежести последней неудачи |
| ProbeIntervalSec | 5 | период цикла опроса |
| RetryDelayMs | 1000 | пауза перед повторной попыткой |
| RecoveryTimeoutSec | 60 | ожидание восстановления после действия |
| RecoverySuccesses | 1 | сколько успехов подряд считать восстановлением |
| MinRestartGapSec | 30 | минимальная пауза между перезапусками |
| MaxRestartGapSec | 300 | максимальная пауза |
| MaxRestartsPerHour | 6 | при превышении пауза = MaxRestartGapSec |
| FailedRestartsBeforeCircuit | 3 | неудач до предохранителя |
| CircuitCooldownSec | 900 | остывание предохранителя |
| JitterPercent | 20 | случайный разброс паузы, 0–50 |
| ConnectCooldownSec | 30 | пауза между командами connect |
| RequireTunnelEvidence | false | true = перезапускать только при отказе туннеля |
| RestartOnServiceOutage | false | true = перезапускать, даже если туннель «подключён» |
| HardRestartAfterFailedSoft | true | после неудачного мягкого — жёсткий |
| AlertThrottleSec | 300 | как часто повторять предупреждение |

### Как выбирается действие

1. Если ни одна цель не «недоступна» — ничего.
2. Если недоступных меньше кворума — предупреждение (одиночный отказ не рвёт VPN).
3. В `MonitorOnly` — всегда только предупреждение.
4. Фатальная ошибка OpenVPN — предупреждение (перезапуск не поможет).
5. Профиль не запущен — `Connect` без убийства процессов.
6. Туннель упал/переподключается — мягкий перезапуск.
7. Туннель «подключён», цели недоступны — перезапуск (по умолчанию) либо
   предупреждение при `RequireTunnelEvidence: true`.
8. Предохранитель/backoff — ожидание с записью причины в журнал.

## Probe

| Поле | По умолчанию | Смысл |
|---|---|---|
| UseSystemProxy | false | использовать системный прокси |
| FollowRedirects | true | следовать 3xx |
| MaxRedirects | 5 | предел переходов |
| Tls12 | true | включить TLS 1.2 (нужно для Win7) |
| UserAgent | RestartVPN/0.1 … | User-Agent |
| Treat5xxAsFailure | true | глобальное правило для 5xx |
| MaxBodyBytes | 65536 | предел чтения тела для BodyPattern |

## Logging

| Поле | По умолчанию | Смысл |
|---|---|---|
| Level | Info | Error / Warn / Info / Debug |
| MaxFileBytes | 2097152 | ротация при превышении, 64 КБ–100 МБ |
| KeepFiles | 5 | сколько ротаций хранить |
| Jsonl | true | писать `restartvpn-<profile>.events.jsonl` |
| Directory | "" | переопределить каталог журнала |

## Notifications

| Поле | По умолчанию | Смысл |
|---|---|---|
| Balloon | true | всплывающие уведомления в трее |
| Sound | false | звук при событии |
| OnlyOnAction | true | не уведомлять о каждой проверке |

## Ui

| Поле | По умолчанию | Смысл |
|---|---|---|
| StartMinimized | true | стартовать в трее |
| CloseToTray | true | закрытие окна = свернуть |
| Autostart | false | автозапуск (per-user, HKCU Run) |
| ConfirmRestart | true | спрашивать перед ручным перезапуском |
| MinimizeToTrayHintShown | false | служебный флаг подсказки |

## Приоритет и хранение

1. Значения по умолчанию (класс `AppSettings`).
2. Файл JSON (частичный файл допустим — отсутствующие поля берутся по умолчанию).
3. Валидация: ошибки блокируют запуск мониторинга, предупреждения — нет.

Сохранение атомарное: `settings.json.tmp` → `File.Replace`, предыдущая версия
остаётся в `settings.json.bak`. Кодировка UTF-8 с BOM (для Win7 Notepad).