import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { catalogApi } from '../api/endpoints'
import type { CefrLevel } from '../api/types'
import { EmptyState, ErrorState, ProgressBar, Spinner } from '../components/Feedback'
import { PageHeader } from '../components/PageHeader'
import { useI18n } from '../i18n'

const levels: CefrLevel[] = ['A1', 'A2', 'B1', 'B2', 'C1', 'C2']

export function CatalogPage() {
  const { t, plural, locale, formatNumber } = useI18n()
  const [languageId, setLanguageId] = useState('')
  const [level, setLevel] = useState<CefrLevel | ''>('')

  const languages = useQuery({ queryKey: ['languages'], queryFn: catalogApi.languages })
  const courses = useQuery({
    queryKey: ['courses', languageId, level],
    queryFn: () => catalogApi.courses(languageId || undefined, level || undefined),
  })

  const visible = courses.data

  return (
    <>
      <PageHeader
        title={t('catalog.title')}
        subtitle={t('catalog.subtitle')}
        actions={
          <div className="filters">
            <select
              className="select"
              value={languageId}
              onChange={(event) => setLanguageId(event.target.value)}
              aria-label={t('catalog.languageFilter')}
            >
              <option value="">{t('common.all')}</option>
              {languages.data?.map((language) => (
                <option key={language.id} value={language.id}>
                  {language.flagEmoji} {locale === 'en' ? language.nameEnglish : language.nameRussian}
                </option>
              ))}
            </select>

            <select
              className="select"
              value={level}
              onChange={(event) => setLevel(event.target.value as CefrLevel | '')}
              aria-label={t('common.level')}
            >
              <option value="">{t('common.all')}</option>
              {levels.map((item) => (
                <option key={item} value={item}>
                  {item}
                </option>
              ))}
            </select>
          </div>
        }
      />

      {courses.isPending ? <Spinner /> : null}
      {courses.isError ? <ErrorState error={courses.error} onRetry={() => courses.refetch()} /> : null}

      {visible?.length === 0 ? <EmptyState title={t('common.empty')} /> : null}

      <div className="card-grid">
        {visible?.map((course) => (
          <article className="card course-card" key={course.id}>
            <div className="course-card__head">
              <span className="badge badge--level">{course.level}</span>
              {course.isEnrolled ? <span className="badge badge--success">{t('catalog.enrolled')}</span> : null}
            </div>
            <h2>
              <Link to={`/courses/${course.slug}`}>{course.title}</Link>
            </h2>
            {course.description ? <p className="muted">{course.description}</p> : null}
            <p className="course-card__meta">
              {formatNumber(course.lessonsCount)} {plural('lesson', course.lessonsCount)} ·{' '}
              {formatNumber(course.estimatedMinutes)} {t('common.minutes')}
            </p>
            <ProgressBar value={course.progressPercent} />
            <Link className="button" to={`/courses/${course.slug}`}>
              {t('catalog.lessonsTitle')}
            </Link>
          </article>
        ))}
      </div>
    </>
  )
}
