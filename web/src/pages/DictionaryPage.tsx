import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { type FormEvent } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { dictionaryApi, practiceApi } from '../api/endpoints'
import type { LexicalUnit, SimilarWord } from '../api/types'
import { EmptyState, ErrorState, Spinner } from '../components/Feedback'
import { PageHeader } from '../components/PageHeader'
import { useI18n } from '../i18n'
import { WordOfTheDayCard } from './MyWordsPage'

const PAGE_SIZE = 20

export function DictionaryPage() {
  const { t, formatNumber } = useI18n()
  const [params, setParams] = useSearchParams()

  const query = params.get('query') ?? ''
  const page = Math.max(1, Number(params.get('page') ?? '1') || 1)

  const search = useQuery({
    queryKey: ['dictionary', query, page],
    queryFn: ({ signal }) => dictionaryApi.search({ query: query || undefined, page, limit: PAGE_SIZE }, signal),
    enabled: true,
  })

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const next = String(form.get('query') ?? '').trim()
    setParams(next ? { query: next } : {})
  }

  const goToPage = (target: number) => {
    const next: Record<string, string> = { page: String(target) }
    if (query) {
      next.query = query
    }
    setParams(next)
  }

  return (
    <>
      <PageHeader title={t('dictionary.title')} subtitle={t('dictionary.subtitle')} />

      <WordOfTheDayCard />

      <form className="search-bar" key={query} onSubmit={submit}>
        <input
          className="input"
          type="search"
          name="query"
          defaultValue={query}
          placeholder={t('dictionary.searchPlaceholder')}
          aria-label={t('common.search')}
        />
        <button type="submit" className="button button--primary">
          {t('common.search')}
        </button>
      </form>

      {search.isPending ? <Spinner /> : null}
      {search.isError ? <ErrorState error={search.error} onRetry={() => search.refetch()} /> : null}

      {search.data ? (
        <>
          <p className="muted">
            {t('dictionary.results')}: {formatNumber(search.data.total)}
          </p>

          {search.data.items.length === 0 ? (
            <EmptyState title={t('dictionary.noResults')} hint={t('dictionary.noResultsHint')} />
          ) : (
            <div className="word-grid">
              {search.data.items.map((word) => (
                <WordTile key={word.id} word={word} />
              ))}
            </div>
          )}

          {search.data.totalPages > 1 ? (
            <div className="pager">
              <button
                type="button"
                className="button"
                disabled={page <= 1}
                onClick={() => goToPage(Math.max(1, page - 1))}
              >
                {t('common.prev')}
              </button>
              <span className="muted">
                {t('common.page')} {formatNumber(page)} {t('common.of')} {formatNumber(search.data.totalPages)}
              </span>
              <button
                type="button"
                className="button"
                disabled={page >= search.data.totalPages}
                onClick={() => goToPage(page + 1)}
              >
                {t('common.next')}
              </button>
            </div>
          ) : null}
        </>
      ) : null}
    </>
  )
}

function WordTile({ word }: { word: LexicalUnit }) {
  return (
    <Link className="card word-tile" to={`/dictionary/${word.id}`}>
      <div className="word-tile__head">
        <strong>{word.text}</strong>
        <span className="badge badge--level">{word.minLearnerLevel}</span>
      </div>
      {word.translation ? <span className="word-tile__translation">{word.translation}</span> : null}
      {word.partOfSpeech ? <span className="muted">{word.partOfSpeech}</span> : null}
    </Link>
  )
}

export function WordPage() {
  const { t } = useI18n()
  const queryClient = useQueryClient()
  const { wordId = '' } = useParams()

  const word = useQuery({ queryKey: ['word', wordId], queryFn: () => dictionaryApi.word(wordId) })
  const similar = useQuery({ queryKey: ['word-similar', wordId], queryFn: () => dictionaryApi.similar(wordId) })
  const myWords = useQuery({ queryKey: ['my-words', 'all'], queryFn: () => dictionaryApi.myWords({ pageSize: 200 }) })
  const decks = useQuery({ queryKey: ['decks'], queryFn: practiceApi.decks })

  const isInWords = myWords.data?.items.some((item) => item.lexicalUnitId === wordId) ?? false

  const toggleWord = useMutation({
    mutationFn: async () => {
      if (isInWords) {
        await dictionaryApi.removeMyWord(wordId)
        return
      }
      await dictionaryApi.addMyWord(wordId)
    },
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: ['my-words'] }),
        queryClient.invalidateQueries({ queryKey: ['decks'] }),
      ]),
  })

  const addToDeck = useMutation({
    mutationFn: (deckId: string) => practiceApi.addCards(deckId, [wordId]),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['decks'] }),
  })

  if (word.isPending) {
    return <Spinner />
  }
  if (word.isError) {
    return <ErrorState error={word.error} onRetry={() => word.refetch()} />
  }

  const data = word.data

  return (
    <>
      <PageHeader
        title={data.text}
        subtitle={data.translation ?? undefined}
        actions={
          <div className="row-actions">
            <button
              type="button"
              className={isInWords ? 'button' : 'button button--primary'}
              onClick={() => toggleWord.mutate()}
              disabled={toggleWord.isPending}
            >
              {isInWords ? t('dictionary.inWords') : t('dictionary.addToWords')}
            </button>
            {toggleWord.isError ? <ErrorState error={toggleWord.error} /> : null}
            {decks.data && decks.data.length > 0 ? (
              <select
                className="select"
                value=""
                onChange={(event) => {
                  if (event.target.value) {
                    addToDeck.mutate(event.target.value)
                  }
                }}
                disabled={addToDeck.isPending}
                aria-label={t('practice.addToDeck')}
              >
                <option value="">{t('practice.addToDeck')}</option>
                {decks.data.map((deck) => (
                  <option key={deck.id} value={deck.id}>
                    {deck.name}
                  </option>
                ))}
              </select>
            ) : null}
          </div>
        }
      />

      <section className="card word-detail">
        <dl className="definitions">
          <div>
            <dt>{t('dictionary.partOfSpeech')}</dt>
            <dd>{data.partOfSpeech ?? '—'}</dd>
          </div>
          <div>
            <dt>{t('common.level')}</dt>
            <dd>{data.minLearnerLevel}</dd>
          </div>
          <div>
            <dt>{t('dictionary.frequency')}</dt>
            <dd>{data.frequencyRank}</dd>
          </div>
          <div>
            <dt>{t('dictionary.source')}</dt>
            <dd>{data.status}</dd>
          </div>
        </dl>

        {data.transcription ? <p className="muted">{data.transcription}</p> : null}
        {data.audioUrl ? (
          <audio controls preload="none" src={data.audioUrl}>
            {data.text}
          </audio>
        ) : null}
        {data.exampleTarget ? (
          <p className="word-detail__example">
            <strong>{t('dictionary.example')}:</strong> {data.exampleTarget}
          </p>
        ) : null}
        {data.exampleNative ? <p className="muted">{data.exampleNative}</p> : null}
        {data.tags && data.tags.length > 0 ? (
          <div className="chip-list">
            {data.tags.map((tag) => (
              <span className="chip" key={tag}>
                {tag}
              </span>
            ))}
          </div>
        ) : null}
      </section>

      <section className="card">
        <h2>{t('dictionary.similar')}</h2>
        {similar.isPending ? <Spinner /> : null}
        {similar.isError ? <ErrorState error={similar.error} onRetry={() => similar.refetch()} /> : null}
        {similar.data && similar.data.length === 0 ? (
          <EmptyState title={t('common.empty')} hint={t('dictionary.similarHint')} />
        ) : null}
        {similar.data && similar.data.length > 0 ? (
          <div className="word-grid">
            {similar.data.map((item: SimilarWord) => (
              <WordTile key={item.id} word={item} />
            ))}
          </div>
        ) : null}
      </section>
    </>
  )
}
