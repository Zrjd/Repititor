import { useI18n, type Locale } from '../i18n'

export function LanguageToggle() {
  const { locale, toggle, t } = useI18n()
  const next: Locale = locale === 'ru' ? 'en' : 'ru'

  return (
    <button
      type="button"
      className="lang-toggle"
      onClick={toggle}
      title={`${t('nav.language')}: ${locale.toUpperCase()}`}
      aria-label={`${t('nav.language')}: ${locale === 'ru' ? 'русский' : 'English'}`}
    >
      <span aria-hidden="true">{locale === 'ru' ? 'RU' : 'EN'}</span>
      <span className="lang-toggle__sep" aria-hidden="true">
        /
      </span>
      <span className={locale === 'ru' ? 'lang-toggle__inactive' : undefined}>{next === 'ru' ? 'RU' : 'EN'}</span>
    </button>
  )
}
