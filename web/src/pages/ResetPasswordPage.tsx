import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { useI18n } from '../i18n'
import { LanguageToggle } from '../components/LanguageToggle'
import { authApi } from '../api/endpoints'

export function ResetPasswordPage() {
  const { t } = useI18n()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''

  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState(false)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (password !== confirm) {
      setError(t('auth.passwordMismatch'))
      return
    }
    setBusy(true)
    setError(null)
    try {
      await authApi.resetPassword(token, password)
      setDone(true)
    } catch {
      setError(t('auth.resetFailed'))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="auth">
      <div className="auth__aside">
        <span className="auth__logo" aria-hidden="true">
          R
        </span>
        <h1>{t('app.name')}</h1>
        <p>{t('app.tagline')}</p>
        <LanguageToggle />
      </div>

      <form className="auth__form card" onSubmit={submit}>
        <h2>{t('auth.resetTitle')}</h2>

        {done ? (
          <>
            <p className="alert alert--success">{t('auth.resetDone')}</p>
            <button
              type="button"
              className="button button--primary"
              onClick={() => navigate('/login', { replace: true })}
            >
              {t('auth.backToLogin')}
            </button>
          </>
        ) : (
          <>
            <label className="field">
              <span>{t('auth.newPassword')}</span>
              <input
                type="password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                autoComplete="new-password"
                required
                minLength={8}
              />
            </label>

            <label className="field">
              <span>{t('auth.confirmPassword')}</span>
              <input
                type="password"
                value={confirm}
                onChange={(event) => setConfirm(event.target.value)}
                autoComplete="new-password"
                required
                minLength={8}
              />
            </label>

            {error ? <p className="alert alert--error">{error}</p> : null}

            <button type="submit" className="button button--primary" disabled={busy || !token}>
              {busy ? t('common.loading') : t('auth.resetSubmit')}
            </button>

            <p className="auth__switch">
              <Link to="/login">{t('auth.backToLogin')}</Link>
            </p>
          </>
        )}
      </form>
    </div>
  )
}
