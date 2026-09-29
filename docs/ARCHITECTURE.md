# Архитектура Repetitor API

## Слои

```
Controllers (Api/Controllers)      HTTP, auth-политики, маппинг DTO
        │
Services (Infrastructure/Services)  бизнес-логика: SRS, RAG, grading, tutor, speech
        │
Gateways (Infrastructure/Ai)        доступ к OpenAI/Ollama, единый лимит и логирование
        │
Persistence (Infrastructure/Persistence)  EF Core DbContext, migrations, patches, seeding
```

Контроллеры не содержат бизнес-логики: валидация — через `ApiValidation`/валидацию
DataAnnotations, результат — `ActionResult<T>` с `PagedResponse<T>` для списков.

## Токены доступа

- **Access** — подписанный JWT (`HS256`), TTL из `Jwt:AccessTokenMinutes`.
  Claims: `sub` (userId), `email`, `name`, `role`, `level` (CEFR), `lang` (целевой язык),
  `sid` (refresh-сессия), `jti`.
- **Refresh** — 32-байтовое случайное значение в base64url. В БД (`UserSessions`) хранится
  только SHA-256 хеш вместе с `ExpiresAt`, `RevokedAt`, `CreatedAt`, IP и user-agent.
- `refresh` ротирует токен и обновляет `sid` в access-токене; повторное использование
  отозванного токена отклоняется.
- `logout` отзывает текущую сессию, `logout-all` — все сессии пользователя.
- Пароли: BCrypt (work factor 12), автоматический rehash при входе.

Из-за стандартного inbound claim mapping `sub` читается как `ClaimTypes.NameIdentifier`,
а `email` — как `ClaimTypes.Email`; `CurrentUserAccessor` учитывает оба варианта.

## AI-провайдеры

`IAiGateway` — единственная точка входа для LLM/embeddings:

- `OpenAiClient` — `/v1/chat/completions`, `/v1/embeddings`, SSE-стрим;
- `OllamaClient` — `/api/chat`, `/api/embed`, NDJSON-стрим;
- `AiGateway` — выбор провайдера по operation (`Chat` / `Embedding` / `Grading`),
  лимиты параллелизма, `AiProviderException` с HTTP-статусом, запись в `AiUsageLogs`.

Провайдер без API-ключа исключается из выбора: `GET /api/v1/ai/providers` показывает
`configured: false`, а `POST /api/v1/ai/providers/{name}/health` — доступность.

## RAG и embeddings

1. `EmbeddingService` строит текст (`BuildEmbeddingText`) и посылает в провайдер;
   вектор приводится к размерности провайдера (`ToVectorLiteral`): `Ai:EmbeddingDimensions`
   для удалённых (1536) и `Ai:EmbeddingLocalDimensions` для локальных (1024) моделей.
2. Векторы хранятся в `lexical_unit_embeddings.embedding` / `embedding_local` — это
   `vector(...)` с HNSW-индексами `vector_cosine_ops`, которые создаёт патч
   `0001_pgvector_embeddings`; провайдер, модель, размерность и `content_hash` лежат рядом.
3. Поиск похожих — cosine distance (`<=>`) с порогом `Ai:RagMinSimilarity`, в RAG-контекст
   попадает до `Ai:MaxRagContextItems` слов.
4. `POST /api/v1/ai/dictionary/reindex` пересчитывает векторы для всех слов.

## SRS (интервальное повторение)

`SrsService.Schedule(card, rating, clock)` работает в два режима: обучающие шаги для
`New`/`Learning`/`Relearning` и интервальное повторение для `Review`/`Mastered`.
Рейтинг — `ReviewRating.Again | Hard | Good | Easy`.

Обучающие шаги (интервал в днях):

| Рейтинг | Шаг 0 | Шаг 1 | Переход |
| --- | --- | --- | --- |
| `Again` | 1 мин | 1 мин | `Learning`, шаг 0 |
| `Hard` | 1.5 мин | 15 мин | `Learning`, ease −0.15 |
| `Good` | 10 мин | — | `Learning` → `Review`, 1 день |
| `Easy` | 4 дня | — | сразу `Review` |

Интервальное повторение:

| Рейтинг | Новый интервал | Ease | Состояние |
| --- | --- | --- | --- |
| `Again` | `max(1, Interval * 0.4)`, `Repetitions = 0`, `Lapses++` | −0.2 | `Relearning` |
| `Hard` | `max(1, Interval * 1.2, Interval + 1)` | −0.15 | без смены |
| `Good` | `Interval * Ease` (для `Mastered` — `max(Ease, 2.0)`) | — | `Mastered` при `Repetitions ≥ 3` и `Interval ≥ 21` |
| `Easy` | `max(Interval + 1, Interval * Ease * 1.3)` | +0.15 | `Mastered` при `Repetitions ≥ 2` |

`EaseFactor` ограничен 1.3..3.2 (старт 2.5), интервал — `0..ReviewCard.MaxIntervalDays`.
`Mastery` меняется на `+20 / +12 / +6 / −10` для `Easy / Good / Hard / Again`
(для новых и обучающихся карточек — вдвое, для `Relearning` — `+5`).
`ReviewCard` хранит `EaseFactor`, `IntervalDays`, `Repetitions`, `Lapses`, `LearningStep`,
`State`, `Mastery`, `DueAtUtc`, `LastReviewedAtUtc`, `SuspendedUntilUtc`.
Лимит новых карточек в день — `Learning:NewCardsPerDay`.

## Проверка ответов

`AnswerGradingService`:

1. **Эвристика** (`UseAi = false` или AI недоступен): нормализация (`TextNormalizer`),
   сравнение с reference и `AcceptedVariants`, similarity (Levenshtein); ответ считается
   верным при `Similarity >= 0.92` или полном совпадении после нормализации.
2. **AI** (`UseAi = true`): запрос к LLM со схемой ответа (`Correct`, `ScorePercent`,
   `Feedback`, `Issues`), ответ валидируется и нормализуется; при `AiProviderException`
   используется эвристика.
3. **Упражнения** (`GradeExerciseAsync`): разбор JSON-полезной нагрузки по типу
   (`MultipleChoice`, `Listening`, `MatchPairs`, `GapFill`/`FillInTheBlanks`, `WordOrder`,
   `Translate*`); порог fuzzy-сравнения 0.94, агрегация `ScorePercent` и `Issues`.

`TextNormalizer.Normalize` приводит текст к сравнимому виду: lower-case, апострофы удаляются,
пунктуация заменяется пробелом, пробелы схлопываются. Это обязательное условие для
корректности similarity и проверки ответов.

## Фоновая генерация уроков

Генерация урока занимает минуты, поэтому запрос не ждёт модель: он только ставит задание
в очередь, а результат пишет фоновый воркер.

- Очередь хранится в самом уроке: `Lesson.AiGenerationStatus`
  (`None` / `Queued` / `Running` / `Completed` / `Failed`) плюс
  `AiGenerationRequestedAt`, `AiGenerationStartedAt`, `AiGenerationCompletedAt`,
  `AiGenerationError` и `AiGenerationRequest` (`jsonb` с темой, длительностью и пожеланиями).
  Отдельной таблицы заданий нет — состояние переживает рестарт вместе с уроком.
- `LessonGenerationWorker` (BackgroundService) каждые `Generation:PollSeconds` секунд
  атомарно забирает одно задание (`UPDATE ... WHERE status = Queued`, в Postgres это
  блокировка строки), переводит его в `Running` и пишет результат: заголовок, краткое
  описание, markdown и словарь. Второй инстанс API подхватит то же задание, а не
  продублирует его.
- Прерванные задания не теряются: при старте и раз в `Generation:StaleAfterMinutes`
  всё, что осталось в `Running`, возвращается в очередь.
- `POST .../lessons/{id}/generate` отвечает `202` с состоянием, `409` — если задание
  уже в очереди или выполняется, `?wait=true` — старый синхронный ответ (состояние
  при этом не меняется). `GET .../lessons/{id}/generation` отдаёт текущее состояние,
  список уроков — тоже, вместе с признаком `isAvailableToStudents`.
- Ручная правка заголовка, описания, markdown или словаря снимает бейдж `Completed`
  и возвращает урок в `None`; правка сортировки состояние не трогает.

Ошибка модели пишется в `AiGenerationError` и статус `Failed`, урок можно поставить
в очередь снова. Таймаут запроса к провайдеру (`Ai:Providers:*:TimeoutSeconds`,
по умолчанию 120 секунд) считается и для стриминга: тишина в потоке дольше этого
значения считается зависанием, так как `ResponseHeadersRead` снимает `HttpClient.Timeout`
сразу после заголовков.

## Речь

- **TTS** — провайдер (OpenAI `audio/speech` или локальный кэш/озвучка), результат
  кэшируется по `sha256(text|voice|lang)`, отдаётся как `audio/mpeg`.
- **STT** — загрузка записи, распознавание речи, результат в `SpeechRecordings`.
- **Произношение** — сравнение распознанного текста с эталоном, оценка 0..100, история попыток.
  Файлы хранятся в `Media:RootPath` под UUID-именами, удаление проверяет границы каталога.

## База данных и миграции

- EF Core 10 + Npgsql, `AppDbContext` как `DbContextFactory` и scoped-контекст.
- `JsonNode` конвертируется в `jsonb` (`JsonNodeValueConverter`).
- Массивы UUID хранятся в нативных `uuid[]`.
- `DatabaseInitializer` при старте: `MigrateAsync` → `SchemaPatches` (vector/trgm/view) → seed.
- Seed идемпотентен: языки, демо-курс с уроками, грамматика, словарь, упражнения,
  администратор (только если `Seed:AdminEmail` и `Seed:AdminPassword` заданы).

## Наблюдаемость

- `RequestLoggingMiddleware` — метод, путь, статус, длительность, requestId.
- `GlobalExceptionHandler` — единый ProblemDetails, детали только в лог.
- Health checks: `self` (`/health/live`) и `postgres` (`/health/ready` через `AddDbContextCheck`).
- Логи AI: `AiUsageLogs` (operation, provider, токены, latency, статус) → `/api/v1/ai/usage|stats`.
