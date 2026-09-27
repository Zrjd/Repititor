import { Link } from 'react-router-dom'
import { useI18n } from '../i18n'

export function NotFoundPage() {
  const { t } = useI18n()
  return (
    <div className="state state--empty">
      <p className="state__title">{t('common.notFound')}</p>
      <p className="state__detail">{t('common.notFoundHint')}</p>
      <Link className="button" to="/">
        {t('nav.dashboard')}
      </Link>
    </div>
  )
}
