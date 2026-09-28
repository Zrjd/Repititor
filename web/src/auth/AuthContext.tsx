import { useQueryClient } from '@tanstack/react-query'
import { createContext, use, useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { ApiError, setUnauthorizedHandler, tokenStore } from '../api/client'
import { authApi } from '../api/endpoints'
import type { AuthResponse, SignupRole, UserProfile } from '../api/types'

interface AuthValue {
  user: UserProfile | null
  isAuthenticated: boolean
  isBootstrapping: boolean
  signIn: (email: string, password: string) => Promise<void>
  signUp: (email: string, password: string, displayName: string, role: SignupRole) => Promise<void>
  signOut: (everywhere?: boolean) => Promise<void>
  refreshUser: () => Promise<void>
  patchUser: (patch: Partial<UserProfile>) => void
}

const AuthContext = createContext<AuthValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserProfile | null>(null)
  const [isBootstrapping, setIsBootstrapping] = useState(Boolean(tokenStore.access ?? tokenStore.refresh))
  const queryClient = useQueryClient()

  const signOut = useCallback(
    async (everywhere = false) => {
      try {
        if (everywhere) {
          await authApi.logoutAll()
        } else if (tokenStore.refresh) {
          await authApi.logout(tokenStore.refresh)
        }
      } catch {
        // Revocation is best effort: the local session is dropped either way.
      } finally {
        tokenStore.clear()
        setUser(null)
        queryClient.clear()
      }
    },
    [queryClient],
  )

  useEffect(() => {
    setUnauthorizedHandler(() => {
      setUser(null)
      queryClient.clear()
    })
    return () => setUnauthorizedHandler(null)
  }, [queryClient])

  useEffect(() => {
    if (!tokenStore.access && !tokenStore.refresh) {
      return
    }

    let cancelled = false
    authApi
      .me()
      .then((profile) => {
        if (!cancelled) {
          setUser(profile)
        }
      })
      .catch((error: unknown) => {
        if (error instanceof ApiError && error.isUnauthorized) {
          tokenStore.clear()
        }
      })
      .finally(() => {
        if (!cancelled) {
          setIsBootstrapping(false)
        }
      })

    return () => {
      cancelled = true
    }
  }, [])

  const applySession = useCallback((session: AuthResponse) => {
    tokenStore.set(session.accessToken, session.refreshToken)
    setUser(session.user)
  }, [])

  const value = useMemo<AuthValue>(
    () => ({
      user,
      isAuthenticated: user !== null,
      isBootstrapping,
      signIn: async (email, password) => {
        applySession(await authApi.login(email, password))
      },
      signUp: async (email, password, displayName, role) => {
        applySession(await authApi.register(email, password, displayName, role))
      },
      signOut,
      refreshUser: async () => {
        setUser(await authApi.me())
      },
      patchUser: (patch) => {
        setUser((prev) => (prev ? { ...prev, ...patch } : prev))
      },
    }),
    [user, isBootstrapping, applySession, signOut],
  )

  return <AuthContext value={value}>{children}</AuthContext>
}

export function useAuth() {
  const context = use(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used inside AuthProvider')
  }
  return context
}
