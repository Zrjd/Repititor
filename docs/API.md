# API Reference (краткая карта)

Базовый URL: `http://localhost:8080` (Docker) или `https://<host>`.
Префикс: `/api/v1`. Аутентификация: `Authorization: Bearer <accessToken>`.
Полные схемы: Swagger UI (`/swagger`) или `GET /swagger/v1/swagger.json`.

Без токена доступны `GET /catalog/*` (языки, курсы, уроки, грамматика), `GET /ai/providers`,
`POST /ai/providers/{name}/health`, `GET /ai/limits`, `POST /auth/*` и health.
Остальные разделы требуют JWT. `/teacher/*` и `/teacher/groups/*` требуют роль `Teacher`
или `Admin` (иначе 403). `/admin/courses` и `/admin/lessons` — роль `Admin` или `Teacher`
с проверкой владения, чужой курс недоступен. Остальные `/admin/*` (пользователи, статистика,
AI-настройки, AI-логи) и `POST /ai/dictionary/reindex` доступны только `Admin`: там
хранятся ключи провайдеров и административная статистика.

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

## teacher (Teacher, Admin)

Собственные курсы и уроки. Курс создаётся черновиком и попадает в публичный каталог
только после публикации. Чужой курс или урок недоступны: 404, либо 403 при известном id.

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET | `/teacher/courses?languageId=&includeUnpublished=` | свои курсы, включая черновики |
| POST | `/teacher/courses` | создать курс (черновик) → 201 + курс |
| GET | `/teacher/courses/{id}` | данные курса для редактирования |
| PUT | `/teacher/courses/{id}` | частичное обновление; статус публикации здесь не меняется |
| POST | `/teacher/courses/{id}/publish` | публикация курса |
| POST | `/teacher/courses/{id}/unpublish` | снятие с публикации (возврат в черновики) |
| DELETE | `/teacher/courses/{id}` | удалить курс вместе с уроками |
| GET | `/teacher/courses/{id}/lessons` | уроки курса, включая неопубликованные |
| POST | `/teacher/courses/{courseId}/lessons` | создать урок в своём курсе |
| GET | `/teacher/lessons/{id}` | данные урока для редактирования |
| PUT | `/teacher/lessons/{id}` | частичное обновление урока |
| DELETE | `/teacher/lessons/{id}` | удалить урок |
| POST | `/teacher/lessons/{id}/generate` | поставить генерацию содержания урока в очередь ИИ (`?wait=true` — старый синхронный ответ) |
| GET | `/teacher/lessons/{id}/generation` | состояние фоновой генерации урока |
| POST | `/teacher/courses/{id}/generate` | генерация описания и плана уроков курса через ИИ |

## groups (учебные группы)

Учитель набирает учеников по email или по коду-приглашению и назначает группе курсы.
Назначение курса сразу зачисляет всех участников; новые участники получают те же курсы.
Группа из одного участника — персональные занятия. Записи на курсы и прогресс учеников
не отзываются при исключении из группы, снятии курса или удалении группы.

### teacher/groups (Teacher, Admin)

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET | `/teacher/groups` | свои группы с числом участников и курсов; администратор — все |
| POST | `/teacher/groups` | создать группу: `name`, `description` → 201 |
| GET | `/teacher/groups/assignable-courses` | курсы для назначения: свои (включая черновики) и опубликованные системные |
| GET | `/teacher/groups/{id}` | группа с составом, курсами и кодами-приглашениями |
| PATCH | `/teacher/groups/{id}` | изменить `name` и `description` |
| DELETE | `/teacher/groups/{id}` | удалить группу вместе с составом, курсами и приглашениями |
| POST | `/teacher/groups/{id}/members` | добавить зарегистрированного ученика по `email`; 404, если ученик не найден; повтор не создаёт дубль |
| DELETE | `/teacher/groups/{id}/members/{userId}` | исключить ученика; уже открытые им курсы остаются |
| POST | `/teacher/groups/{id}/invitations` | код-приглашение: `expiresInDays` (1–365), `maxUses` (0 — без ограничений) |
| GET | `/teacher/groups/{id}/invitations` | коды группы: активен / отозван / истёк / исчерпан |
| DELETE | `/teacher/groups/{id}/invitations/{invitationId}` | отозвать код (204) |
| POST | `/teacher/groups/{id}/courses` | назначить курс (`courseId`) и зачислить участников; для чужого или неопубликованного системного — 400 |
| DELETE | `/teacher/groups/{id}/courses/{courseId}` | убрать курс из группы; записи и прогресс сохраняются |

### groups (любой авторизованный)

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET | `/groups` | группы, где пользователь состоит учеником: учитель, число участников, курсы |
| POST | `/groups/join` | вступить по `code` и получить курсы группы; 400 — код отозван, истёк или исчерпан |

## ai

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET | `/ai/providers` | список провайдеров и `configured` |
| POST | `/ai/providers/{name}/health` | проверка доступности |
| GET | `/ai/usage` | расход и статистика собственных AI-вызовов |
| GET | `/ai/limits` | квоты и rate limits |
| POST | `/ai/dictionary/reindex` | пересчёт embeddings (Admin) |

## admin

### Пользователи, статистика и ИИ (только Admin)

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET | `/admin/stats` | сводка: пользователи, контент, активность, AI-расход |
| GET | `/admin/users?query=&page=&pageSize=` | пользователи |
| PATCH | `/admin/users/{id}/role?role=` | смена роли |
| PATCH | `/admin/users/{id}/active?active=` | блокировка/разблокировка |
| GET | `/admin/ai-settings` | провайдеры, модели, температура, лимиты, промпт уроков |
| PUT | `/admin/ai-settings` | частичное обновление; сохраняется в `app_settings` |
| POST | `/admin/ai-settings/test?provider=&model=` | проверка подключения с замером задержки |
| DELETE | `/admin/ai-logs?olderThanDays=` | удалить записи журнала старше N дней (по умолчанию 30) |

### Курсы и уроки (Admin, Teacher)

Учитель видит и изменяет только свои курсы и уроки, администратор — все. Поля публикации
управляются отдельным запросом `publish`, а не через `PUT`.

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET | `/admin/courses?languageId=&includeUnpublished=` | курсы панели управления, включая неопубликованные |
| POST | `/admin/courses` | создать курс (черновик) → 201 |
| GET | `/admin/courses/{id}` | курс для редактирования |
| PUT | `/admin/courses/{id}` | частичное обновление курса |
| DELETE | `/admin/courses/{id}` | удалить курс |
| POST | `/admin/courses/{id}/publish?published=` | публикация или снятие с публикации |
| GET | `/admin/courses/{id}/lessons` | уроки курса |
| POST | `/admin/lessons` | создать урок (`courseId`) |
| GET | `/admin/lessons/{id}` | урок для редактирования |
| PUT | `/admin/lessons/{id}` | частичное обновление урока |
| DELETE | `/admin/lessons/{id}` | удалить урок |
| POST | `/admin/lessons/{id}/generate` | поставить генерацию содержания урока в очередь ИИ (`?wait=true` — старый синхронный ответ) |
| GET | `/admin/lessons/{id}/generation` | состояние фоновой генерации урока |
| POST | `/admin/courses/{id}/generate` | генерация описания и плана уроков курса через ИИ |

## Фоновая генерация уроков

`POST /admin/lessons/{id}/generate` и `POST /teacher/lessons/{id}/generate` по умолчанию не ждут ИИ:
сервер сохраняет запрос в уроке, отвечает `202 Accepted` с текущим состоянием и возвращает управление.
Фоновый воркер выполняет задания по одному, сам записывает заголовок, описание, Markdown и ключевые слова
в урок и переводит статус в `Completed`; при ошибке — `Failed` с текстом в `error`.

Состояние (`None`, `Queued`, `Running`, `Completed`, `Failed`) возвращается в списках уроков
(`aiGenerationStatus`, `aiGenerationRequestedAt`, `aiGenerationCompletedAt`, `aiGenerationError`)
и отдельным запросом `GET .../lessons/{id}/generation`.

- Повторный запрос, пока генерация активна, возвращает `409` с текущим состоянием.
- `?wait=true` сохраняет прежнее синхронное поведение: ответ приходит с готовым содержимым.
- Ручная правка `title`, `summary`, `contentMarkdown` или `keyVocabulary` снимает метку
  «генерация завершена» (`Completed` → `None`), но не прерывает активную генерацию.
- `isAvailableToStudents` — черновик, если урок или его курс не опубликованы.
- Настройки: `Generation:WorkerEnabled`, `Generation:PollSeconds`, `Generation:StaleAfterMinutes`,
  `Generation:MaxErrorLength`. После перезапуска API задания в статусе `Running` возвращаются в очередь.

## Служебные

| Метод | Путь | Назначение |
| --- | --- | --- |
| GET | `/` | редирект на `/swagger` |
| GET | `/health/live` | liveness |
| GET | `/health/ready` | readiness (PostgreSQL) |
| GET | `/openapi/v1.json` | метаданные OpenAPI-инструментов приложения |
| GET | `/swagger/v1/swagger.json` | полный OpenAPI-документ |
