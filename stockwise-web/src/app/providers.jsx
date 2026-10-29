import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { api, clearAuthInfo, setAuthInfo, tryRefresh } from '../lib/api-client.js'

const AuthContext = createContext(null)

export function AuthProvider({ children }) {
  const [me, setMe] = useState(null) // { user, activeOrganizationId, activeOrganizationName, organizations, permissions }
  const [status, setStatus] = useState('loading') // loading | authenticated | unauthenticated
  const [currency, setCurrency] = useState('USD')
  const [dateFormat, setDateFormat] = useState('yyyy-MM-dd')

  const applyMe = useCallback(async () => {
    const data = await api.get('/api/v1/auth/me')
    setMe(data)
    setAuthInfo({
      organizationId: data.activeOrganizationId,
      permissions: data.permissions
    })
    try {
      const settings = await api.get('/api/v1/settings')
      setCurrency(settings.currencyCode)
      setDateFormat(settings.dateFormat)
    } catch {
      // settings endpoint requires permission; fall back silently
    }
    setStatus('authenticated')
    return data
  }, [])

  // On first load: silently exchange the refresh cookie for an access token.
  useEffect(() => {
    ;(async () => {
      const ok = await tryRefresh()
      if (!ok) {
        setStatus('unauthenticated')
        return
      }
      try {
        await applyMe()
      } catch {
        clearAuthInfo()
        setStatus('unauthenticated')
      }
    })()
  }, [applyMe])

  const login = useCallback(async (email, password) => {
    const res = await api.post('/api/v1/auth/login', { email, password })
    setAuthInfo(res)
    await applyMe()
  }, [applyMe])

  const register = useCallback(async (payload) => {
    const res = await api.post('/api/v1/auth/register', payload)
    setAuthInfo(res)
    await applyMe()
  }, [applyMe])

  const logout = useCallback(async () => {
    try {
      await api.post('/api/v1/auth/logout', {})
    } catch (e) {
      // ignore network errors on logout
    } finally {
      clearAuthInfo()
      localStorage.removeItem('stockwise:warehouse')
      setMe(null)
      setStatus('unauthenticated')
    }
  }, [])

  const switchOrganization = useCallback(async (orgId) => {
    const res = await api.post('/api/v1/auth/switch-organization', { organizationId: orgId })
    setAuthInfo(res)
    await applyMe()
  }, [applyMe])

  const refreshMe = useCallback(async () => {
    await applyMe()
  }, [applyMe])

  const value = useMemo(() => ({
    me,
    status,
    currency,
    dateFormat,
    user: me?.user,
    orgId: me?.activeOrganizationId,
    orgName: me?.activeOrganizationName,
    organizations: me?.organizations || [],
    permissions: me?.permissions || [],
    hasPermission: (p) => !p || (me?.permissions || []).includes(p),
    login,
    register,
    logout,
    switchOrganization,
    refreshMe
  }), [me, status, currency, dateFormat, login, register, logout, switchOrganization, refreshMe])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}
