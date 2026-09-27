# API Reference (краткая карта)

Базовый URL: `http://localhost:8080` (Docker) или `https://<host>`.
Префикс: `/api/v1`. Аутентификация: `Authorization: Bearer <accessToken>`.
Полные схемы: Swagger UI (`/swagger`) или `GET /swagger/v1/swagger.json`.

Без токена доступны только `GET /catalog/*` (языки, курсы, уроки, грамматика),
`GET /ai/providers`, `POST /auth/*` и health. Остальные разделы требуют JWT.
`/admin/*` дополнительно требует роль `Admin` или `Teacher` (иначе 403).

Ограничение: 300 запросов/мин на IP. Ошибки — ProblemDetails:
`{ "type", "title", "status", "detail", "instance", "errors" }`.

## auth

| Метод | Путь | Тело / параметры | Ответ |
| --- | --- | --- | --- |
| POST | `/auth/register` | `email`, `password`, `displayName` | 201 + `AuthResponse` |
| POST | `/auth/login` | `email`, `password` | `AuthResponse` |
| POST | `/auth/refresh` | `refreshToken` | `AuthResponse` |
| POST | `/auth/logout` | `refreshToken` в теле или заголовке `X-Refresh-Token` | 204 |
| POST | `/auth/logout-all` | — (отзывает все refresh-токены) | 204 |
| GET | `/auth/me` | — | текущий пользователь |
| POST | `/auth/change-password` | `currentPassword`, `newPassword` | 204 |
| POST | `/auth/forgot-password` | `email` | 202 (всегда, без утечки существования email) |
| POST | `/auth/reset-password` | `email`, `token`, `newPassword` | 204 |

`AuthResponse`: `accessToken`, `refreshToken`, `tokenType`, `expiresInSeconds`, `expiresAt`, `user`
(id, email, displayName, role, targetLanguageId/Code/Name, interfaceLanguageId, level, targetLevel,
dailyGoalXp, speechRate, totalXp, currentStreak, longestStreak, emailConfirmed, createdAt).

`POST /auth/refresh` ротирует refresh-токен: предыдущий токен отзывается, повторное
использование возвращает 401.

## users

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET | `/users/me` | профиль |
| PATCH | `/users/me` | изменить displayName, targetLanguage, cefrLevel, dailyGoal |
| GET | `/users/me/dashboard?activityDays=` | агрегаты для главного экрана |
| GET | `/users/me/stats?days=` | серия, streak, активность по дням |
| GET | `/users/me/goal` | дневная цель и прогресс |
| PATCH | `/users/me` | изменить дневную цель: `dailyGoalXp` (у `/users/me/goal` только GET) |

## catalog

`GET /catalog/languages`, `GET /catalog/languages/{code}`, `GET /catalog/courses?languageId=&level=`,
`GET /catalog/courses/{slug}`, `GET /catalog/courses/{slug}/lessons`, `GET /catalog/lessons/{lessonId}`,
`GET /catalog/grammar?languageId=&maxLevel=`, `GET /catalog/grammar/{id}`.

## dictionary

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET | `/dictionary/words` | поиск: `query`, `languageId`, `translationLanguageId`, `partOfSpeech`, `maxMinLevel`, `tags`, `page`, `limit`; при `semantic=true` — `provider`, `minSimilarity` |
| GET | `/dictionary/words/{id}` | карточка слова |
| GET | `/dictionary/words/similar/{id}` | похожие по embedding (пустой массив, если векторов нет) |
| POST/PUT/DELETE | `/dictionary/words[/{id}]` | CRUD (Admin/Teacher) |
| POST | `/dictionary/words/import` | массовый импорт: `text` + `delimiter`, `hasHeader`, `skipDuplicates`, `computeEmbeddings`, `minLearnerLevel`, `tags` → `{imported, updated, skipped, failed, errors}` |
| GET | `/dictionary/my-words` | слова пользователя (`page`, `pageSize`, `state`, `deckId`) |
| POST/DELETE | `/dictionary/my-words/{lexicalUnitId}` | добавить/убрать |
| GET | `/dictionary/word-of-the-day` | слово дня |
| POST | `/dictionary/audio/word/{lexicalUnitId}` | озвучка слова |

Удаление слова — soft delete (`Status = Deprecated`): слово пропадает из выдачи, но повторный
импорт того же текста восстанавливает его (`updated`).

## decks / practice

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET/POST | `/decks`, GET/PATCH/DELETE `/decks/{id}` | SRS-колоды |
| GET/POST/DELETE | `/decks/{id}/cards[/{lexicalUnitId}]` | карточки колоды |
| POST | `/decks/{id}/reset-progress` | сброс прогресса |
| GET | `/practice/due?limit=&deckId=` | карточки к повторению → `{cards, totalDue, limit}` |
| GET | `/practice/due/count` | только количество |
| GET | `/practice/forecast?deckId=` | нагрузка по дням + `remainingDue` |
| POST | `/practice/reviews` | `{ reviews: [{ reviewCardId, rating: Again\|Hard\|Good\|Easy, durationMs, givenAnswer }], sessionNewCards }` → `[{ reviewCardId, state, dueAt, intervalDays, easeFactor, wasCorrect, masteryScore, correctTranslation, xpEarned }]` |
| POST | `/practice/summary` | итоги сессии |
| POST | `/practice/suspend/{reviewCardId}` | отложить карточку |
| POST | `/practice/prefetch-embeddings` | фоновая дозагрузка векторов (502 `ai_unavailable`, если провайдер недоступен) |

## exercises

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET | `/exercises`, `/exercises/{id}` | список/карточка упражнения |
| POST | `/exercises/generate` | генерация (`exerciseType`, `cefrLevel`, `topic`, `count`) |
| PUT/DELETE | `/exercises/{id}` | CRUD (владелец или Admin) |
| POST | `/exercises/{id}/attempts` | проверить ответы: `{ answers: { "<itemId>": "<optionText>\|<optionIndex>" }, freeTextAnswer, durationMs, useAiGrading }` → `ExerciseGrading` |
| GET | `/exercises/{id}/attempts`, `/exercises/history` | история |
| GET | `/exercises/recommended` | подбор по уровню и weak spots |

Ответ оценивания: `scorePercent`, `isPassed`, `correctCount`, `totalCount`, `xpEarned`,
`feedback` и `items[]` с `expected`/`given`/`correct`/`explanation`. Ключи правильных ответов
(`correct_index`, `correct_answer`, `correct_sentence`, `correct_translation`) хранятся в БД, но
вырезаются из `GET /exercises/{id}`.

## tutor

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET/POST | `/tutor/sessions` | список/создание чат-сессии (201) |
| GET | `/tutor/sessions/{sessionId}`, `/messages` | сессия и сообщения |
| POST | `/tutor/sessions/{sessionId}/messages` | ответ репетитора: `{ message }` |
| POST | `/tutor/sessions/{sessionId}/messages/stream` | SSE-стрим |
| POST | `/tutor/sessions/{sessionId}/messages/{messageId}/regenerate` | повтор ответа |
| PATCH/POST/DELETE | `/tutor/sessions/{sessionId}`, `/archive` | переименовать, архивировать, удалить |
| POST | `/tutor/messages/{messageId}/feedback` | 👍/👎 |
| GET | `/tutor/usage` | расход токенов |

## speech

| Метод | Путь | Назначение |
| --- | --- | --- |
| POST | `/speech/synthesize` | TTS: `text`, `language`, `voice` → audio |
| POST | `/speech/transcribe` | STT: multipart audio |
| POST | `/speech/pronunciation` | оценка произношения: `audio`, `referenceText` |
| GET | `/speech/pronunciation/attempts`, `/speech/recordings` | история |
| DELETE | `/speech/recordings/{id}` | удалить запись |

## ai

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET | `/ai/providers` | список провайдеров и `configured` |
| POST | `/ai/providers/{name}/health` | проверка доступности |
| GET | `/ai/usage` | расход и статистика собственных AI-вызовов |
| GET | `/ai/limits` | квоты и rate limits |
| POST | `/ai/dictionary/reindex` | пересчёт embeddings (Admin) |

## admin (Admin, Teacher)

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET | `/admin/stats` | сводка: пользователи, контент, активность, AI-расход |
| GET | `/admin/users?query=&page=&pageSize=` | пользователи |
| PATCH | `/admin/users/{id}/role?role=` | смена роли |
| PATCH | `/admin/users/{id}/active?isActive=` | блокировка/разблокировка |
| DELETE | `/admin/ai-logs` | очистка AI-логов |

## Служебные

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET | `/` | редирект на `/swagger` |
| GET | `/health/live` | liveness |
| GET | `/health/ready` | readiness (PostgreSQL) |
| GET | `/openapi/v1.json` | метаданные OpenAPI-инструментов приложения |
| GET | `/swagger/v1/swagger.json` | полный OpenAPI-документ |
