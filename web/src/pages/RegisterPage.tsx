import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { useI18n } from '../i18n'
import { LanguageToggle } from '../components/LanguageToggle'
import { ApiError } from '../api/client'

export function RegisterPage() {
  const { t } = useI18n()
  const { signUp } = useAuth()
  const navigate = useNavigate()
  const [form, setForm] = useState({ email: '', password: '', displayName: '' })
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const update = (key: keyof typeof form) => (event: { target: { value: string } }) =>
    setForm((prev) => ({ ...prev, [key]: event.target.value }))

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await signUp(form.email.trim(), form.password, form.displayName.trim() || form.email.split('@')[0])
      navigate('/', { replace: true })
    } catch (cause) {
      setError(
        cause instanceof ApiError
          ? (Object.values(cause.errors).flat()[0] ?? t('common.error'))
          : t('common.error'),
      )
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
        <h2>{t('auth.signUp')}</h2>

        <label className="field">
          <span>{t('auth.displayName')}</span>
          <input value={form.displayName} onChange={update('displayName')} autoComplete="name" />
        </label>

        <label className="field">
          <span>{t('auth.email')}</span>
          <input
            type="email"
            value={form.email}
            onChange={update('email')}
            autoComplete="email"
            required
          />
        </label>

        <label className="field">
          <span>{t('auth.password')}</span>
          <input
            type="password"
            value={form.password}
            onChange={update('password')}
            autoComplete="new-password"
            minLength={8}
            required
          />
          <small>{t('auth.passwordHint')}</small>
        </label>

        {error ? <p className="alert alert--error">{error}</p> : null}

        <button type="submit" className="button button--primary" disabled={busy}>
          {busy ? t('common.loading') : t('auth.submitRegister')}
        </button>

        <p className="auth__switch">
          {t('auth.haveAccount')}{' '}
          <Link to="/login">{t('auth.signIn')}</Link>
        </p>
      </form>
    </div>
  )
}
