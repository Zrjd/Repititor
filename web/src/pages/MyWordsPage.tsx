import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { dictionaryApi } from '../api/endpoints'
import type { CardState } from '../api/types'
import { EmptyState, ErrorState, Spinner } from '../components/Feedback'
import { PageHeader } from '../components/PageHeader'
import { useI18n } from '../i18n'

const states: CardState[] = ['New', 'Learning', 'Review', 'Relearning', 'Mastered']

export function MyWordsPage() {
  const { t, formatDate } = useI18n()
  const queryClient = useQueryClient()
  const [page, setPage] = useState(1)
  const [state, setState] = useState<CardState | ''>('')

  const words = useQuery({
    queryKey: ['my-words', page, state],
    queryFn: ({ signal }) =>
      dictionaryApi.myWords({ page, pageSize: 20, state: state || undefined }, signal),
  })

  const remove = useMutation({
    mutationFn: (lexicalUnitId: string) => dictionaryApi.removeMyWord(lexicalUnitId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['my-words'] }),
  })

  return (
    <>
      <PageHeader
        title={t('nav.myWords')}
        actions={
          <select
            className="select"
            value={state}
            onChange={(event) => {
              setState(event.target.value as CardState | '')
              setPage(1)
            }}
            aria-label={t('dictionary.state')}
          >
            <option value="">{t('common.all')}</option>
            {states.map((item) => (
              <option key={item} value={item}>
                {item}
              </option>
            ))}
          </select>
        }
      />

      {words.isPending ? <Spinner /> : null}
      {words.isError ? <ErrorState error={words.error} onRetry={() => words.refetch()} /> : null}
      {words.data?.items.length === 0 ? <EmptyState title={t('common.empty')} /> : null}

      <div className="stack">
        {words.data?.items.map((item) => (
          <article className="card my-word" key={item.userLexicalUnitId}>
            <div className="my-word__body">
              <div className="my-word__head">
                <Link to={`/dictionary/${item.lexicalUnitId}`}>
                  <strong>{item.text}</strong>
                </Link>
                {item.translation ? <span className="muted">{item.translation}</span> : null}
                <span className={`badge badge--state-${item.state.toLowerCase()}`}>{item.state}</span>
              </div>
              <p className="my-word__meta">
                {t('dictionary.mastered')}: {item.masteryScore} · {t('dictionary.source')}: {item.source} ·{' '}
                {t('dictionary.added')}: {formatDate(item.addedAt)} ·{' '}
                {t('dictionary.lastReviewed')}:{' '}
                {item.lastReviewedAt ? formatDate(item.lastReviewedAt) : t('dictionary.never')}
              </p>
            </div>
            <button
              type="button"
              className="button button--ghost"
              onClick={() => remove.mutate(item.lexicalUnitId)}
            >
              {t('dictionary.removeFromWords')}
            </button>
          </article>
        ))}
      </div>

      {words.data && words.data.totalPages > 1 ? (
        <div className="pager">
          <button
            type="button"
            className="button"
            disabled={page <= 1}
            onClick={() => setPage((prev) => Math.max(1, prev - 1))}
          >
            {t('common.prev')}
          </button>
          <span className="muted">
            {t('common.page')} {page} {t('common.of')} {words.data.totalPages}
          </span>
          <button
            type="button"
            className="button"
            disabled={page >= words.data.totalPages}
            onClick={() => setPage((prev) => prev + 1)}
          >
            {t('common.next')}
          </button>
        </div>
      ) : null}
    </>
  )
}

export function WordOfTheDayCard() {
  const { t } = useI18n()
  const wotd = useQuery({ queryKey: ['wotd'], queryFn: dictionaryApi.wordOfTheDay })

  if (wotd.isPending) {
    return <Spinner />
  }
  if (wotd.isError) {
    return <ErrorState error={wotd.error} onRetry={() => wotd.refetch()} />
  }

  const { word, reason } = wotd.data

  return (
    <section className="card wotd">
      <span className="badge badge--accent">{t('dictionary.wordOfTheDay')}</span>
      <h2>
        <Link to={`/dictionary/${word.id}`}>{word.text}</Link>
      </h2>
      {word.translation ? <p className="wotd__translation">{word.translation}</p> : null}
      {word.exampleTarget ? <p className="muted">{word.exampleTarget}</p> : null}
      {reason ? (
        <p className="muted">
          {t('dictionary.wordOfTheDayReason')}: {reason}
        </p>
      ) : null}
    </section>
  )
}
