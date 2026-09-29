export type CefrLevel = 'A1' | 'A2' | 'B1' | 'B2' | 'C1' | 'C2'
export type UserRole = 'Learner' | 'Teacher' | 'Admin'
export type SignupRole = Exclude<UserRole, 'Admin'>
export type CardState = 'New' | 'Learning' | 'Review' | 'Relearning' | 'Mastered'
export type ReviewRating = 'Again' | 'Hard' | 'Good' | 'Easy'
export type LessonStatus = 'NotStarted' | 'InProgress' | 'Completed'

export interface PagedResponse<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
  totalPages: number
}

export interface UserProfile {
  id: string
  email: string
  displayName: string
  role: UserRole
  avatarUrl?: string | null
  targetLanguageId: string
  targetLanguageCode: string
  targetLanguageName: string
  interfaceLanguageId: string
  level: CefrLevel
  targetLevel: CefrLevel
  dailyGoalXp: number
  speechRate: number
  totalXp: number
  currentStreak: number
  longestStreak: number
  preferredAiProvider?: string | null
  emailConfirmed: boolean
  createdAt: string
}

export interface AuthResponse {
  accessToken: string
  refreshToken: string
  tokenType: string
  expiresAt: string
  expiresInSeconds: number
  user: UserProfile
}

export interface Language {
  id: string
  code: string
  nameEnglish: string
  nameRussian: string
  nativeName: string
  flagEmoji: string
  isEnabled: boolean
}

export interface Course {
  id: string
  slug: string
  title: string
  description?: string | null
  languageId: string
  languageCode: string
  level: CefrLevel
  lessonsCount: number
  estimatedMinutes: number
  progressPercent: number
  accentColor?: string | null
  isEnrolled: boolean
}

export interface LessonSummary {
  id: string
  courseId: string
  slug: string
  title: string
  summary?: string | null
  sortOrder: number
  status: LessonStatus
  progressPercent: number
  bestScorePercent?: number | null
  estimatedMinutes: number
  keyVocabulary: string[]
}

export interface Lesson extends LessonSummary {
  contentMarkdown?: string | null
}

export interface GrammarTopic {
  id: string
  slug: string
  title: string
  summary?: string | null
  explanationMarkdown?: string | null
  minLevel: CefrLevel
}

export interface LexicalUnit {
  id: string
  languageId: string
  translationLanguageId: string
  text: string
  transcription?: string | null
  translation?: string | null
  alternativeTranslations?: string[] | null
  partOfSpeech?: string | null
  gender?: string | null
  pluralForm?: string | null
  pastTense?: string | null
  audioUrl?: string | null
  exampleTarget?: string | null
  exampleNative?: string | null
  notes?: string | null
  tags?: string[] | null
  minLearnerLevel: CefrLevel
  frequencyRank: number
  status: string
  createdAt: string
  updatedAt: string
}

export interface SimilarWord extends LexicalUnit {
  semanticSimilarity?: number | null
}

export interface UserWord {
  userLexicalUnitId: string
  lexicalUnitId: string
  text: string
  translation?: string | null
  transcription?: string | null
  audioUrl?: string | null
  partOfSpeech?: string | null
  exampleTarget?: string | null
  state: CardState
  suspended: boolean
  masteryScore: number
  correctStreak: number
  incorrectStreak: number
  personalNote?: string | null
  source: string
  addedAt: string
  lastReviewedAt?: string | null
}

export interface WordOfTheDay {
  word: LexicalUnit
  reason?: string | null
  audioUrl?: string | null
}

export interface Deck {
  id: string
  name: string
  description?: string | null
  languageCode?: string | null
  coverEmoji?: string | null
  tags?: string[] | null
  isArchived: boolean
  cardsCount: number
  dueCount: number
  newCount: number
  masteredCount: number
  createdAt: string
  updatedAt: string
}

export interface ReviewCard {
  reviewCardId: string
  lexicalUnitId: string
  text: string
  translation?: string | null
  transcription?: string | null
  audioUrl?: string | null
  partOfSpeech?: string | null
  exampleTarget?: string | null
  state: CardState
  dueAt: string
  intervalDays: number
  repetitions: number
}

export interface DueCards {
  cards: ReviewCard[]
  totalDue: number
  limit: number
}

export interface ReviewResult {
  reviewCardId: string
  state: CardState
  dueAt: string
  intervalDays: number
  easeFactor: number
  wasCorrect: boolean
  masteryScore: number
  correctTranslation?: string | null
  xpEarned: number
}

export interface ForecastDay {
  date: string
  cards: number
}

export interface PracticeSummary {
  reviewed: number
  correct: number
  again: number
  newCardsSeen: number
  xpEarned: number
  accuracyPercent: number
  remainingDue: number
  forecast: ForecastDay[]
}

export interface ActivityDay {
  date: string
  xpEarned: number
  reviewsCompleted: number
  correctAnswers: number
  newWordsLearned: number
  exercisesCompleted: number
  minutesStudied: number
}

export interface DueWord {
  lexicalUnitId: string
  text: string
  translation?: string | null
  transcription?: string | null
  audioUrl?: string | null
  state: CardState
  dueInSeconds: number
}

export interface Dashboard {
  user: UserProfile
  cardsDue: number
  newCards: number
  totalWords: number
  masteredWords: number
  recommendedWords: DueWord[]
  reviewsToday: number
  exercisesDone7d: number
  xpToday: number
  dailyGoalXp: number
  currentStreak: number
  streakLast14: number[]
  activity: ActivityDay[]
}

export interface GoalProgress {
  dailyGoalXp: number
  xpToday: number
  reviewsToday: number
  goalReached: boolean
  currentStreak: number
  longestStreak: number
}

export interface AdminCourse {
  id: string
  slug: string
  title: string
  description?: string | null
  level: CefrLevel
  languageId: string
  languageCode: string
  coverUrl?: string | null
  accentColor?: string | null
  estimatedMinutes: number
  isPublished: boolean
  sortOrder: number
  lessonsCount: number
  createdAt: string
  ownerDisplayName?: string | null
}

export interface StudyGroup {
  id: string
  name: string
  description?: string | null
  membersCount: number
  coursesCount: number
  isPersonal: boolean
  createdAt: string
}

export interface StudyGroupMember {
  userId: string
  displayName: string
  email: string
  joinedAt: string
}

export interface StudyGroupCourse {
  courseId: string
  slug: string
  title: string
  level: CefrLevel
  languageCode: string
  isPublished: boolean
  isOwnCourse: boolean
  assignedAt: string
}

export interface GroupInvitation {
  id: string
  code: string
  expiresAt: string
  maxUses: number
  usedCount: number
  isRevoked: boolean
  isActive: boolean
}

export interface StudyGroupDetail {
  id: string
  name: string
  description?: string | null
  teacherUserId: string
  isPersonal: boolean
  createdAt: string
  members: StudyGroupMember[]
  courses: StudyGroupCourse[]
  invitations: GroupInvitation[]
}

export interface LearnerGroup {
  id: string
  name: string
  description?: string | null
  teacherDisplayName: string
  membersCount: number
  courses: StudyGroupCourse[]
}

export type LessonGenerationStatus = 'None' | 'Queued' | 'Running' | 'Completed' | 'Failed'

export interface LessonGenerationState {
  lessonId: string
  status: LessonGenerationStatus
  requestedAt?: string | null
  completedAt?: string | null
  error?: string | null
  isActive: boolean
}

export interface AdminLesson {
  id: string
  courseId: string
  slug: string
  title: string
  summary?: string | null
  contentMarkdown?: string | null
  sortOrder: number
  estimatedMinutes: number
  isPublished: boolean
  grammarTopicId?: string | null
  keyVocabulary?: string[] | null
  aiGenerationStatus: LessonGenerationStatus
  aiGenerationRequestedAt?: string | null
  aiGenerationCompletedAt?: string | null
  aiGenerationError?: string | null
  isAvailableToStudents: boolean
}

export interface AiProviderSettings {
  name: string
  kind: string
  baseUrl: string
  apiKey?: string | null
  chatModel: string
  embeddingModel: string
  ttsModel?: string | null
  sttModel?: string | null
  enabled: boolean
  timeoutSeconds: number
  requestsPerMinute: number
}

export interface AiSettings {
  defaultChatProvider: string
  defaultEmbeddingProvider: string
  defaultChatModel: string
  temperature: number
  maxOutputTokens: number
  lessonPrompt: string
  providers: AiProviderSettings[]
}

export interface GenerateLessonContent {
  title: string
  summary?: string | null
  contentMarkdown: string
  keyVocabulary: string[]
  provider: string
  model: string
  inputTokens: number
  outputTokens: number
}

export interface GenerateCourseContent {
  description: string
  lessonTitles: string[]
  provider: string
  model: string
  inputTokens: number
  outputTokens: number
}
