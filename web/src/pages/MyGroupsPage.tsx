import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { useI18n } from '../i18n'
import { groupsApi } from '../api/endpoints'
import { ErrorState, Spinner } from '../components/Feedback'
import { PageHeader } from '../components/PageHeader'

/**
 * Страница ученика: группы, в которые его записал учитель, и вступление по коду-приглашению.
 */
export function MyGroupsPage() {
  const { t } = useI18n()
  const qc = useQueryClient()
  const [code, setCode] = useState('')
  const [joined, setJoined] = useState<string>('')
  const [invalid, setInvalid] = useState(false)

  const groups = useQuery({ queryKey: ['my-groups'], queryFn: groupsApi.mine })

  const joinMutation = useMutation({
    mutationFn: () => groupsApi.join(code.trim()),
    onSuccess: (group) => {
      setCode('')
      setInvalid(false)
      setJoined(group.name)
      qc.invalidateQueries({ queryKey: ['my-groups'] })
    },
    onError: (error: unknown) => {
      const status = (error as { status?: number }).status
      setInvalid(status === 400 || status === 404)
    },
  })

  return (
    <>
      <PageHeader title={t('groups.myGroupsTitle')} subtitle={t('groups.myGroupsSubtitle')} />

      <form
        className="card stack"
        onSubmit={(e) => {
          e.preventDefault()
          joinMutation.mutate()
        }}
      >
        <label>
          {t('groups.joinCode')}
          <input
            value={code}
            onChange={(e) => {
              setCode(e.target.value)
              setInvalid(false)
            }}
            required
            maxLength={8}
            autoComplete="off"
            placeholder="ABCD1234"
          />
        </label>
        <div>
          <button
            type="submit"
            className="button button--primary"
            disabled={joinMutation.isPending || code.trim().length === 0}
          >
            {t('groups.join')}
          </button>
        </div>
        {invalid ? <span className="text-error">{t('groups.invalidInvite')}</span> : null}
        {joined ? <span className="text-success">{t('groups.joinedGroup', { name: joined })}</span> : null}
      </form>

      {groups.isPending ? <Spinner /> : null}
      {groups.isError ? (
        <ErrorState error={groups.error} onRetry={() => groups.refetch()} />
      ) : null}

      {groups.data && groups.data.length === 0 ? (
        <p className="muted">{t('groups.myGroupsEmpty')}</p>
      ) : null}

      <div className="card-grid">
        {groups.data?.map((group) => (
          <article key={group.id} className="card course-card">
            <div className="course-card__head">
              <h3>{group.name}</h3>
              {group.membersCount === 1 ? <span className="badge badge--level">{t('groups.groupPersonal')}</span> : null}
            </div>
            {group.description ? <p className="muted">{group.description}</p> : null}
            <p className="muted">{t('groups.groupTeacher', { name: group.teacherDisplayName })}</p>

            <p className="course-card__meta">{t('groups.membersCount', { count: group.membersCount })}</p>
            <strong>{t('groups.groupCourses')}</strong>
            {group.courses.length === 0 ? (
              <p className="muted">{t('groups.noCourses')}</p>
            ) : (
              <ul className="stack">
                {group.courses.map((course) => (
                  <li key={course.courseId} className="my-word">
                    <div className="my-word__body">
                      <strong>{course.title}</strong>
                      <span className="muted">
                        {course.level} · {course.languageCode}
                      </span>
                    </div>
                    <Link className="button button--ghost" to={`/courses/${course.slug}`}>
                      {t('catalog.lessonsTitle')}
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </article>
        ))}
      </div>
    </>
  )
}
