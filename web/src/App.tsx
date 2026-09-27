import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Navigate, Route, Routes, useLocation } from 'react-router-dom'
import type { ReactNode } from 'react'
import { AuthProvider, useAuth } from './auth/AuthContext'
import { Layout } from './components/Layout'
import { Spinner } from './components/Feedback'
import { I18nProvider } from './i18n'
import { AdminPage } from './pages/AdminPage'
import { CatalogPage } from './pages/CatalogPage'
import { CoursePage, LessonPage } from './pages/CoursePage'
import { DashboardPage } from './pages/DashboardPage'
import { DictionaryPage, WordPage } from './pages/DictionaryPage'
import { GrammarPage } from './pages/GrammarPage'
import { LoginPage } from './pages/LoginPage'
import { MyWordsPage } from './pages/MyWordsPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { PracticePage } from './pages/PracticePage'
import { RegisterPage } from './pages/RegisterPage'
import { ReviewSessionPage } from './pages/ReviewSessionPage'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
})

function Protected({ children }: { children: ReactNode }) {
  const { isAuthenticated, isBootstrapping } = useAuth()
  const location = useLocation()

  if (isBootstrapping) {
    return <Spinner />
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  }

  return <>{children}</>
}

function GuestOnly({ children }: { children: ReactNode }) {
  const { isAuthenticated, isBootstrapping } = useAuth()
  if (isBootstrapping) {
    return <Spinner />
  }
  return isAuthenticated ? <Navigate to="/" replace /> : <>{children}</>
}

export default function App() {
  return (
    <I18nProvider>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          <AuthProvider>
            <Routes>
              <Route
                path="/login"
                element={
                  <GuestOnly>
                    <LoginPage />
                  </GuestOnly>
                }
              />
              <Route
                path="/register"
                element={
                  <GuestOnly>
                    <RegisterPage />
                  </GuestOnly>
                }
              />

              <Route
                element={
                  <Protected>
                    <Layout />
                  </Protected>
                }
              >
                <Route index element={<DashboardPage />} />
                <Route path="admin" element={<AdminPage />} />
                <Route path="courses" element={<CatalogPage />} />
                <Route path="courses/:slug" element={<CoursePage />} />
                <Route path="lessons/:lessonId" element={<LessonPage />} />
                <Route path="grammar" element={<GrammarPage />} />
                <Route path="dictionary" element={<DictionaryPage />} />
                <Route path="dictionary/:wordId" element={<WordPage />} />
                <Route path="my-words" element={<MyWordsPage />} />
                <Route path="practice" element={<PracticePage />} />
                <Route path="practice/session" element={<ReviewSessionPage />} />
                <Route path="*" element={<NotFoundPage />} />
              </Route>
            </Routes>
          </AuthProvider>
        </BrowserRouter>
      </QueryClientProvider>
    </I18nProvider>
  )
}
