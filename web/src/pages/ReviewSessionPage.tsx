import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { practiceApi } from '../api/endpoints'
import type { ReviewCard, ReviewRating, ReviewResult } from '../api/types'
import { EmptyState, ErrorState, Spinner, StatTile } from '../components/Feedback'
import { useI18n } from '../i18n'

const SESSION_LIMIT = 20

const ratings: { value: ReviewRating; key: 'practice.again' | 'practice.hard' | 'practice.good' | 'practice.easy' }[] = [
  { value: 'Again', key: 'practice.again' },
  { value: 'Hard', key: 'practice.hard' },
  { value: 'Good', key: 'practice.good' },
  { value: 'Easy', key: 'practice.easy' },
]

export function ReviewSessionPage() {
  const { t, formatNumber, formatDate } = useI18n()
  const queryClient = useQueryClient()
  const [revealed, setRevealed] = useState(false)
  const [index, setIndex] = useState(0)
  const [results, setResults] = useState<ReviewResult[]>([])
  const shownAt = useRef(0)

  const cards = useQuery({
    queryKey: ['session-cards'],
    queryFn: () => practiceApi.due(SESSION_LIMIT),
  })

  const submit = useMutation({
    mutationFn: (payload: { reviewCardId: string; rating: ReviewRating; durationMs: number }[]) =>
      practiceApi.submitReviews(payload, payload.length),
    onSuccess: (data) => {
      setResults((prev) => [...prev, ...data])
      setIndex((prev) => prev + 1)
      setRevealed(false)
      shownAt.current = performance.now()
      void queryClient.invalidateQueries({ queryKey: ['session-cards'] })
      void queryClient.invalidateQueries({ queryKey: ['decks'] })
      void queryClient.invalidateQueries({ queryKey: ['due-count'] })
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
    },
  })

  const queue: ReviewCard[] = useMemo(() => cards.data?.cards ?? [], [cards.data])
  const current = queue[index]
  const finished = index >= queue.length
  const cardId = current?.reviewCardId

  useEffect(() => {
    shownAt.current = performance.now()
  }, [cardId])

  if (cards.isPending) {
    return <Spinner />
  }
  if (cards.isError) {
    return <ErrorState error={cards.error} onRetry={() => cards.refetch()} />
  }

  if (queue.length === 0) {
    return (
      <EmptyState
        title={t('practice.sessionEmpty')}
        hint={t('practice.sessionEmptyHint')}
      />
    )
  }

  if (finished) {
    const correct = results.filter((item) => item.wasCorrect).length
    const xp = results.reduce((sum, item) => sum + item.xpEarned, 0)

    return (
      <div className="session-done">
        <h1>{t('practice.sessionDone')}</h1>
        <div className="stat-grid stat-grid--compact">
          <StatTile label={t('practice.answered')} value={formatNumber(results.length)} />
          <StatTile label={t('practice.correct')} value={formatNumber(correct)} />
          <StatTile
            label={t('practice.accuracy')}
            value={`${results.length ? Math.round((correct / results.length) * 100) : 0}%`}
          />
          <StatTile label={t('practice.xpEarned')} value={formatNumber(xp)} tone="accent" />
        </div>
        <div className="row-actions">
          <Link className="button button--primary" to="/practice">
            {t('practice.backToPractice')}
          </Link>
          <Link className="button" to="/dashboard">
            {t('nav.dashboard')}
          </Link>
        </div>
      </div>
    )
  }

  if (!current) {
    return <Spinner />
  }

  const reveal = () => {
    shownAt.current = performance.now()
    setRevealed(true)
  }

  const rate = (rating: ReviewRating) => {
    const durationMs = Math.max(0, performance.now() - shownAt.current)
    submit.mutate([{ reviewCardId: current.reviewCardId, rating, durationMs }])
  }

  const lastResult = results.at(-1)

  return (
    <div className="session">
      <header className="session__head">
        <h1>{t('practice.sessionTitle')}</h1>
        <span className="muted">
          {formatNumber(index + 1)} / {formatNumber(queue.length)}
        </span>
      </header>

      <div className="progress" role="progressbar" aria-valuenow={index} aria-valuemin={0} aria-valuemax={queue.length}>
        <div className="progress__fill" style={{ width: `${(index / queue.length) * 100}%` }} />
      </div>

      <article className="card flashcard">
        <span className="badge badge--level">{current.state}</span>
        <h2 className="flashcard__text">{current.text}</h2>

        {revealed ? (
          <div className="flashcard__answer">
            {current.translation ? <p className="flashcard__translation">{current.translation}</p> : null}
            {current.exampleTarget ? <p className="muted">{current.exampleTarget}</p> : null}
            <p className="muted">
              {t('practice.interval')}: {current.intervalDays} {t('common.minutes')} ·{' '}
              {formatDate(current.dueAt, { dateStyle: 'short', timeStyle: 'short' })}
            </p>
          </div>
        ) : null}
      </article>

      {revealed ? (
        <div className="ratings">
          {ratings.map((rating) => (
            <button
              key={rating.value}
              type="button"
              className={`rating rating--${rating.value.toLowerCase()}`}
              onClick={() => rate(rating.value)}
              disabled={submit.isPending}
            >
              {t(rating.key)}
            </button>
          ))}
        </div>
      ) : (
        <button type="button" className="button button--primary" onClick={reveal}>
          {t('practice.showAnswer')}
        </button>
      )}

      {submit.isError ? <ErrorState error={submit.error} /> : null}

      {lastResult ? (
        <p className="session__feedback muted">
          {lastResult.state} · +{formatNumber(lastResult.xpEarned)} XP
        </p>
      ) : null}
    </div>
  )
}
