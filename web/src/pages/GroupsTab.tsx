import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useI18n } from '../i18n'
import type { TranslationKey } from '../i18n'
import { teacherApi } from '../api/endpoints'
import type { GroupInvitation, StudyGroup, StudyGroupDetail } from '../api/types'
import { ErrorState, Spinner } from '../components/Feedback'

/**
 * Кабинет учебных групп: состав учеников (в том числе персональные занятия с одним
 * учеником), коды-приглашения и курсы, доступные участникам. Назначение курса сразу
 * выдаёт его всем участникам группы.
 */
export function GroupsTab() {
  const { t } = useI18n()
  const qc = useQueryClient()
  const [creating, setCreating] = useState(false)
  const [selectedId, setSelectedId] = useState<string>('')

  const groups = useQuery({ queryKey: ['teacher-groups'], queryFn: teacherApi.groups })
  const createMutation = useMutation({
    mutationFn: teacherApi.createGroup,
    onSuccess: (group) => {
      qc.invalidateQueries({ queryKey: ['teacher-groups'] })
      setCreating(false)
      setSelectedId(group.id)
    },
  })

  const detail = useQuery({
    queryKey: ['teacher-group', selectedId],
    queryFn: () => teacherApi.group(selectedId),
    enabled: !!selectedId,
  })

  const deleteMutation = useMutation({
    mutationFn: teacherApi.deleteGroup,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['teacher-groups'] })
      setSelectedId('')
    },
  })

  if (groups.isPending) return <Spinner />
  if (groups.isError) return <ErrorState error={groups.error} onRetry={() => groups.refetch()} />

  return (
    <div className="admin-section">
      <div className="admin-toolbar">
        <h2>{t('groups.groupsTitle')}</h2>
        <button type="button" className="button button--primary" onClick={() => setCreating(true)}>
          {t('groups.createGroup')}
        </button>
      </div>

      {creating ? (
        <GroupForm
          onCancel={() => setCreating(false)}
          onSubmit={(data) => createMutation.mutate(data)}
          pending={createMutation.isPending}
        />
      ) : null}

      {groups.data.length === 0 ? (
        <p className="muted">{t('groups.groupEmpty')}</p>
      ) : (
        <div className="admin-list">
          {groups.data.map((group) => (
            <button
              key={group.id}
              type="button"
              className={`card admin-row admin-row--button${group.id === selectedId ? ' admin-row--active' : ''}`}
              onClick={() => setSelectedId(group.id)}
            >
              <div className="admin-row__body">
                <strong>
                  {group.name}
                  {group.isPersonal ? ` · ${t('groups.groupPersonal')}` : ''}
                </strong>
                <span className="muted">
                  {t('groups.membersCount', { count: group.membersCount })} ·{' '}
                  {t('groups.coursesCount', { count: group.coursesCount })}
                </span>
              </div>
            </button>
          ))}
        </div>
      )}

      {selectedId ? (
        <GroupDetail
          detail={detail.data}
          pending={detail.isPending}
          error={detail.isError}
          onRetry={() => detail.refetch()}
          onChanged={() => {
            qc.invalidateQueries({ queryKey: ['teacher-group', selectedId] })
            qc.invalidateQueries({ queryKey: ['teacher-groups'] })
          }}
          onDeleted={() => deleteMutation.mutate(selectedId)}
        />
      ) : null}
    </div>
  )
}

function GroupForm({
  group,
  onCancel,
  onSubmit,
  pending,
}: {
  group?: StudyGroup
  onCancel: () => void
  onSubmit: (data: { name: string; description?: string }) => void
  pending: boolean
}) {
  const { t } = useI18n()
  const [name, setName] = useState(group?.name ?? '')
  const [description, setDescription] = useState(group?.description ?? '')

  return (
    <form
      className="card admin-form"
      onSubmit={(e) => {
        e.preventDefault()
        onSubmit({ name, description: description || undefined })
      }}
    >
      <h3>{group ? t('groups.editGroup') : t('groups.createGroup')}</h3>
      <label>
        {t('groups.groupName')}
        <input value={name} onChange={(e) => setName(e.target.value)} required maxLength={160} />
        <small className="muted">{t('groups.groupNameHint')}</small>
      </label>
      <label>
        {t('groups.groupDescription')}
        <textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={2} maxLength={1000} />
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

function GroupDetail({
  detail,
  pending,
  error,
  onRetry,
  onChanged,
  onDeleted,
}: {
  detail?: StudyGroupDetail
  pending: boolean
  error: boolean
  onRetry: () => void
  onChanged: () => void
  onDeleted: () => void
}) {
  const { t } = useI18n()
  if (pending) return <Spinner />
  if (error || !detail) return <ErrorState error={new Error(t('errors.notFound'))} onRetry={onRetry} />

  return (
    <div className="admin-section">
      <div className="admin-toolbar">
        <h3>{detail.name}</h3>
        <button
          type="button"
          className="button button--danger"
          onClick={() => {
            if (window.confirm(t('groups.deleteGroupConfirm', { name: detail.name }))) onDeleted()
          }}
        >
          {t('groups.deleteGroup')}
        </button>
      </div>

      <MembersBlock group={detail} onChanged={onChanged} />
      <GroupCoursesBlock group={detail} onChanged={onChanged} />
      <InvitationsBlock group={detail} onChanged={onChanged} />
    </div>
  )
}

function MembersBlock({ group, onChanged }: { group: StudyGroupDetail; onChanged: () => void }) {
  const { t } = useI18n()
  const [email, setEmail] = useState('')
  const [notFound, setNotFound] = useState(false)

  const addMutation = useMutation({
    mutationFn: () => teacherApi.addMember(group.id, email),
    onSuccess: () => {
      setEmail('')
      setNotFound(false)
      onChanged()
    },
    onError: (error: unknown) => {
      setNotFound((error as { status?: number }).status === 404)
    },
  })

  const removeMutation = useMutation({
    mutationFn: (userId: string) => teacherApi.removeMember(group.id, userId),
    onSuccess: onChanged,
  })

  return (
    <div className="card admin-form">
      <h4>{t('groups.groupMembers')}</h4>
      <form
        className="admin-form__inline"
        onSubmit={(e) => {
          e.preventDefault()
          addMutation.mutate()
        }}
      >
        <label>
          {t('groups.addMemberEmail')}
          <input
            type="email"
            value={email}
            onChange={(e) => {
              setEmail(e.target.value)
              setNotFound(false)
            }}
            required
            placeholder="student@example.com"
          />
          <small className="muted">{t('groups.addMemberHint')}</small>
        </label>
        <button
          type="submit"
          className="button button--primary"
          disabled={addMutation.isPending || !email}
        >
          {t('groups.addMember')}
        </button>
      </form>
      {notFound ? <p className="text-error">{t('groups.memberNotFound')}</p> : null}

      {group.members.length === 0 ? (
        <p className="muted">{t('groups.noMembers')}</p>
      ) : (
        <ul className="admin-list">
          {group.members.map((member) => (
            <li key={member.userId} className="card admin-row">
              <div className="admin-row__body">
                <strong>{member.displayName}</strong>
                <span className="muted">{member.email}</span>
              </div>
              <div className="admin-row__actions">
                <button
                  type="button"
                  className="button button--ghost"
                  onClick={() => {
                    if (window.confirm(t('groups.removeMemberConfirm', { name: member.displayName }))) {
                      removeMutation.mutate(member.userId)
                    }
                  }}
                >
                  {t('groups.removeMember')}
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function GroupCoursesBlock({ group, onChanged }: { group: StudyGroupDetail; onChanged: () => void }) {
  const { t } = useI18n()
  const [courseId, setCourseId] = useState('')

  const assignable = useQuery({
    queryKey: ['teacher-assignable-courses'],
    queryFn: teacherApi.assignableCourses,
  })

  const assignMutation = useMutation({
    mutationFn: () => teacherApi.assignCourse(group.id, courseId),
    onSuccess: () => {
      setCourseId('')
      onChanged()
    },
  })

  const unassignMutation = useMutation({
    mutationFn: (id: string) => teacherApi.unassignCourse(group.id, id),
    onSuccess: onChanged,
  })

  const available = (assignable.data ?? []).filter(
    (course) => !group.courses.some((assigned) => assigned.courseId === course.id),
  )

  return (
    <div className="card admin-form">
      <h4>{t('groups.groupCourses')}</h4>
      <form
        className="admin-form__inline"
        onSubmit={(e) => {
          e.preventDefault()
          assignMutation.mutate()
        }}
      >
        <label>
          {t('groups.assignCourse')}
          <select value={courseId} onChange={(e) => setCourseId(e.target.value)} required>
            <option value="">—</option>
            {available.map((course) => (
              <option key={course.id} value={course.id}>
                {course.title} · {course.level} · {course.languageCode}
                {course.ownerDisplayName ? ` · ${t('groups.ownCourse')}` : ` · ${t('groups.systemCourse')}`}
              </option>
            ))}
          </select>
          <small className="muted">{t('groups.assignCourseHint')}</small>
        </label>
        <button
          type="submit"
          className="button button--primary"
          disabled={assignMutation.isPending || !courseId}
        >
          {t('groups.assignCourse')}
        </button>
      </form>

      {group.courses.length === 0 ? (
        <p className="muted">{t('groups.noCourses')}</p>
      ) : (
        <ul className="admin-list">
          {group.courses.map((course) => (
            <li key={course.courseId} className="card admin-row">
              <div className="admin-row__body">
                <strong>
                  {course.title}
                  {!course.isPublished ? ` · ${t('groups.draftBadge')}` : ''}
                </strong>
                <span className="muted">
                  {course.level} · {course.languageCode} ·{' '}
                  {course.isOwnCourse ? t('groups.ownCourse') : t('groups.systemCourse')}
                </span>
              </div>
              <div className="admin-row__actions">
                <button
                  type="button"
                  className="button button--ghost"
                  onClick={() => unassignMutation.mutate(course.courseId)}
                >
                  {t('groups.unassignCourse')}
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function InvitationsBlock({ group, onChanged }: { group: StudyGroupDetail; onChanged: () => void }) {
  const { t, formatDate } = useI18n()
  const [copied, setCopied] = useState(false)

  const createMutation = useMutation({
    mutationFn: () => teacherApi.createInvitation(group.id, { expiresInDays: 14, maxUses: 0 }),
    onSuccess: onChanged,
  })

  const revokeMutation = useMutation({
    mutationFn: (id: string) => teacherApi.revokeInvitation(group.id, id),
    onSuccess: onChanged,
  })

  const invitations = group.invitations

  return (
    <div className="card admin-form">
      <h4>{t('groups.inviteTitle')}</h4>
      <p className="muted">{t('groups.inviteHint')}</p>
      <div className="admin-form__actions">
        <button
          type="button"
          className="button button--primary"
          onClick={() => createMutation.mutate()}
          disabled={createMutation.isPending}
        >
          {t('groups.createInvite')}
        </button>
        {copied ? <span className="text-success">{t('groups.copied')}</span> : null}
      </div>

      {invitations.length === 0 ? (
        <p className="muted">{t('groups.inviteTitle')}: —</p>
      ) : (
        <ul className="admin-list">
          {invitations.map((invite) => (
            <li key={invite.id} className="card admin-row">
              <div className="admin-row__body">
                <strong>{invite.code}</strong>
                <span className="muted">
                  {t('groups.inviteUses', {
                    used: invite.usedCount,
                    max: invite.maxUses === 0 ? t('groups.inviteUnlimited') : invite.maxUses,
                  })}{' '}
                  · {t('groups.inviteExpires', { date: formatDate(invite.expiresAt) })} ·{' '}
                  {inviteStatus(invite, t)}
                </span>
              </div>
              <div className="admin-row__actions">
                <button
                  type="button"
                  className="button button--ghost"
                  onClick={() => {
                    void navigator.clipboard?.writeText(invite.code)
                    setCopied(true)
                  }}
                >
                  {t('groups.copyCode')}
                </button>
                {invite.isActive ? (
                  <button
                    type="button"
                    className="button button--danger"
                    onClick={() => revokeMutation.mutate(invite.id)}
                  >
                    {t('groups.revokeInvite')}
                  </button>
                ) : null}
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function inviteStatus(invite: GroupInvitation, t: (key: TranslationKey) => string) {
  if (invite.isRevoked) return t('groups.inviteRevoked')
  if (invite.isActive) return t('groups.inviteActive')
  if (new Date(invite.expiresAt) < new Date()) return t('groups.inviteExpired')
  return t('groups.inviteUsedUp')
}
