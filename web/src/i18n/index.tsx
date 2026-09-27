import { createContext, use, useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { en } from './en'
import { ru, type Dictionary } from './ru'

export type Locale = 'ru' | 'en'

const STORAGE_KEY = 'repetitor.locale'

type Path = string
type Leaves<T> = T extends string
  ? Path
  : T extends readonly (string | number)[]
    ? never
    : { [K in keyof T]: Leaves<T[K]> }[keyof T]
type TranslationKey = Leaves<Dictionary>
type PluralKey = keyof Dictionary['plural']

const dictionaries: Record<Locale, Dictionary> = { ru, en }

const pluralOrder: Record<Locale, string[]> = {
  ru: ['one', 'few', 'many', 'other'],
  en: ['one', 'other'],
}

interface I18nValue {
  locale: Locale
  setLocale: (locale: Locale) => void
  toggle: () => void
  t: (key: TranslationKey, params?: Record<string, string | number>) => string
  plural: (key: PluralKey, count: number) => string
  formatNumber: (value: number) => string
  formatDate: (value: string, options?: Intl.DateTimeFormatOptions) => string
}

const I18nContext = createContext<I18nValue | null>(null)

function readLocale(): Locale {
  const stored = localStorage.getItem(STORAGE_KEY)
  if (stored === 'ru' || stored === 'en') {
    return stored
  }
  return navigator.language.toLowerCase().startsWith('en') ? 'en' : 'ru'
}

function resolve(dictionary: Dictionary, key: string): string | undefined {
  let node: unknown = dictionary
  for (const part of key.split('.')) {
    if (node === null || typeof node !== 'object' || !(part in node)) {
      return undefined
    }
    node = (node as Record<string, unknown>)[part]
  }
  return typeof node === 'string' ? node : undefined
}

export function I18nProvider({ children }: { children: ReactNode }) {
  const [locale, setLocaleState] = useState<Locale>(readLocale)

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, locale)
    document.documentElement.lang = locale
  }, [locale])

  const setLocale = useCallback((next: Locale) => setLocaleState(next), [])
  const toggle = useCallback(
    () => setLocaleState((prev) => (prev === 'ru' ? 'en' : 'ru')),
    [],
  )

  const value = useMemo<I18nValue>(() => {
    const dictionary = dictionaries[locale]
    return {
      locale,
      setLocale,
      toggle,
      t: (key, params) => {
        const template = resolve(dictionary, key) ?? resolve(ru, key) ?? key
        if (!params) {
          return template
        }
        return Object.entries(params).reduce(
          (acc, [name, replacement]) => acc.replaceAll(`{${name}}`, String(replacement)),
          template,
        )
      },
      formatNumber: (input) => new Intl.NumberFormat(locale).format(input),
      plural: (key, count) => {
        const forms = dictionary.plural[key] ?? ru.plural[key]
        const category = new Intl.PluralRules(locale).select(count)
        const order = pluralOrder[locale]
        let index = order.indexOf(category)
        if (index < 0) {
          index = order.length - 1
        }
        return forms[Math.min(index, forms.length - 1)]
      },
      formatDate: (input, options) =>
        new Intl.DateTimeFormat(locale, options ?? { dateStyle: 'medium' }).format(new Date(input)),
    }
  }, [locale, setLocale, toggle])

  return <I18nContext value={value}>{children}</I18nContext>
}

export function useI18n() {
  const context = use(I18nContext)
  if (!context) {
    throw new Error('useI18n must be used inside I18nProvider')
  }
  return context
}

export type { TranslationKey }
