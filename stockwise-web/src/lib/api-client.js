// Central API client. All fetch calls go through here so auth, errors and
// tenant scoping behave consistently. No component calls fetch directly.

let accessToken = null
let accessTokenExpiresAt = null
let orgId = null
let permissions = []

export function setAuthInfo({ accessToken: token, expiresAt, organizationId, permissions: perms }) {
  accessToken = token || null
  accessTokenExpiresAt = expiresAt ? new Date(expiresAt) : null
  orgId = organizationId || orgId
  permissions = perms || []
}

export function setAccessToken(token) {
  accessToken = token
}

export function clearAuthInfo() {
  accessToken = null
  accessTokenExpiresAt = null
  orgId = null
  permissions = []
}

export function getOrgId() {
  return orgId
}

export function setOrgId(id) {
  orgId = id
}

export function hasPermission(permission) {
  if (!permission) return true
  return permissions.includes(permission)
}

export class ApiError extends Error {
  constructor(status, detail, errorCode, errors) {
    super(detail)
    this.status = status
    this.errorCode = errorCode
    this.errors = errors
  }
}

async function request(path, { method = 'GET', body, formData, signal, retryOn401 = true } = {}) {
  const headers = {}
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`
  if (body !== undefined) headers['Content-Type'] = 'application/json'

  const res = await fetch(path, {
    method,
    headers,
    body: formData ? formData : body !== undefined ? JSON.stringify(body) : undefined,
    signal,
    credentials: 'same-origin'
  })

  if (res.status === 401 && retryOn401 && !path.startsWith('/api/v1/auth/')) {
    // Silent refresh, then retry once.
    const refreshed = await tryRefresh()
    if (refreshed) return request(path, { method, body, formData, signal, retryOn401: false })
  }

  if (res.status === 204) return null

  const contentType = res.headers.get('content-type') || ''
  if (!contentType.includes('application/json')) {
    if (!res.ok) throw new ApiError(res.status, res.statusText || 'Request failed', 'http_error')
    return res.blob()
  }

  const data = await res.json().catch(() => ({}))
  if (!res.ok) {
    throw new ApiError(
      res.status,
      data.detail || data.title || 'Request failed',
      data.title,
      data.errors
    )
  }
  return data
}

export async function tryRefresh() {
  try {
    const res = await fetch('/api/v1/auth/refresh', {
      method: 'POST',
      credentials: 'same-origin'
    })
    if (!res.ok) return false
    const data = await res.json()
    setAuthInfo(data)
    return true
  } catch {
    return false
  }
}

export const api = {
  get: (path, signal) => request(path, { signal }),
  post: (path, body, signal) => request(path, { method: 'POST', body, signal }),
  postForm: (path, formData, signal) => request(path, { method: 'POST', formData, signal }),
  put: (path, body, signal) => request(path, { method: 'PUT', body, signal }),
  patch: (path, body, signal) => request(path, { method: 'PATCH', body, signal }),
  delete: (path, signal) => request(path, { method: 'DELETE', signal })
}

// Query-string helper
export function qs(params) {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params || {})) {
    if (value !== undefined && value !== null && value !== '') search.set(key, value)
  }
  const s = search.toString()
  return s ? `?${s}` : ''
}
