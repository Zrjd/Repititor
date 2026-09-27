const ACCESS_KEY = 'repetitor.accessToken'
const REFRESH_KEY = 'repetitor.refreshToken'

export const tokenStore = {
  get access() {
    return localStorage.getItem(ACCESS_KEY)
  },
  get refresh() {
    return localStorage.getItem(REFRESH_KEY)
  },
  set(access: string, refresh: string) {
    localStorage.setItem(ACCESS_KEY, access)
    localStorage.setItem(REFRESH_KEY, refresh)
  },
  clear() {
    localStorage.removeItem(ACCESS_KEY)
    localStorage.removeItem(REFRESH_KEY)
  },
}

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  code?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly errors: Record<string, string[]>
  readonly problem: ProblemDetails

  constructor(status: number, problem: ProblemDetails) {
    const firstFieldError = problem.errors ? Object.values(problem.errors).flat()[0] : undefined
    super(firstFieldError ?? problem.detail ?? problem.title ?? `HTTP ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.code = problem.code ?? `http_${status}`
    this.errors = problem.errors ?? {}
    this.problem = problem
  }

  get isUnauthorized() {
    return this.status === 401
  }
}

const baseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

export function apiUrl(path: string) {
  return `${baseUrl}/api/v1${path}`
}

type UnauthorizedHandler = () => void

let onUnauthorized: UnauthorizedHandler | null = null
let refreshInFlight: Promise<boolean> | null = null

export function setUnauthorizedHandler(handler: UnauthorizedHandler | null) {
  onUnauthorized = handler
}

async function readProblem(response: Response): Promise<ProblemDetails> {
  const text = await response.text()
  if (!text) {
    return { status: response.status }
  }

  try {
    return JSON.parse(text) as ProblemDetails
  } catch {
    return { status: response.status, detail: text }
  }
}

async function rawRequest(path: string, init: RequestInit, access: string | null): Promise<Response> {
  const headers = new Headers(init.headers)
  if (!headers.has('Content-Type') && init.body) {
    headers.set('Content-Type', 'application/json')
  }
  if (access) {
    headers.set('Authorization', `Bearer ${access}`)
  }

  return fetch(apiUrl(path), { ...init, headers })
}

async function rotateTokens(): Promise<boolean> {
  const refresh = tokenStore.refresh
  if (!refresh) {
    return false
  }

  const response = await rawRequest('/auth/refresh', {
    method: 'POST',
    body: JSON.stringify({ refreshToken: refresh }),
  }, null)

  if (!response.ok) {
    return false
  }

  const data = (await response.json()) as { accessToken: string; refreshToken: string }
  tokenStore.set(data.accessToken, data.refreshToken)
  return true
}

async function refreshOnce(): Promise<boolean> {
  refreshInFlight ??= rotateTokens().finally(() => {
    refreshInFlight = null
  })
  return refreshInFlight
}

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'
  body?: unknown
  query?: Record<string, string | number | boolean | undefined | null>
  signal?: AbortSignal
  auth?: boolean
}

export function buildQuery(query: RequestOptions['query']): string {
  if (!query) {
    return ''
  }

  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') {
      continue
    }
    params.set(key, String(value))
  }

  const search = params.toString()
  return search ? `?${search}` : ''
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, query, signal, auth = true } = options
  const init: RequestInit = { method, signal }
  if (body !== undefined) {
    init.body = JSON.stringify(body)
  }

  const url = `${path}${buildQuery(query)}`
  const useAuth = auth && Boolean(tokenStore.access ?? tokenStore.refresh)
  let response = await rawRequest(url, init, useAuth ? tokenStore.access : null)

  if (response.status === 401 && useAuth) {
    if (await refreshOnce()) {
      response = await rawRequest(url, init, tokenStore.access)
    } else {
      tokenStore.clear()
      onUnauthorized?.()
    }
  }

  if (response.status === 204) {
    return undefined as T
  }

  if (!response.ok) {
    throw new ApiError(response.status, await readProblem(response))
  }

  const contentType = response.headers.get('content-type') ?? ''
  if (!contentType.includes('application/json')) {
    return undefined as T
  }

  return (await response.json()) as T
}

export const api = {
  get: <T>(path: string, options?: Omit<RequestOptions, 'method' | 'body'>) =>
    request<T>(path, { ...options, method: 'GET' }),
  post: <T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'method' | 'body'>) =>
    request<T>(path, { ...options, method: 'POST', body }),
  put: <T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'method' | 'body'>) =>
    request<T>(path, { ...options, method: 'PUT', body }),
  patch: <T>(path: string, body?: unknown, options?: Omit<RequestOptions, 'method' | 'body'>) =>
    request<T>(path, { ...options, method: 'PATCH', body }),
  delete: <T>(path: string, options?: Omit<RequestOptions, 'method' | 'body'>) =>
    request<T>(path, { ...options, method: 'DELETE' }),
}
