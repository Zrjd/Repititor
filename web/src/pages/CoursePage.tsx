import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { catalogApi } from '../api/endpoints'
import { EmptyState, ErrorState, ProgressBar, Spinner } from '../components/Feedback'
import { PageHeader } from '../components/PageHeader'
import { useI18n } from '../i18n'

export function CoursePage() {
  const { t, plural, formatNumber } = useI18n()
  const { slug = '' } = useParams()

  const course = useQuery({ queryKey: ['course', slug], queryFn: () => catalogApi.course(slug) })
  const lessons = useQuery({ queryKey: ['lessons', slug], queryFn: () => catalogApi.lessons(slug) })

  if (course.isPending || lessons.isPending) {
    return <Spinner />
  }
  if (course.isError) {
    return <ErrorState error={course.error} onRetry={() => course.refetch()} />
  }
  if (lessons.isError) {
    return <ErrorState error={lessons.error} onRetry={() => lessons.refetch()} />
  }

  const data = course.data

  return (
    <>
      <PageHeader
        title={data.title}
        subtitle={data.description ?? undefined}
        actions={
          <div className="course-card__head">
            <span className="badge badge--level">{data.level}</span>
            {data.isEnrolled ? <span className="badge badge--success">{t('catalog.enrolled')}</span> : null}
          </div>
        }
      />

      <section className="card">
        <p className="muted">
          {formatNumber(data.lessonsCount)} {plural('lesson', data.lessonsCount)} ·{' '}
          {formatNumber(data.estimatedMinutes)} {t('common.minutes')}
        </p>
        <p>{t('catalog.progress')}</p>
        <ProgressBar value={data.progressPercent} />
      </section>

      <h2>{t('catalog.lessonsTitle')}</h2>
      {lessons.data.length === 0 ? <EmptyState title={t('common.empty')} /> : null}

      <ol className="lesson-list">
        {lessons.data.map((lesson) => (
          <li key={lesson.id} className="card lesson-row">
            <div className="lesson-row__order">{lesson.sortOrder}</div>
            <div className="lesson-row__body">
              <h3>
                <Link to={`/lessons/${lesson.id}`}>{lesson.title}</Link>
              </h3>
              {lesson.summary ? <p className="muted">{lesson.summary}</p> : null}
              <p className="lesson-row__meta">
                <span className={`badge badge--${lesson.status.toLowerCase()}`}>
                  {t(`catalog.lessonStatus.${lesson.status}` as 'catalog.lessonStatus.NotStarted')}
                </span>
                {formatNumber(lesson.estimatedMinutes)} {t('common.minutes')}
                {lesson.bestScorePercent !== null
                  ? ` · ${t('catalog.bestScore')}: ${lesson.bestScorePercent}%`
                  : ''}
              </p>
              {lesson.progressPercent > 0 ? <ProgressBar value={lesson.progressPercent} /> : null}
            </div>
          </li>
        ))}
      </ol>
    </>
  )
}

export function LessonPage() {
  const { t, formatNumber } = useI18n()
  const { lessonId = '' } = useParams()

  const lesson = useQuery({ queryKey: ['lesson', lessonId], queryFn: () => catalogApi.lesson(lessonId) })

  if (lesson.isPending) {
    return <Spinner />
  }
  if (lesson.isError) {
    return <ErrorState error={lesson.error} onRetry={() => lesson.refetch()} />
  }

  const data = lesson.data

  return (
    <>
      <PageHeader
        title={data.title}
        subtitle={data.summary ?? undefined}
        actions={
          <span className={`badge badge--${data.status.toLowerCase()}`}>
            {t(`catalog.lessonStatus.${data.status}` as 'catalog.lessonStatus.NotStarted')}
          </span>
        }
      />

      <section className="card">
        <p className="muted">
          {formatNumber(data.estimatedMinutes)} {t('common.minutes')}
          {data.progressPercent > 0 ? ` · ${t('catalog.progress')}: ${data.progressPercent}%` : ''}
        </p>
        {data.contentMarkdown ? (
          <article className="markdown">
            {data.contentMarkdown.split('\n\n').map((block, index) =>
              block.startsWith('#') ? (
                <h3 key={index}>{block.replace(/^#+\s*/, '')}</h3>
              ) : (
                <p key={index}>{block}</p>
              ),
            )}
          </article>
        ) : (
          <EmptyState title={t('common.empty')} />
        )}
      </section>

      {data.keyVocabulary.length > 0 ? (
        <section className="card">
          <h2>{t('nav.dictionary')}</h2>
          <div className="chip-list">
            {data.keyVocabulary.map((word) => (
              <Link className="chip" key={word} to={`/dictionary?query=${encodeURIComponent(word)}`}>
                {word}
              </Link>
            ))}
          </div>
        </section>
      ) : null}
    </>
  )
}
