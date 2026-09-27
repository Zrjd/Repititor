# Repetitor API

ASP.NET Core 10 Web API для приложения Repetitor (Android / Web): языковые курсы, словарь с
семантическим поиском (pgvector), SRS-карточки, AI-упражнения, чат-репетитор, проверка ответов,
TTS/STT и оценка произношения.

Старый MVC-проект `Repetitor/` в этом репозитории не используется и не изменяется.

## Стек

| Компонент | Версия / инструмент |
| --- | --- |
| .NET | 10 (`net10.0`) |
| EF Core | 10.0.x (`Npgsql.EntityFrameworkCore.PostgreSQL` 10.x) |
| БД | PostgreSQL 17 + pgvector |
| Auth | JWT (access/refresh), BCrypt (work factor 12) |
| AI | OpenAI Chat/Embeddings, Ollama (chat + embeddings) |
| Документация | Swashbuckle 10 (`/swagger`, `/swagger/v1/swagger.json`; вкл. `SWAGGER_ENABLED`) |
| Тесты | xUnit 2.9 (`tests/Repetitor.Api.Tests`, 69 тестов) |

## Структура репозитория

```
Repetitor.sln
web/                              - React 19 SPA (Vite, TypeScript, React Router, TanStack Query)
src/Repetitor.Api/
  Program.cs                     — composition root, pipeline, health checks
  Api/                           — контроллеры, DTO, validation
  Domain/                        — сущности и перечисления
  Infrastructure/
    Auth/                        — JWT, refresh-токены, хеширование паролей
    Ai/                          — провайдеры OpenAI/Ollama, gateway, streaming
    Media/                       — загрузка/раздача файлов
    Persistence/                 — DbContext, converters, migrations, patches, seeding
    Services/                    — SRS, RAG, embeddings, упражнения, grading, tutor, speech
deploy/init.sql                  — SQL-скрипт начальной схемы
tests/Repetitor.Api.Tests/       — unit-тесты
docker-compose.yml               — PostgreSQL/pgvector + API + Web UI (+ Ollama по профилю)
```

## Быстрый старт (Docker Compose)

1. Скопируйте `.env.example` в `.env` и задайте **обязательно**:

   ```dotenv
   JWT_SIGNING_KEY=<случайная строка >= 32 байт>
   POSTGRES_PASSWORD=<пароль>
   ```

2. Поднимите сервисы:

   ```bash
   docker compose up -d --build
   docker compose ps
   ```

3. Проверьте готовность:

   ```bash
   curl http://localhost:8080/health/live
   curl http://localhost:8080/health/ready
   ```

4. Swagger UI: <http://localhost:8080/swagger>

5. Web UI (React SPA) — <http://localhost:8081>:

   ```bash
   docker compose up -d --build web
   ```

Локальный AI через Ollama (профиль `ai-local`, требует ~4 ГБ RAM):

```bash
docker compose --profile ai-local up -d
docker compose exec ollama ollama pull llama3.1
docker compose exec ollama ollama pull nomic-embed-text
```

## Web UI

`web/` — React 19 SPA (Vite, TypeScript, React Router, TanStack Query) для ядра API: вход и
регистрация, дашборд, каталог курсов и уроки, грамматика, словарь, мои слова и SRS-сессия.
Интерфейс русский/английский, язык сохраняется в браузере.

```bash
cd web
npm install
npm run dev        # http://localhost:5173, /api проксируется на http://localhost:8080
```

Подробности, скрипты и переменные окружения — в [`web/README.md`](web/README.md).

## Быстрый старт (без Docker)

```bash
dotnet restore
dotnet run --project src/Repetitor.Api
```

Требуется локальный PostgreSQL с расширением `vector`:

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

Конфигурация задаётся через `appsettings.json`, `appsettings.Development.json` или переменные
окружения. Все переменные можно переопределить префиксом `REPETITOR_` (например,
`REPETITOR_Jwt__SigningKey`). Обычный формат `Section__Key` тоже работает.

При старте приложение (при `Database:AutoMigrate=true`):

1. применяет EF-миграции;
2. выполняет идемпотентные `SchemaPatches`:
   - `0001_pgvector_embeddings` — `vector`-колонки `lexical_unit_embeddings.embedding`
     (`Ai:EmbeddingDimensions`) и `embedding_local` (`Ai:EmbeddingLocalDimensions`) + HNSW-индексы;
   - `0002_dictionary_trgm_search` — GIN-индексы `gin_trgm_ops` для поиска по словам;
   - `0003_daily_stats_view` — представление `vw_user_learning_summary`;
3. добавляет seed-данные (языки, демо-курс, уроки, грамматика, словарь, упражнения).

`deploy/init.sql` содержит только EF-миграцию; при ручном применении SQL выполните также
`SchemaPatches` (или simply запустите приложение один раз).

## Ключевые настройки

| Ключ | Назначение | По умолчанию |
| --- | --- | --- |
| `ConnectionStrings:Postgres` | строка подключения Npgsql | `Host=localhost;Database=repetitor;Username=postgres;Password=postgres` |
| `Jwt:SigningKey` | HMAC-ключ, **минимум 32 байта** | dev-заглушка в `appsettings.json` — замените |
| `Jwt:AccessTokenMinutes` | TTL access-токена | 30 |
| `Jwt:RefreshTokenDays` | TTL refresh-токена | 30 |
| `Database:AutoMigrate` | миграции + patches + seed при старте | `true` |
| `Seed:AdminEmail` / `Seed:AdminPassword` | создание админа при seed | пусто |
| `Ai:DefaultChatProvider` | `openai` или `ollama` | `openai` (в `appsettings.json` выключен, фактически доступен `ollama`) |
| `Ai:DefaultEmbeddingProvider` | `openai` или `ollama` | `openai` |
| `Ai:Providers:openai:ApiKey` | ключ OpenAI | пусто |
| `Ai:Providers:ollama:BaseUrl` | адрес Ollama | `http://localhost:11434/` |
| `Ai:MaxRagContextItems` | сколько найденных слов попадает в RAG-контекст | 8 |
| `Ai:RagMinSimilarity` | порог cosine-релевантности для RAG | 0.25 |
| `Ai:EmbeddingDimensions` / `Ai:EmbeddingLocalDimensions` | размерность векторов (remote/local) | 1536 / 1024 |
| `Media:RootPath` | каталог загрузок | `media` |
| `Learning:NewCardsPerDay` | лимит новых SRS-карточек в день | 20 |
| `Learning:MaxFreeAiCallsPerDay` | лимит AI-вызовов пользователя в день | 100 |
| `Cors:0` | разрешённый origin | `*` |

Ограничения запросов: глобальный rate limit — 300 запросов/мин на IP.

## Аутентификация

```bash
curl -X POST http://localhost:8080/api/v1/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"email":"user@example.com","password":"Str0ng-Passw0rd!","displayName":"User"}'

curl -X POST http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"user@example.com","password":"Str0ng-Passw0rd!"}'
```

Ответ содержит `accessToken` (Bearer) и `refreshToken`. Refresh-токены хранятся как
opaque-значения, в БД лежит только SHA-256 хеш; `logout-all` отзывает все сессии
пользователя. Роль проверяется политикой `Learner` (роль `Learner` или `Admin`).

## Эндпоинты

Базовый префикс — `/api/v1`. Полный список и схемы: Swagger UI или `GET /swagger/v1/swagger.json`.

| Область | Префикс | Назначение |
| --- | --- | --- |
| Auth | `/api/v1/auth` | register, login, refresh, logout, me, смена/сброс пароля |
| Users | `/api/v1/users` | профиль, дашборд, статистика, учебная цель |
| Catalog | `/api/v1/catalog` | языки, курсы, уроки, грамматика |
| Dictionary | `/api/v1/dictionary` | поиск слов, CRUD, импорт, мои слова, word of the day, TTS |
| Decks | `/api/v1/decks` | SRS-колоды и карточки |
| Practice | `/api/v1/practice` | due-карточки, отзывы, summary, forecast, embeddings |
| Exercises | `/api/v1/exercises` | CRUD упражнений, генерация через AI, попытки, рекомендации |
| Tutor | `/api/v1/tutor` | чат-сессии, сообщения, SSE-стрим, regenerate, feedback |
| Speech | `/api/v1/speech` | TTS, STT, оценка произношения, записи |
| Ai | `/api/v1/ai` | провайдеры и их health, usage, stats, reindex, лимиты, роли |

Коды ошибок: `400` — валидация (`ValidationProblemDetails` с `type = urn:repetitor:validation`),
`401`/`403` — авторизация, `404` — не найдено, `409` — конфликт, `429` — rate limit,
`500`/`503` — ошибки сервисов и провайдеров.

## Миграции

```bash
dotnet ef migrations add <Name> --project src/Repetitor.Api
dotnet ef database update --project src/Repetitor.Api
dotnet ef migrations script --project src/Repetitor.Api --output deploy/init.sql --idempotent
```

Векторные колонки и индексы создаются скриптом `SchemaPatches`, а не EF-моделями.

## Тесты и сборка

```bash
dotnet build Repetitor.sln
dotnet test tests/Repetitor.Api.Tests/Repetitor.Api.Tests.csproj
```

Покрыты `TextNormalizer` (нормализация, токенизация, similarity, Levenshtein), `SrsService`
(расчёт интервалов, due/forecast, состояния), `TokenService`/BCrypt, `AnswerGradingService`
(эвристика, AI-fallback, gap-fill), `PagedResponse`, `ApiValidation`, `EmbeddingService`.

## Безопасность

- Пароли: BCrypt work factor 12, `NeedsRehash` для автоматической ротации.
- Refresh-токены: только хеши, ротация при `refresh`, отзыв по `logout-all`.
- Входные данные ограничиваются DTO и `ApiValidation`; EF-параметризация защищает от SQL-инъекций.
- Исключения логируются через `GlobalExceptionHandler`, клиенту возвращается ProblemDetails без деталей.
- Загруженные файлы сохраняются под UUID-именами, пути проверяются на выход за пределы `Media:RootPath`.
- В production задайте собственные `Jwt:SigningKey`, `Cors`, `Seed:*` и отключите Swagger при необходимости.
