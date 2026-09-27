import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { catalogApi } from '../api/endpoints'
import type { CefrLevel } from '../api/types'
import { EmptyState, ErrorState, Spinner } from '../components/Feedback'
import { PageHeader } from '../components/PageHeader'
import { useI18n } from '../i18n'

const levels: CefrLevel[] = ['A1', 'A2', 'B1', 'B2', 'C1', 'C2']

export function GrammarPage() {
  const { t } = useI18n()
  const [level, setLevel] = useState<CefrLevel | ''>('')
  const [openId, setOpenId] = useState<string | null>(null)

  const topics = useQuery({
    queryKey: ['grammar', level],
    queryFn: () => catalogApi.grammar(undefined, level || undefined),
  })

  return (
    <>
      <PageHeader
        title={t('grammar.title')}
        subtitle={t('grammar.subtitle')}
        actions={
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
        }
      />

      {topics.isPending ? <Spinner /> : null}
      {topics.isError ? <ErrorState error={topics.error} onRetry={() => topics.refetch()} /> : null}
      {topics.data?.length === 0 ? <EmptyState title={t('grammar.empty')} /> : null}

      <div className="stack">
        {topics.data?.map((topic) => {
          const isOpen = openId === topic.id
          return (
            <article className="card grammar-card" key={topic.id}>
              <button
                type="button"
                className="grammar-card__head"
                onClick={() => setOpenId(isOpen ? null : topic.id)}
                aria-expanded={isOpen}
              >
                <span>
                  <strong>{topic.title}</strong>
                  {topic.summary ? <small className="muted">{topic.summary}</small> : null}
                </span>
                <span className="badge badge--level">{topic.minLevel}</span>
              </button>

              {isOpen && topic.explanationMarkdown ? (
                <article className="markdown">
                  {topic.explanationMarkdown.split('\n\n').map((block, index) =>
                    block.startsWith('#') ? (
                      <h4 key={index}>{block.replace(/^#+\s*/, '')}</h4>
                    ) : (
                      <p key={index}>{block}</p>
                    ),
                  )}
                </article>
              ) : null}
            </article>
          )
        })}
      </div>
    </>
  )
}
