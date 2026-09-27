import { api } from './client'
import type {
  ActivityDay,
  AdminCourse,
  AdminLesson,
  AiSettings,
  AuthResponse,
  CefrLevel,
  Course,
  Dashboard,
  Deck,
  DueCards,
  GenerateCourseContent,
  GenerateLessonContent,
  GoalProgress,
  GrammarTopic,
  Language,
  Lesson,
  LessonSummary,
  LexicalUnit,
  PagedResponse,
  PracticeSummary,
  ReviewCard,
  ReviewRating,
  ReviewResult,
  SimilarWord,
  UserProfile,
  UserWord,
  WordOfTheDay,
} from './types'

export const authApi = {
  login: (email: string, password: string) =>
    api.post<AuthResponse>('/auth/login', { email, password }, { auth: false }),
  register: (email: string, password: string, displayName: string) =>
    api.post<AuthResponse>('/auth/register', { email, password, displayName }, { auth: false }),
  me: () => api.get<UserProfile>('/auth/me'),
  logout: (refreshToken: string) => api.post<void>('/auth/logout', { refreshToken }),
  logoutAll: () => api.post<void>('/auth/logout-all'),
  updateProfile: (patch: {
    displayName?: string
    targetLanguageId?: string
    cefrLevel?: CefrLevel
    dailyGoalXp?: number
    speechRate?: number
  }) => api.patch<UserProfile>('/users/me', patch),
  setGoal: (dailyGoalXp: number) => api.patch<UserProfile>('/users/me', { dailyGoalXp }),
}

export const catalogApi = {
  languages: () => api.get<Language[]>('/catalog/languages', { auth: false }),
  language: (code: string) => api.get<Language>(`/catalog/languages/${code}`, { auth: false }),
  courses: (languageId?: string, level?: CefrLevel) =>
    api.get<Course[]>('/catalog/courses', { query: { languageId, level }, auth: false }),
  course: (slug: string) => api.get<Course>(`/catalog/courses/${slug}`, { auth: false }),
  lessons: (slug: string) => api.get<LessonSummary[]>(`/catalog/courses/${slug}/lessons`, { auth: false }),
  lesson: (lessonId: string) => api.get<Lesson>(`/catalog/lessons/${lessonId}`),
  grammar: (languageId?: string, maxLevel?: CefrLevel) =>
    api.get<GrammarTopic[]>('/catalog/grammar', { query: { languageId, maxLevel }, auth: false }),
}

export interface WordSearchParams {
  query?: string
  languageId?: string
  partOfSpeech?: string
  maxMinLevel?: CefrLevel
  page?: number
  limit?: number
}

export const dictionaryApi = {
  search: (params: WordSearchParams, signal?: AbortSignal) =>
    api.get<PagedResponse<LexicalUnit>>('/dictionary/words', { query: { ...params }, signal }),
  word: (id: string) => api.get<LexicalUnit>(`/dictionary/words/${id}`),
  similar: (id: string) => api.get<SimilarWord[]>(`/dictionary/words/similar/${id}`),
  wordOfTheDay: () => api.get<WordOfTheDay>('/dictionary/word-of-the-day'),
  myWords: (params: { page?: number; pageSize?: number; state?: string }, signal?: AbortSignal) =>
    api.get<PagedResponse<UserWord>>('/dictionary/my-words', { query: { ...params }, signal }),
  addMyWord: (lexicalUnitId: string) => api.post<UserWord>(`/dictionary/my-words/${lexicalUnitId}`),
  removeMyWord: (lexicalUnitId: string) => api.delete<void>(`/dictionary/my-words/${lexicalUnitId}`),
}

export const practiceApi = {
  decks: () => api.get<Deck[]>('/decks'),
  createDeck: (name: string, description?: string) => api.post<Deck>('/decks', { name, description }),
  renameDeck: (id: string, name: string) => api.patch<Deck>(`/decks/${id}`, { name }),
  deleteDeck: (id: string) => api.delete<void>(`/decks/${id}`),
  addCards: (deckId: string, lexicalUnitIds: string[]) =>
    api.post<{ added: number; requested: number; deckSize: number }>(`/decks/${deckId}/cards`, {
      lexicalUnitIds,
    }),
  removeCard: (deckId: string, lexicalUnitId: string) =>
    api.delete<void>(`/decks/${deckId}/cards/${lexicalUnitId}`),
  due: (limit = 20, deckId?: string) => api.get<DueCards>('/practice/due', { query: { limit, deckId } }),
  dueCount: () => api.get<{ due: number }>('/practice/due/count'),
  forecast: (deckId?: string) => api.get<PracticeSummary>('/practice/forecast', { query: { deckId } }),
  submitReviews: (reviews: { reviewCardId: string; rating: ReviewRating; durationMs: number }[], sessionNewCards = 0) =>
    api.post<ReviewResult[]>('/practice/reviews', { reviews, sessionNewCards }),
  summary: () => api.post<PracticeSummary>('/practice/summary'),
  suspend: (reviewCardId: string) => api.post<void>(`/practice/suspend/${reviewCardId}`),
}

export const statsApi = {
  dashboard: (activityDays = 14) =>
    api.get<Dashboard>('/users/me/dashboard', { query: { activityDays } }),
  goal: () => api.get<GoalProgress>('/users/me/goal'),
  activity: (days = 30) => api.get<ActivityDay[]>('/users/me/stats', { query: { days } }),
}

export const adminApi = {
  courses: (languageId?: string, includeUnpublished = true) =>
    api.get<AdminCourse[]>('/admin/courses', { query: { languageId, includeUnpublished } }),
  course: (id: string) => api.get<AdminCourse>(`/admin/courses/${id}`),
  createCourse: (data: {
    slug: string
    title: string
    description?: string
    level: CefrLevel
    languageId: string
    coverUrl?: string
    accentColor?: string
    estimatedMinutes: number
    isPublished: boolean
    sortOrder: number
  }) => api.post<AdminCourse>('/admin/courses', data),
  updateCourse: (id: string, data: Partial<AdminCourse>) => api.put<AdminCourse>(`/admin/courses/${id}`, data),
  deleteCourse: (id: string) => api.delete<void>(`/admin/courses/${id}`),
  lessons: (courseId: string) => api.get<AdminLesson[]>(`/admin/courses/${courseId}/lessons`),
  lesson: (id: string) => api.get<AdminLesson>(`/admin/lessons/${id}`),
  createLesson: (data: {
    courseId: string
    slug: string
    title: string
    summary?: string
    contentMarkdown?: string
    sortOrder: number
    estimatedMinutes: number
    isPublished: boolean
    grammarTopicId?: string
    keyVocabulary?: string[]
  }) => api.post<AdminLesson>('/admin/lessons', data),
  updateLesson: (id: string, data: Partial<AdminLesson>) => api.put<AdminLesson>(`/admin/lessons/${id}`, data),
  deleteLesson: (id: string) => api.delete<void>(`/admin/lessons/${id}`),
  generateLesson: (id: string, data: { topic?: string; level?: CefrLevel; requirements?: string }) =>
    api.post<GenerateLessonContent>(`/admin/lessons/${id}/generate`, data),
  generateCourse: (id: string, data: { topic?: string; level?: CefrLevel; lessonsCount: number }) =>
    api.post<GenerateCourseContent>(`/admin/courses/${id}/generate`, data),
  aiSettings: () => api.get<AiSettings>('/admin/ai-settings'),
  updateAiSettings: (data: {
    defaultChatProvider?: string
    defaultEmbeddingProvider?: string
    defaultChatModel?: string
    temperature?: number
    maxOutputTokens?: number
    providers?: Array<{
      name: string
      kind?: string
      baseUrl?: string
      apiKey?: string
      chatModel?: string
      embeddingModel?: string
      ttsModel?: string
      sttModel?: string
      enabled?: boolean
      timeoutSeconds?: number
      requestsPerMinute?: number
    }>
  }) => api.put<AiSettings>('/admin/ai-settings', data),
  testAi: (provider?: string) => api.post<{ healthy: boolean; provider: string; latencyMs: number; error?: string }>('/admin/ai-settings/test', { provider }),
}

export type { ReviewCard }
