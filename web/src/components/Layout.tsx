import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { useI18n, type TranslationKey } from '../i18n'
import { LanguageToggle } from './LanguageToggle'

interface NavItem {
  to: string
  labelKey: TranslationKey
  badge?: number
}

export function Layout() {
  const { t } = useI18n()
  const { user, signOut } = useAuth()
  const navigate = useNavigate()

  const items: NavItem[] = [
    { to: '/', labelKey: 'nav.dashboard' },
    { to: '/courses', labelKey: 'nav.catalog' },
    { to: '/grammar', labelKey: 'nav.grammar' },
    { to: '/dictionary', labelKey: 'nav.dictionary' },
    { to: '/my-words', labelKey: 'nav.myWords' },
    { to: '/practice', labelKey: 'nav.practice' },
  ]

  if (user && (user.role === 'Admin' || user.role === 'Teacher')) {
    items.push({ to: '/admin', labelKey: 'nav.admin' })
  }

  const handleSignOut = async () => {
    await signOut()
    navigate('/login', { replace: true })
  }

  return (
    <div className="app">
      <header className="app__header">
        <div className="app__brand">
          <span className="app__logo" aria-hidden="true">
            R
          </span>
          <span className="app__names">
            <strong>{t('app.name')}</strong>
            <small>{t('app.tagline')}</small>
          </span>
        </div>

        <div className="app__header-actions">
          <LanguageToggle />
          {user ? (
            <div className="app__user">
              <span className="app__user-name">{user.displayName}</span>
              <span className="badge badge--level">{user.level}</span>
              <span className="badge badge--xp" title="XP">
                {user.totalXp} XP
              </span>
              <button type="button" className="button button--ghost" onClick={handleSignOut}>
                {t('nav.signOut')}
              </button>
            </div>
          ) : null}
        </div>
      </header>

      <nav className="app__nav" aria-label={t('app.name')}>
        {items.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.to === '/'}
            className={({ isActive }) => `app__nav-link${isActive ? ' app__nav-link--active' : ''}`}
          >
            {t(item.labelKey)}
          </NavLink>
        ))}
      </nav>

      <main className="app__main">
        <Outlet />
      </main>

      <footer className="app__footer">
        <span>
          {t('app.name')} · {user?.targetLanguageName ?? t('app.tagline')}
        </span>
      </footer>
    </div>
  )
}
