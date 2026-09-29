# Развёртывание

## Docker Compose (рекомендуется)

```bash
cp .env.example .env      # задайте JWT_SIGNING_KEY и POSTGRES_PASSWORD
docker compose up -d --build
docker compose logs -f api
```

Compose-сервисы:

| Сервис | Образ / сборка | Порт | Назначение |
| --- | --- | --- | --- |
| `postgres` | `pgvector/pgvector:pg17` | 5432 | БД с расширением `vector` |
| `api` | `src/Repetitor.Api/Dockerfile` | 8080 | Web API (non-root, healthcheck) |
| `ollama` | `ollama/ollama:latest` | 11434 | локальный AI, профиль `ai-local` |

Особенности:

- `api` стартует только после `healthy`-проверки PostgreSQL;
- тома: `pgdata` (данные), `media` (загрузки), `ollama` (модели);
- `Dockerfile` — multi-stage, финальный образ `mcr.microsoft.com/dotnet/aspnet:10.0`,
  non-root запуск под numeric UID/GID `1001` (`ARG APP_UID`, без записи в `/etc/passwd`,
  т.к. в базовом образе нет `adduser`), `HEALTHCHECK` на `/health/live`;
- Swagger включается `Swagger__Enabled=true` (в Compose — `SWAGGER_ENABLED`);
- миграции, `SchemaPatches` и seed выполняются приложением при старте
  (`Database__AutoMigrate=true`).

Полезные команды:

```bash
docker compose config                 # проверить итоговую конфигурацию
docker compose --profile ai-local up -d
docker compose exec postgres psql -U repetitor -d repetitor -c '\dx'
docker compose down                   # остановить
docker compose down -v                # остановить и удалить тома
```

### Ollama на хосте (Docker Desktop / Windows и macOS)

Если Ollama запущен на хосте, а не в контейнере, сервис `ollama` поднимать не нужно —
это также снимает конфликт за порт `11434`. В `.env` укажите адрес хоста и модели,
которые действительно загружены (`ollama list`):

```dotenv
OLLAMA_BASE_URL=http://host.docker.internal:11434/
OLLAMA_CHAT_MODEL=Qwen3:8b
OLLAMA_EMBEDDING_MODEL=nomic-embed-text
```

```bash
ollama pull Qwen3:8b              # чат-модель
ollama pull nomic-embed-text      # модель эмбеддингов
docker compose up -d api          # пересоздать api с новой конфигурацией
curl -fsS -X POST http://localhost:8080/api/v1/admin/ai-settings/test?provider=ollama
```

Последний вызов требует токена администратора и делает реальный запрос генерации:
`healthy=true` означает, что API видит Ollama и выбранные модели загружены.
Если модель не загружена, приходит `404: model ... not found`.

Полный набор переменных для Ollama:

| Переменная | Назначение | По умолчанию |
| --- | --- | --- |
| `OLLAMA_ENABLED` | включить провайдер | `true` |
| `OLLAMA_BASE_URL` | адрес API Ollama | `http://ollama:11434/` |
| `OLLAMA_PORT` | публикация порта сервиса `ollama` | `11434` |
| `OLLAMA_CHAT_MODEL` | модель чата | `qwen2.5:7b-instruct` |
| `OLLAMA_EMBEDDING_MODEL` | модель эмбеддингов | `nomic-embed-text` |

## Ручное развёртывание

```bash
dotnet publish src/Repetitor.Api -c Release -o /opt/repetitor
```

Переменные окружения:

```bash
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS=http://+:8080
export ConnectionStrings__Postgres='Host=db;Database=repetitor;Username=repetitor;Password=...'
export Jwt__SigningKey='<>= 32 байт>'
export Jwt__Issuer=repetitor-api
export Jwt__Audience=repetitor-clients
export Seed__AdminEmail=admin@example.com
export Seed__AdminPassword='<сильный пароль>'
export Ai__DefaultChatProvider=ollama
export Ai__Providers__ollama__BaseUrl=http://127.0.0.1:11434/
export Media__RootPath=/var/lib/repetitor/media
```

Также поддерживается префикс `REPETITOR_` (например, `REPETITOR_Jwt__SigningKey`).

Фоновую генерацию уроков можно отключить на конкретном инстансе, например если генерацию
выполняет отдельный процесс API:

```bash
export Generation__WorkerEnabled=false
```

Systemd-пример (`/etc/systemd/system/repetitor-api.service`):

```ini
[Unit]
Description=Repetitor API
After=network-online.target

[Service]
Type=simple
User=repetitor
WorkingDirectory=/opt/repetitor
EnvironmentFile=/etc/repetitor/api.env
ExecStart=/usr/bin/dotnet /opt/repetitor/Repetitor.Api.dll
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

## Ручное применение SQL

```bash
psql -d repetitor -f deploy/init.sql
```

`deploy/init.sql` создан `dotnet ef migrations script --idempotent` и содержит
`CREATE EXTENSION IF NOT EXISTS vector;` плюс таблицы EF-модели. Векторные колонки/индексы
(`SchemaPatches`) и seed в скрипт не входят: их выполняет приложение при старте либо
выполните соответствующие `DO $$ ... $$` блоки вручную.

## Проверка после развёртывания

```bash
curl -fsS http://localhost:8080/health/live     # {"status":"Healthy"}
curl -fsS http://localhost:8080/health/ready    # postgres Healthy
curl -fsS http://localhost:8080/api/v1/catalog/languages
```

## Безопасность

- Обязательно задайте собственные `Jwt__SigningKey` (≥ 32 байт) и пароли БД.
- Ограничьте `Cors__0` конкретным origin приложения вместо `*`.
- Не публикуйте PostgreSQL наружу; в compose порт нужен только для отладки.
- `Seed__AdminPassword` задайте один раз, затем уберите переменную.
- Терминируйте TLS на reverse proxy (nginx/Caddy) и проксируйте `/api`, `/swagger`, `/health`.
