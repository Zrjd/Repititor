import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useI18n } from '../i18n'
import { useAuth } from '../auth/AuthContext'
import { adminApi, catalogApi } from '../api/endpoints'
import { ApiError } from '../api/client'
import type { AdminCourse, AdminLesson, CefrLevel } from '../api/types'
import { ErrorState, Spinner } from '../components/Feedback'
import { PageHeader } from '../components/PageHeader'
import { GroupsTab } from './GroupsTab'

type Tab = 'courses' | 'lessons' | 'groups' | 'ai'

export function AdminPage() {
  const { t } = useI18n()
  const { user } = useAuth()
  // Настройки ИИ глобальные и содержат ключи провайдеров, поэтому доступны только администратору.
  const isAdmin = user?.role === 'Admin'
  const [tab, setTab] = useState<Tab>('courses')

  return (
    <>
      <PageHeader title={t('admin.title')} subtitle={t('admin.subtitle')} />
      <div className="admin-tabs">
        {(['courses', 'lessons', 'groups', 'ai'] as Tab[])
          .filter((key) => key !== 'ai' || isAdmin)
          .map((key) => (
            <button
              key={key}
              type="button"
              className={`admin-tab${tab === key ? ' admin-tab--active' : ''}`}
              onClick={() => setTab(key)}
            >
              {key === 'groups' ? t('groups.tabGroups') : t(`admin.tab${key.charAt(0).toUpperCase() + key.slice(1)}` as 'admin.tabCourses')}
            </button>
          ))}
      </div>
      {tab === 'courses' ? <CoursesTab /> : null}
      {tab === 'lessons' ? <LessonsTab /> : null}
      {tab === 'groups' ? <GroupsTab /> : null}
      {tab === 'ai' && isAdmin ? <AiTab /> : null}
    </>
  )
}

function CoursesTab() {
  const { t, plural } = useI18n()
  const qc = useQueryClient()
  const [editing, setEditing] = useState<AdminCourse | null>(null)
  const [creating, setCreating] = useState(false)

  const courses = useQuery({ queryKey: ['admin-courses'], queryFn: () => adminApi.courses() })
  const languages = useQuery({ queryKey: ['languages'], queryFn: () => catalogApi.languages() })

  const createMutation = useMutation({
    mutationFn: (data: Parameters<typeof adminApi.createCourse>[0]) => adminApi.createCourse(data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-courses'] })
      setCreating(false)
    },
  })

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: Parameters<typeof adminApi.updateCourse>[1] }) =>
      adminApi.updateCourse(id, data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-courses'] })
      setEditing(null)
    },
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => adminApi.deleteCourse(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-courses'] }),
  })

  if (courses.isPending || languages.isPending) return <Spinner />
  if (courses.isError) return <ErrorState error={courses.error} onRetry={() => courses.refetch()} />

  return (
    <div className="admin-section">
      <div className="admin-toolbar">
        <h2>{t('admin.coursesTitle')}</h2>
        <button type="button" className="button button--primary" onClick={() => setCreating(true)}>
          {t('admin.createCourse')}
        </button>
      </div>

      {creating ? (
        <CourseForm
          languages={languages.data ?? []}
          onCancel={() => setCreating(false)}
          onSubmit={(data) => createMutation.mutate(data)}
          pending={createMutation.isPending}
        />
      ) : null}

      {editing ? (
        <CourseForm
          course={editing}
          languages={languages.data ?? []}
          onCancel={() => setEditing(null)}
          onSubmit={(data) => updateMutation.mutate({ id: editing.id, data })}
          pending={updateMutation.isPending}
        />
      ) : null}

      <div className="admin-list">
        {courses.data.map((course) => (
          <div key={course.id} className="card admin-row">
            <div className="admin-row__body">
              <strong>{course.title}</strong>
              <span className="muted">
                {course.languageCode} · {course.level} · {course.lessonsCount} {plural('lesson', course.lessonsCount)}
                {course.isPublished ? '' : ` · ${t('admin.coursePublished')}: ✗`}
              </span>
            </div>
            <div className="admin-row__actions">
              <button type="button" className="button button--ghost" onClick={() => setEditing(course)}>
                {t('admin.editCourse')}
              </button>
              <button
                type="button"
                className="button button--danger"
                onClick={() => {
                  if (window.confirm(t('admin.deleteCourseConfirm', { name: course.title }))) {
                    deleteMutation.mutate(course.id)
                  }
                }}
              >
                {t('admin.deleteCourse')}
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

function CourseForm({
  course,
  languages,
  onCancel,
  onSubmit,
  pending,
}: {
  course?: AdminCourse
  languages: Array<{ id: string; code: string; nameEnglish: string }>
  onCancel: () => void
  onSubmit: (data: Parameters<typeof adminApi.createCourse>[0]) => void
  pending: boolean
}) {
  const { t } = useI18n()
  const [title, setTitle] = useState(course?.title ?? '')
  const [slug, setSlug] = useState(course?.slug ?? '')
  const [description, setDescription] = useState(course?.description ?? '')
  const [level, setLevel] = useState<CefrLevel>(course?.level ?? 'A1')
  const [languageId, setLanguageId] = useState(course?.languageId ?? languages[0]?.id ?? '')
  const [estimatedMinutes, setEstimatedMinutes] = useState(course?.estimatedMinutes ?? 60)
  const [isPublished, setIsPublished] = useState(course?.isPublished ?? true)
  const [sortOrder, setSortOrder] = useState(course?.sortOrder ?? 0)

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    onSubmit({
      title,
      slug,
      description: description || undefined,
      level,
      languageId,
      estimatedMinutes,
      isPublished,
      sortOrder,
    })
  }

  return (
    <form className="card admin-form" onSubmit={handleSubmit}>
      <h3>{course ? t('admin.editCourse') : t('admin.createCourse')}</h3>
      <label>
        {t('admin.courseTitle')}
        <input value={title} onChange={(e) => setTitle(e.target.value)} required minLength={2} />
      </label>
      <label>
        {t('admin.courseSlug')}
        <input value={slug} onChange={(e) => setSlug(e.target.value)} required minLength={2} />
      </label>
      <label>
        {t('admin.courseDescription')}
        <textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={3} />
      </label>
      <div className="admin-form__grid">
        <label>
          {t('admin.courseLevel')}
          <select value={level} onChange={(e) => setLevel(e.target.value as CefrLevel)}>
            {(['A1', 'A2', 'B1', 'B2', 'C1', 'C2'] as CefrLevel[]).map((l) => (
              <option key={l} value={l}>{l}</option>
            ))}
          </select>
        </label>
        <label>
          {t('admin.courseLanguage')}
          <select value={languageId} onChange={(e) => setLanguageId(e.target.value)}>
            {languages.map((l) => (
              <option key={l.id} value={l.id}>{l.nameEnglish}</option>
            ))}
          </select>
        </label>
        <label>
          {t('admin.courseMinutes')}
          <input
            type="number"
            value={estimatedMinutes}
            onChange={(e) => setEstimatedMinutes(Number(e.target.value))}
            min={1}
            max={10000}
          />
        </label>
        <label>
          {t('admin.courseSortOrder')}
          <input
            type="number"
            value={sortOrder}
            onChange={(e) => setSortOrder(Number(e.target.value))}
            min={0}
            max={10000}
          />
        </label>
      </div>
      <label className="admin-form__checkbox">
        <input type="checkbox" checked={isPublished} onChange={(e) => setIsPublished(e.target.checked)} />
        {t('admin.coursePublished')}
      </label>
      <div className="admin-form__actions">
        <button type="submit" className="button button--primary" disabled={pending}>
          {t('common.save')}
        </button>
        <button type="button" className="button button--ghost" onClick={onCancel}>
          {t('common.cancel')}
        </button>
      </div>
    </form>
  )
}

function LessonsTab() {
  const { t } = useI18n()
  const qc = useQueryClient()
  const [editing, setEditing] = useState<AdminLesson | null>(null)
  const [creating, setCreating] = useState(false)
  const [selectedCourseId, setSelectedCourseId] = useState<string>('')
  const [generatingFor, setGeneratingFor] = useState<string | null>(null)
  const [generateTopic, setGenerateTopic] = useState('')
  const [generateDuration, setGenerateDuration] = useState<number | undefined>(undefined)
  const [generateSummary, setGenerateSummary] = useState('')

  const courses = useQuery({ queryKey: ['admin-courses'], queryFn: () => adminApi.courses() })
  const lessons = useQuery({
    queryKey: ['admin-lessons', selectedCourseId],
    queryFn: () => adminApi.lessons(selectedCourseId),
    enabled: !!selectedCourseId,
    // Пока идёт хотя бы одна генерация, список обновляется сам.
    refetchInterval: (query) => {
      const data = query.state.data as AdminLesson[] | undefined
      return data?.some((lesson) => isGenerationActive(lesson)) ? 2000 : false
    },
  })

  const createMutation = useMutation({
    mutationFn: (data: Parameters<typeof adminApi.createLesson>[0]) => adminApi.createLesson(data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-lessons'] })
      setCreating(false)
    },
  })

  const updateMutation = useMutation({
    mutationFn: ({ id, data }: { id: string; data: Parameters<typeof adminApi.updateLesson>[1] }) =>
      adminApi.updateLesson(id, data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-lessons'] })
      setEditing(null)
    },
  })

  const deleteMutation = useMutation({
    mutationFn: (id: string) => adminApi.deleteLesson(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-lessons'] }),
  })

  const generateMutation = useMutation({
    mutationFn: (id: string) => adminApi.generateLesson(id, {
      topic: generateTopic || undefined,
      durationMinutes: generateDuration,
      summary: generateSummary || undefined,
    }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin-lessons'] })
      setGeneratingFor(null)
      setGenerateTopic('')
      setGenerateSummary('')
      setGenerateDuration(undefined)
    },
    onError: (error) => {
      // 409: генерация уже идёт — это не ошибка, просто показываем актуальный статус.
      if (!(error instanceof ApiError) || error.status !== 409) {
        console.error(error)
      }
      qc.invalidateQueries({ queryKey: ['admin-lessons'] })
      setGeneratingFor(null)
    },
  })

  if (courses.isPending) return <Spinner />

  return (
    <div className="admin-section">
      <div className="admin-toolbar">
        <h2>{t('admin.lessonsTitle')}</h2>
        <div className="admin-toolbar__filters">
          <select
            value={selectedCourseId}
            onChange={(e) => setSelectedCourseId(e.target.value)}
          >
            <option value="">{t('admin.courseLanguage')}</option>
            {courses.data?.map((c) => (
              <option key={c.id} value={c.id}>{c.title}</option>
            ))}
          </select>
          <button
            type="button"
            className="button button--primary"
            onClick={() => setCreating(true)}
            disabled={!selectedCourseId}
          >
            {t('admin.createLesson')}
          </button>
        </div>
      </div>

      {creating && selectedCourseId ? (
        <LessonForm
          courseId={selectedCourseId}
          onCancel={() => setCreating(false)}
          onSubmit={(data) => createMutation.mutate(data)}
          pending={createMutation.isPending}
        />
      ) : null}

      {editing ? (
        <LessonForm
          lesson={editing}
          onCancel={() => setEditing(null)}
          onSubmit={(data) => updateMutation.mutate({ id: editing.id, data })}
          pending={updateMutation.isPending}
        />
      ) : null}

      {selectedCourseId && lessons.isPending ? <Spinner /> : null}
      {selectedCourseId && lessons.isError ? (
        <ErrorState error={lessons.error} onRetry={() => lessons.refetch()} />
      ) : null}

      <div className="admin-list">
        {lessons.data?.map((lesson) => (
          <div key={lesson.id} className="card admin-row">
            <div className="admin-row__body">
              <strong>{lesson.title}</strong>
              <span className="muted">
                {lesson.sortOrder} · {lesson.estimatedMinutes} {t('common.minutes')}
              </span>
              <div className="admin-badges">
                <GenerationBadge lesson={lesson} />
                {!lesson.isPublished ? (
                  <span className="badge badge--muted">{t('admin.lessonUnpublished')}</span>
                ) : !lesson.isAvailableToStudents ? (
                  <span className="badge badge--draft">{t('admin.lessonDraft')}</span>
                ) : null}
              </div>
              {lesson.aiGenerationStatus === 'Failed' && lesson.aiGenerationError ? (
                <span className="muted admin-row__error">{lesson.aiGenerationError}</span>
              ) : null}
            </div>
            <div className="admin-row__actions">
              <button
                type="button"
                className="button button--accent"
                disabled={isGenerationActive(lesson)}
                onClick={() => setGeneratingFor(generatingFor === lesson.id ? null : lesson.id)}
              >
                {isGenerationActive(lesson) ? t('admin.generationInProgress') : t('admin.generateWithAi')}
              </button>
              <button type="button" className="button button--ghost" onClick={() => setEditing(lesson)}>
                {t('admin.editLesson')}
              </button>
              <button
                type="button"
                className="button button--danger"
                onClick={() => {
                  if (window.confirm(t('admin.deleteLessonConfirm', { name: lesson.title }))) {
                    deleteMutation.mutate(lesson.id)
                  }
                }}
              >
                {t('admin.deleteLesson')}
              </button>
            </div>
            {generatingFor === lesson.id ? (
              <div className="admin-form__ai">
                <label>
                  {t('admin.generateLessonTitle')}
                  <input
                    value={generateTopic}
                    onChange={(e) => setGenerateTopic(e.target.value)}
                    placeholder={t('admin.generateLessonHint')}
                  />
                </label>
                <label>
                  {t('admin.generateLessonDuration')}
                  <input
                    type="number"
                    min={5}
                    max={180}
                    value={generateDuration ?? ''}
                    onChange={(e) => setGenerateDuration(e.target.value ? Number(e.target.value) : undefined)}
                    placeholder={t('admin.generateLessonDurationHint')}
                  />
                </label>
                <label>
                  {t('admin.generateLessonSummary')}
                  <input
                    value={generateSummary}
                    onChange={(e) => setGenerateSummary(e.target.value)}
                    placeholder={t('admin.generateLessonSummaryHint')}
                  />
                </label>
                <button
                  type="button"
                  className="button button--primary"
                  disabled={generateMutation.isPending}
                  onClick={() => generateMutation.mutate(lesson.id)}
                >
                  {generateMutation.isPending ? t('admin.generating') : t('admin.generationEnqueue')}
                </button>
              </div>
            ) : null}
          </div>
        ))}
      </div>
    </div>
  )
}

function isGenerationActive(lesson: AdminLesson) {
  return lesson.aiGenerationStatus === 'Queued' || lesson.aiGenerationStatus === 'Running'
}

function GenerationBadge({ lesson }: { lesson: AdminLesson }) {
  const { t } = useI18n()

  switch (lesson.aiGenerationStatus) {
    case 'Queued':
    case 'Running':
      return <span className="badge badge--progress">{t('admin.generationInProgress')}</span>
    case 'Completed':
      return <span className="badge badge--success">{t('admin.generationCompleted')}</span>
    case 'Failed':
      return <span className="badge badge--danger">{t('admin.generationFailed')}</span>
    default:
      return null
  }
}

function LessonForm({
  lesson,
  courseId,
  onCancel,
  onSubmit,
  pending,
}: {
  lesson?: AdminLesson
  courseId?: string
  onCancel: () => void
  onSubmit: (data: Parameters<typeof adminApi.createLesson>[0]) => void
  pending: boolean
}) {
  const { t } = useI18n()
  const [title, setTitle] = useState(lesson?.title ?? '')
  const [slug, setSlug] = useState(lesson?.slug ?? '')
  const [summary, setSummary] = useState(lesson?.summary ?? '')
  const [contentMarkdown, setContentMarkdown] = useState(lesson?.contentMarkdown ?? '')
  const [estimatedMinutes, setEstimatedMinutes] = useState(lesson?.estimatedMinutes ?? 10)
  const [isPublished, setIsPublished] = useState(lesson?.isPublished ?? true)
  const [sortOrder, setSortOrder] = useState(lesson?.sortOrder ?? 0)
  const [keyVocabulary, setKeyVocabulary] = useState((lesson?.keyVocabulary ?? []).join(', '))

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    onSubmit({
      courseId: lesson?.courseId ?? courseId!,
      title,
      slug,
      summary: summary || undefined,
      contentMarkdown: contentMarkdown || undefined,
      estimatedMinutes,
      isPublished,
      sortOrder,
      keyVocabulary: keyVocabulary
        .split(',')
        .map((s) => s.trim())
        .filter(Boolean),
    })
  }

  return (
    <form className="card admin-form" onSubmit={handleSubmit}>
      <h3>{lesson ? t('admin.editLesson') : t('admin.createLesson')}</h3>

      <label>
        {t('admin.lessonTitle')}
        <input value={title} onChange={(e) => setTitle(e.target.value)} required minLength={2} />
      </label>
      <label>
        {t('admin.lessonSlug')}
        <input value={slug} onChange={(e) => setSlug(e.target.value)} required minLength={2} />
      </label>
      <label>
        {t('admin.lessonSummary')}
        <textarea value={summary} onChange={(e) => setSummary(e.target.value)} rows={2} />
      </label>
      <label>
        {t('admin.lessonContent')}
        <textarea value={contentMarkdown} onChange={(e) => setContentMarkdown(e.target.value)} rows={10} />
      </label>
      <label>
        {t('admin.lessonVocabulary')}
        <input value={keyVocabulary} onChange={(e) => setKeyVocabulary(e.target.value)} />
      </label>
      <div className="admin-form__grid">
        <label>
          {t('admin.lessonMinutes')}
          <input
            type="number"
            value={estimatedMinutes}
            onChange={(e) => setEstimatedMinutes(Number(e.target.value))}
            min={1}
            max={10000}
          />
        </label>
        <label>
          {t('admin.lessonSortOrder')}
          <input
            type="number"
            value={sortOrder}
            onChange={(e) => setSortOrder(Number(e.target.value))}
            min={0}
            max={10000}
          />
        </label>
      </div>
      <label className="admin-form__checkbox">
        <input type="checkbox" checked={isPublished} onChange={(e) => setIsPublished(e.target.checked)} />
        {t('admin.lessonPublished')}
      </label>
      <div className="admin-form__actions">
        <button type="submit" className="button button--primary" disabled={pending}>
          {t('common.save')}
        </button>
        <button type="button" className="button button--ghost" onClick={onCancel}>
          {t('common.cancel')}
        </button>
      </div>
    </form>
  )
}

function AiTab() {
  const { t } = useI18n()
  const qc = useQueryClient()
  const settings = useQuery({ queryKey: ['ai-settings'], queryFn: () => adminApi.aiSettings() })

  const saveMutation = useMutation({
    mutationFn: (data: Parameters<typeof adminApi.updateAiSettings>[0]) => adminApi.updateAiSettings(data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['ai-settings'] })
    },
  })

  const testMutation = useMutation({
    mutationFn: (provider?: string) => adminApi.testAi(provider),
  })

  if (settings.isPending) return <Spinner />
  if (settings.isError) return <ErrorState error={settings.error} onRetry={() => settings.refetch()} />

  const data = settings.data

  return (
    <div className="admin-section">
      <h2>{t('admin.aiSettingsTitle')}</h2>
      <form
        className="card admin-form"
        onSubmit={(e) => {
          e.preventDefault()
          const form = e.target as HTMLFormElement
          const fd = new FormData(form)
          saveMutation.mutate({
            defaultChatProvider: fd.get('defaultChatProvider') as string,
            defaultEmbeddingProvider: fd.get('defaultEmbeddingProvider') as string,
            defaultChatModel: fd.get('defaultChatModel') as string,
            temperature: Number(fd.get('temperature')),
            maxOutputTokens: Number(fd.get('maxOutputTokens')),
            lessonPrompt: fd.get('lessonPrompt') as string,
            providers: data.providers.map((p) => ({
              name: p.name,
              kind: p.kind,
              baseUrl: fd.get(`provider_${p.name}_baseUrl`) as string,
              apiKey: fd.get(`provider_${p.name}_apiKey`) as string,
              chatModel: fd.get(`provider_${p.name}_chatModel`) as string,
              embeddingModel: fd.get(`provider_${p.name}_embeddingModel`) as string,
              ttsModel: fd.get(`provider_${p.name}_ttsModel`) as string,
              sttModel: fd.get(`provider_${p.name}_sttModel`) as string,
              enabled: fd.get(`provider_${p.name}_enabled`) === 'on',
              timeoutSeconds: Number(fd.get(`provider_${p.name}_timeout`)),
              requestsPerMinute: Number(fd.get(`provider_${p.name}_rpm`)),
            })),
          })
        }}
      >
        <div className="admin-form__grid">
          <label>
            {t('admin.aiDefaultChat')}
            <select name="defaultChatProvider" defaultValue={data.defaultChatProvider}>
              {data.providers.map((p) => (
                <option key={p.name} value={p.name}>{p.name}</option>
              ))}
            </select>
          </label>
          <label>
            {t('admin.aiDefaultEmbedding')}
            <select name="defaultEmbeddingProvider" defaultValue={data.defaultEmbeddingProvider}>
              {data.providers.map((p) => (
                <option key={p.name} value={p.name}>{p.name}</option>
              ))}
            </select>
          </label>
          <label>
            {t('admin.aiDefaultModel')}
            <input name="defaultChatModel" defaultValue={data.defaultChatModel} />
          </label>
          <label>
            {t('admin.aiTemperature')}
            <input name="temperature" type="number" step="0.1" min="0" max="2" defaultValue={data.temperature} />
          </label>
          <label>
            {t('admin.aiMaxTokens')}
            <input name="maxOutputTokens" type="number" min="64" max="8000" defaultValue={data.maxOutputTokens} />
          </label>
        </div>

        <label>
          {t('admin.aiLessonPrompt')}
          <textarea
            name="lessonPrompt"
            rows={8}
            defaultValue={data.lessonPrompt}
            placeholder={t('admin.aiLessonPromptHint')}
          />
        </label>

        <h3>{t('admin.aiProvider')}</h3>
        {data.providers.map((p) => (
          <fieldset key={p.name} className="admin-provider">
            <legend>{p.name}</legend>
            <div className="admin-form__grid">
              <label>
                {t('admin.aiBaseUrl')}
                <input name={`provider_${p.name}_baseUrl`} defaultValue={p.baseUrl} />
              </label>
              <label>
                {t('admin.aiApiKey')}
                <input name={`provider_${p.name}_apiKey`} type="password" defaultValue={p.apiKey ?? ''} />
              </label>
              <label>
                {t('admin.aiChatModel')}
                <input name={`provider_${p.name}_chatModel`} defaultValue={p.chatModel} />
              </label>
              <label>
                {t('admin.aiEmbeddingModel')}
                <input name={`provider_${p.name}_embeddingModel`} defaultValue={p.embeddingModel} />
              </label>
              <label>
                {t('admin.aiEnabled')}
                <input name={`provider_${p.name}_enabled`} type="checkbox" defaultChecked={p.enabled} />
              </label>
              <label>
                Timeout
                <input name={`provider_${p.name}_timeout`} type="number" min={5} max={600} defaultValue={p.timeoutSeconds} />
              </label>
              <label>
                RPM
                <input name={`provider_${p.name}_rpm`} type="number" min={1} max={1000} defaultValue={p.requestsPerMinute} />
              </label>
            </div>
            <button
              type="button"
              className="button button--ghost"
              disabled={testMutation.isPending}
              onClick={() => testMutation.mutate(p.name)}
            >
              {testMutation.isPending ? t('admin.aiTesting') : t('admin.aiTest')}
            </button>
            {testMutation.data && testMutation.data.provider === p.name ? (
              <p className={testMutation.data.healthy ? 'text-success' : 'text-error'}>
                {testMutation.data.healthy
                  ? `${t('admin.aiTestOk')} (${testMutation.data.latencyMs}ms)`
                  : `${t('admin.aiTestFail')}: ${testMutation.data.error}`}
              </p>
            ) : null}
          </fieldset>
        ))}

        <div className="admin-form__actions">
          <button type="submit" className="button button--primary" disabled={saveMutation.isPending}>
            {t('admin.saveSettings')}
          </button>
          {saveMutation.isSuccess ? <span className="text-success">{t('admin.saved')}</span> : null}
        </div>
      </form>
    </div>
  )
}
