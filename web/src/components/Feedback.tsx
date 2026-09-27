import { useI18n } from '../i18n'
import { ApiError } from '../api/client'

export function Spinner({ label }: { label?: string }) {
  const { t } = useI18n()
  return (
    <div className="spinner" role="status" aria-live="polite">
      <span className="spinner__ring" aria-hidden="true" />
      <span className="spinner__label">{label ?? t('common.loading')}</span>
    </div>
  )
}

export function ErrorState({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const { t } = useI18n()

  const title =
    error instanceof ApiError
      ? t(`errors.${mapErrorCode(error.code)}` as 'errors.network')
      : t('common.error')

  return (
    <div className="state state--error" role="alert">
      <p className="state__title">{title}</p>
      {error instanceof Error && error.message !== title ? (
        <p className="state__detail">{error.message}</p>
      ) : null}
      {onRetry ? (
        <button type="button" className="button" onClick={onRetry}>
          {t('common.retry')}
        </button>
      ) : null}
    </div>
  )
}

function mapErrorCode(code: string): string {
  switch (code) {
    case 'invalid_credentials':
    case 'unauthorized':
    case 'token_expired':
      return 'unauthorized'
    case 'forbidden':
      return 'forbidden'
    case 'not_found':
      return 'notFound'
    case 'conflict':
      return 'conflict'
    case 'validation_failed':
      return 'validation'
    case 'ai_unavailable':
      return 'aiUnavailable'
    case 'rate_limited':
      return 'rateLimited'
    default:
      return code.startsWith('http_5') ? 'server' : 'network'
  }
}

export function EmptyState({ title, hint }: { title: string; hint?: string }) {
  return (
    <div className="state state--empty">
      <p className="state__title">{title}</p>
      {hint ? <p className="state__detail">{hint}</p> : null}
    </div>
  )
}

export function ProgressBar({ value, max = 100 }: { value: number; max?: number }) {
  const percent = max <= 0 ? 0 : Math.min(100, Math.round((value / max) * 100))
  return (
    <div
      className="progress"
      role="progressbar"
      aria-valuenow={percent}
      aria-valuemin={0}
      aria-valuemax={100}
    >
      <div className="progress__fill" style={{ width: `${percent}%` }} />
      <span className="progress__label">{percent}%</span>
    </div>
  )
}

export function StatTile({
  label,
  value,
  hint,
  tone = 'default',
}: {
  label: string
  value: string | number
  hint?: string
  tone?: 'default' | 'accent' | 'warn'
}) {
  return (
    <div className={`stat stat--${tone}`}>
      <span className="stat__label">{label}</span>
      <span className="stat__value">{value}</span>
      {hint ? <span className="stat__hint">{hint}</span> : null}
    </div>
  )
}
