# API первого этапа

JSON использует camelCase, `SourceKind`: 0 — цвет, 1 — локальное видео, 2 — OBS, 3 — прямой S3. Ошибки: `{ "error": "описание" }`. Все `/api`, кроме `/api/login`, требуют cookie оператора. Все изменяющие запросы `/api` требуют Origin, совпадающий с `PUBLIC_ORIGIN`. Идентификаторы каналов/источников — UUID.

| Метод | Путь | Назначение |
|---|---|---|
| POST | `/api/login` | `{password}` |
| POST | `/api/logout` | Выход |
| GET | `/api/session` | Проверка сеанса |
| POST | `/api/password` | `{currentPassword,newPassword}`, аннулирование сеансов |
| GET/POST | `/api/channels` | Список / `{name,slug}` |
| GET | `/api/channels/{id}` | Канал, источники, плейлисты, расписание, события |
| POST | `/api/channels/{id}/sources` | `{name,kind:0,locator:"#143536"}` |
| POST | `/api/channels/{id}/media` | Multipart-поле `file`, локальная загрузка |
| POST | `/api/channels/{id}/preview` | `{sourceId,revision}` |
| POST | `/api/channels/{id}/take` | `{transition:"CUT" или "MIX",frames,revision}` |
| POST | `/api/channels/{id}/autopilot` | `{enabled,fallbackId,revision}` |
| POST | `/api/channels/{id}/playlists` | `{name,items:[{sourceId,durationSeconds}]}` |
| POST | `/api/channels/{id}/schedule` | `{title,sourceId?,playlistId?,startsAt,endsAt}` |
| DELETE | `/api/channels/{id}/schedule/{blockId}` | Удалить блок |
| PUT | `/api/channels/{id}/graphics` | `{scene:{elements:[...]},revision}` |
| POST | `/api/channels/{id}/graphics` | `{enabled,revision}`, выдача/снятие |
| GET/PUT | `/api/channels/{id}/restriction` | Статус / `{enabled,rules:["RU"],blockUnknown,revision}` |
| GET/POST/DELETE | `/api/channels/{id}/restriction/image` | Посмотреть / загрузить `file` / сбросить заставку |
| GET/PUT | `/api/channels/{id}/storage` | Метаданные подключения / конфигурация и новые ключи |
| GET | `/api/channels/{id}/storage/objects?prefix=&token=` | Страница ListObjectsV2 |
| POST | `/api/channels/{id}/storage/direct` | `{key,durationSeconds}`, прямой источник S3 |
| POST/GET | `/api/channels/{id}/storage/imports` | `{key}` — локальный импорт / список заданий |
| GET | `/iptv/playlist.m3u` | Публичный список телеканалов |
| GET | `/iptv/epg.xml` | Публичный XMLTV за вчера и следующие 7 дней |
| GET | `/hls/program/{slug}/{file}` | Публичный выход с региональной политикой |
| GET | `/hls/preview/{slug}/{file}` | Монитор, требуется cookie |

Настройки S3: `{endpoint,bucket,region,accessKey,secretKey,sessionToken?,pathStyle:true}`. Пустой secretKey сохраняет ранее настроенные ключи. Нельзя получить секрет обратно через GET. Ключи не добавляются к объекту Source; подписанная ссылка формируется на сервере непосредственно перед AMCP.

SignalR `/hubs/air` отправляет событие `changed(channelId)`, после него клиент обновляет API. Hub не принимает команд эфира.

Графический слой: `{id,kind,text,x,y,width,height,fontSize,color,background}`. Виды: `logo`, `title`, `ticker`, `subtitle`. Координаты Full HD, до 20 слоёв. Используется текстовый логотип; загрузка PNG/SVG-логотипа и импорт временных SRT/WebVTT дорожек — следующий этап. HTML/CSS/JS оператора не исполняются, текст вставляется через textContent.

Внутренний `/internal/media-auth` вызывается только MediaMTX с shared-secret и проверяет engine/OBS. Публичный nginx этот путь не проксирует.

Даты расписания передавайте с часовым поясом; сервер сохраняет UTC. Блок до 24 часов, ровно один sourceId/playlistId, пересечения запрещены. В интерфейсе время отображается в часовом поясе браузера.
