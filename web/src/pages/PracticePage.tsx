import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { practiceApi } from '../api/endpoints'
import { EmptyState, ErrorState, Spinner } from '../components/Feedback'
import { PageHeader } from '../components/PageHeader'
import { useI18n } from '../i18n'

export function PracticePage() {
  const { t, formatNumber, formatDate } = useI18n()
  const queryClient = useQueryClient()
  const [creating, setCreating] = useState(false)

  const decks = useQuery({ queryKey: ['decks'], queryFn: practiceApi.decks })
  const due = useQuery({ queryKey: ['due-count'], queryFn: practiceApi.dueCount })
  const forecast = useQuery({ queryKey: ['forecast'], queryFn: () => practiceApi.forecast() })

  const createDeck = useMutation({
    mutationFn: ({ name, description }: { name: string; description?: string }) =>
      practiceApi.createDeck(name, description),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['decks'] }),
  })

  const deleteDeck = useMutation({
    mutationFn: (id: string) => practiceApi.deleteDeck(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['decks'] }),
  })

  const submitDeck = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const name = String(form.get('name') ?? '').trim()
    const description = String(form.get('description') ?? '').trim()
    if (!name) {
      return
    }
    createDeck.mutate({ name, description: description || undefined })
    event.currentTarget.reset()
    setCreating(false)
  }

  const dueCount = due.data?.due ?? 0

  return (
    <>
      <PageHeader
        title={t('practice.title')}
        subtitle={t('practice.subtitle')}
        actions={
          <div className="row-actions">
            <button
              type="button"
              className="button"
              onClick={() => setCreating((prev) => !prev)}
              aria-expanded={creating}
            >
              {t('practice.createDeck')}
            </button>
            <Link
              className={dueCount > 0 ? 'button button--primary' : 'button'}
              to="/practice/session"
              aria-disabled={dueCount === 0}
            >
              {t('practice.start')}
            </Link>
          </div>
        }
      />

      {creating ? (
        <form className="card deck-form" onSubmit={submitDeck}>
          <label className="field">
            <span>{t('practice.deckName')}</span>
            <input name="name" required maxLength={120} />
          </label>
          <label className="field">
            <span>{t('practice.deckDescription')}</span>
            <textarea name="description" rows={2} maxLength={1000} />
          </label>
          <div className="row-actions">
            <button type="submit" className="button button--primary" disabled={createDeck.isPending}>
              {t('common.create')}
            </button>
            <button type="button" className="button button--ghost" onClick={() => setCreating(false)}>
              {t('common.cancel')}
            </button>
          </div>
        </form>
      ) : null}

      {decks.isPending ? <Spinner /> : null}
      {decks.isError ? <ErrorState error={decks.error} onRetry={() => decks.refetch()} /> : null}

      {decks.data?.length === 0 ? (
        <EmptyState title={t('practice.noDecks')} hint={t('practice.noDecksHint')} />
      ) : null}

      <div className="card-grid">
        {decks.data?.map((deck) => (
          <article className="card deck-card" key={deck.id}>
            <h2>{deck.name}</h2>
            {deck.description ? <p className="muted">{deck.description}</p> : null}
            <dl className="deck-card__stats">
              <div>
                <dt>{t('practice.cards')}</dt>
                <dd>{formatNumber(deck.cardsCount)}</dd>
              </div>
              <div>
                <dt>{t('practice.due')}</dt>
                <dd className={deck.dueCount > 0 ? 'is-accent' : undefined}>{formatNumber(deck.dueCount)}</dd>
              </div>
              <div>
                <dt>{t('practice.new')}</dt>
                <dd>{formatNumber(deck.newCount)}</dd>
              </div>
              <div>
                <dt>{t('practice.mastered')}</dt>
                <dd>{formatNumber(deck.masteredCount)}</dd>
              </div>
            </dl>
            <p className="muted">{formatDate(deck.updatedAt)}</p>
            <button
              type="button"
              className="button button--ghost"
              onClick={() => {
                if (window.confirm(t('practice.deleteDeckConfirm', { name: deck.name }))) {
                  deleteDeck.mutate(deck.id)
                }
              }}
            >
              {t('common.delete')}
            </button>
          </article>
        ))}
      </div>

      <section className="card">
        <h2>{t('practice.forecastTitle')}</h2>
        {forecast.isPending ? <Spinner /> : null}
        {forecast.isError ? <ErrorState error={forecast.error} onRetry={() => forecast.refetch()} /> : null}
        {forecast.data ? (
          <div className="forecast">
            {forecast.data.forecast.map((day) => (
              <div className="forecast__day" key={day.date}>
                <span className="forecast__count">{day.cards}</span>
                <span className="forecast__date">{formatDate(day.date, { day: 'numeric', month: 'short' })}</span>
              </div>
            ))}
          </div>
        ) : null}
      </section>
    </>
  )
}
