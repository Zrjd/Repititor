import { useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { useI18n } from '../i18n'
import { LanguageToggle } from '../components/LanguageToggle'
import { ApiError } from '../api/client'

export function LoginPage() {
  const { t } = useI18n()
  const { signIn } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const from = (location.state as { from?: string } | null)?.from ?? '/'

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await signIn(email.trim(), password)
      navigate(from, { replace: true })
    } catch (cause) {
      setError(cause instanceof ApiError && cause.isUnauthorized ? t('auth.failed') : t('common.error'))
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
        <h2>{t('auth.signIn')}</h2>

        <label className="field">
          <span>{t('auth.email')}</span>
          <input
            type="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            autoComplete="email"
            required
          />
        </label>

        <label className="field">
          <span>{t('auth.password')}</span>
          <input
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            autoComplete="current-password"
            required
          />
        </label>

        {error ? <p className="alert alert--error">{error}</p> : null}

        <button type="submit" className="button button--primary" disabled={busy}>
          {busy ? t('common.loading') : t('auth.submitLogin')}
        </button>

        <p className="auth__switch">
          {t('auth.noAccount')}{' '}
          <Link to="/register">{t('auth.signUp')}</Link>
        </p>
      </form>
    </div>
  )
}
