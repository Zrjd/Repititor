import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { practiceApi, statsApi } from '../api/endpoints'
import { EmptyState, ErrorState, ProgressBar, Spinner, StatTile } from '../components/Feedback'
import { PageHeader } from '../components/PageHeader'
import { useAuth } from '../auth/AuthContext'
import { useI18n } from '../i18n'

export function DashboardPage() {
  const { t, plural, formatNumber, formatDate } = useI18n()
  const { user } = useAuth()
  const dashboard = useQuery({ queryKey: ['dashboard'], queryFn: () => statsApi.dashboard(14) })

  if (dashboard.isPending) {
    return <Spinner />
  }
  if (dashboard.isError) {
    return <ErrorState error={dashboard.error} onRetry={() => dashboard.refetch()} />
  }

  const data = dashboard.data
  const activity = [...data.activity].reverse()
  const maxReviews = Math.max(1, ...activity.map((day) => day.reviewsCompleted))
  const goal = data.dailyGoalXp

  return (
    <>
      <PageHeader
        title={t('dashboard.title')}
        subtitle={t('dashboard.greeting', { name: user?.displayName ?? '' })}
        actions={
          data.cardsDue > 0 ? (
            <Link className="button button--primary" to="/practice/session">
              {t('dashboard.startPractice')}
            </Link>
          ) : (
            <Link className="button" to="/dictionary">
              {t('dashboard.browseDictionary')}
            </Link>
          )
        }
      />

      <section className="stat-grid">
        <StatTile
          label={t('dashboard.dueCards')}
          value={formatNumber(data.cardsDue)}
          hint={
            data.newCards > 0
              ? `+${formatNumber(data.newCards)} ${plural('newCard', data.newCards)}`
              : undefined
          }
          tone={data.cardsDue > 0 ? 'accent' : 'default'}
        />
        <StatTile label={t('dashboard.words')} value={formatNumber(data.totalWords)} />
        <StatTile label={t('dashboard.mastered')} value={formatNumber(data.masteredWords)} />
        <StatTile label={t('dashboard.streak')} value={formatNumber(data.currentStreak)} />
        <StatTile label={t('dashboard.xp')} value={formatNumber(data.xpToday)} />
        <StatTile label={t('dashboard.exercises')} value={formatNumber(data.exercisesDone7d)} />
      </section>

      <section className="card goal-card">
        <div className="goal-card__head">
          <div>
            <h2>{t('dashboard.goal')}</h2>
            <p className="muted">{t('dashboard.goalProgress', { value: data.xpToday, goal })}</p>
          </div>
          {data.xpToday >= goal ? <span className="badge badge--success">{t('dashboard.goalReached')}</span> : null}
        </div>
        <ProgressBar value={data.xpToday} max={goal} />
      </section>

      {data.cardsDue === 0 ? (
        <EmptyState title={t('dashboard.nothingDue')} hint={t('dashboard.nothingDueHint')} />
      ) : null}

      <section className="card">
        <h2>{t('dashboard.activityTitle')}</h2>
        {activity.length === 0 ? (
          <EmptyState title={t('common.empty')} />
        ) : (
          <div className="chart" role="img" aria-label={t('dashboard.activityTitle')}>
            {activity.map((day) => (
              <div className="chart__column" key={day.date}>
                <div
                  className="chart__bar"
                  style={{ height: `${Math.max(4, (day.reviewsCompleted / maxReviews) * 100)}%` }}
                  title={`${formatDate(day.date)}: ${day.reviewsCompleted}`}
                />
                <span className="chart__label">{day.date.slice(8)}</span>
              </div>
            ))}
          </div>
        )}
      </section>

      {data.recommendedWords.length > 0 ? (
        <section className="card">
          <h2>{t('dashboard.recommended')}</h2>
          <div className="word-grid">
            {data.recommendedWords.slice(0, 6).map((word) => (
              <Link className="card word-tile" key={word.lexicalUnitId} to={`/dictionary/${word.lexicalUnitId}`}>
                <div className="word-tile__head">
                  <strong>{word.text}</strong>
                  <span className="badge badge--level">{word.state}</span>
                </div>
                {word.translation ? <span className="word-tile__translation">{word.translation}</span> : null}
              </Link>
            ))}
          </div>
          <Link className="button" to="/practice/session">
            {t('practice.start')}
          </Link>
        </section>
      ) : null}
    </>
  )
}

export function PracticeSummaryPanel() {
  const { t, formatNumber } = useI18n()
  const summary = useQuery({ queryKey: ['practice-summary'], queryFn: () => practiceApi.summary() })

  if (summary.isPending) {
    return <Spinner />
  }
  if (summary.isError) {
    return <ErrorState error={summary.error} onRetry={() => summary.refetch()} />
  }

  return (
    <div className="stat-grid stat-grid--compact">
      <StatTile label={t('practice.reviewed')} value={formatNumber(summary.data.reviewed)} />
      <StatTile label={t('practice.correct')} value={formatNumber(summary.data.correct)} />
      <StatTile label={t('practice.accuracy')} value={`${summary.data.accuracyPercent}%`} />
      <StatTile label={t('practice.xpEarned')} value={formatNumber(summary.data.xpEarned)} />
      <StatTile label={t('practice.remaining')} value={formatNumber(summary.data.remainingDue)} />
    </div>
  )
}
