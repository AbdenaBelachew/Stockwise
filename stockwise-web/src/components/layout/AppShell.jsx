import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  LayoutDashboard, Package, FolderTree, Warehouse, Layers, ArrowLeftRight, ClipboardCheck,
  Truck, ShoppingCart, FileText, Bell, Users, Settings, ChevronDown, ChevronRight, ChevronLeft,
  Menu, X, Search, PackageCheck, Boxes, ClipboardList, LogOut, Building2, ShieldCheck,
  Scale, Check, Sun, Moon, Command, User, MapPin, Activity, SlidersHorizontal
} from 'lucide-react'
import { useAuth } from '../../app/providers.jsx'
import { P } from '../../lib/permissions.js'
import { api } from '../../lib/api-client.js'

function NavItem({ to, icon: Icon, label, end, collapsed, badge }) {
  return (
    <NavLink
      to={to}
      end={end}
      className={({ isActive }) =>
        `group relative flex items-center gap-3 rounded-xl px-3 py-2 text-sm font-medium transition-all duration-150 ${
          collapsed ? 'justify-center px-0 h-10 w-10 mx-auto' : ''
        } ${
          isActive
            ? 'bg-emerald-50/90 text-emerald-900 font-semibold shadow-xs border border-emerald-200/50 dark:bg-emerald-950/40 dark:text-emerald-300 dark:border-emerald-800/40'
            : 'text-slate-600 hover:bg-slate-100/80 hover:text-slate-900 dark:text-slate-400 dark:hover:bg-slate-800/60 dark:hover:text-slate-100 border border-transparent'
        }`
      }
    >
      {({ isActive }) => (
        <>
          {/* Active indicator bar */}
          <span
            aria-hidden="true"
            className={`absolute left-0 top-1/2 h-5 w-1 -translate-y-1/2 rounded-r-full bg-emerald-600 transition-all duration-200 dark:bg-emerald-400 ${
              isActive ? 'opacity-100 scale-y-100' : 'opacity-0 scale-y-50'
            }`}
          />

          <Icon
            className={`h-[18px] w-[18px] shrink-0 transition-transform duration-150 group-hover:scale-105 ${
              isActive ? 'text-emerald-600 dark:text-emerald-400' : 'text-slate-400 group-hover:text-slate-600 dark:text-slate-500 dark:group-hover:text-slate-300'
            }`}
            strokeWidth={isActive ? 2 : 1.75}
          />

          {!collapsed && (
            <span className="truncate flex-1">{label}</span>
          )}

          {!collapsed && badge != null && (
            <span className="ml-auto flex h-5 min-w-5 items-center justify-center rounded-full bg-emerald-100 px-1.5 text-[11px] font-semibold text-emerald-700 dark:bg-emerald-900/50 dark:text-emerald-300">
              {badge}
            </span>
          )}

          {/* Floating tooltip in collapsed mode */}
          {collapsed && (
            <div className="pointer-events-none absolute left-full ml-3 z-50 hidden rounded-md bg-slate-900 px-2.5 py-1 text-xs font-medium text-white shadow-lg dark:bg-slate-800 dark:border dark:border-slate-700/60 group-hover:flex items-center gap-1.5 whitespace-nowrap">
              <span>{label}</span>
              {badge != null && (
                <span className="rounded bg-emerald-500/20 px-1 text-[10px] text-emerald-300">{badge}</span>
              )}
            </div>
          )}
        </>
      )}
    </NavLink>
  )
}

function NavGroup({ title, children, collapsed }) {
  if (collapsed) {
    return (
      <div className="my-2 px-3" title={title}>
        <div className="border-t border-slate-200/70 dark:border-slate-800" aria-label={title} />
      </div>
    )
  }
  return (
    <div className="mb-2">
      <div className="mb-1.5 mt-3 px-3 flex items-center justify-between">
        <span className="text-[11px] font-bold uppercase tracking-wider text-slate-400 dark:text-slate-500">
          {title}
        </span>
      </div>
      <div className="space-y-0.5">{children}</div>
    </div>
  )
}

export default function AppShell() {
  const auth = useAuth()
  const location = useLocation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem('stockwise:sidebar-collapsed') === 'true')
  const [userMenuOpen, setUserMenuOpen] = useState(false)
  const [orgMenuOpen, setOrgMenuOpen] = useState(false)
  const [warehouseMenuOpen, setWarehouseMenuOpen] = useState(false)
  const [quickSearchOpen, setQuickSearchOpen] = useState(false)
  const [quickSearchQuery, setQuickSearchQuery] = useState('')
  const [warehouse, setWarehouse] = useState(() => localStorage.getItem('stockwise:warehouse') || '')

  // Dark mode state
  const [isDark, setIsDark] = useState(() => {
    if (typeof window === 'undefined') return false
    const saved = localStorage.getItem('stockwise:theme')
    if (saved) return saved === 'dark'
    return window.matchMedia('(prefers-color-scheme: dark)').matches
  })

  // Synchronize dark mode class
  useEffect(() => {
    if (isDark) {
      document.documentElement.classList.add('dark')
      localStorage.setItem('stockwise:theme', 'dark')
    } else {
      document.documentElement.classList.remove('dark')
      localStorage.setItem('stockwise:theme', 'light')
    }
  }, [isDark])

  const toggleTheme = () => setIsDark((prev) => !prev)

  // Persist sidebar collapsed state
  const handleToggleCollapse = () => {
    setCollapsed((prev) => {
      const next = !prev
      localStorage.setItem('stockwise:sidebar-collapsed', String(next))
      return next
    })
  }

  // Close mobile sidebar on route change
  useEffect(() => setSidebarOpen(false), [location.pathname])

  // Global keyboard shortcut for quick search (Ctrl+K or Cmd+K) and sidebar toggle ([)
  useEffect(() => {
    const handleKeyDown = (e) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault()
        setQuickSearchOpen((open) => !open)
      } else if (e.key === '[' && !['INPUT', 'TEXTAREA'].includes(e.target.tagName)) {
        e.preventDefault()
        handleToggleCollapse()
      } else if (e.key === 'Escape') {
        setQuickSearchOpen(false)
        setUserMenuOpen(false)
        setOrgMenuOpen(false)
        setWarehouseMenuOpen(false)
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [])

  const { data: warehouses } = useQuery({
    queryKey: ['warehouses'],
    queryFn: () => api.get('/api/v1/warehouses')
  })

  useEffect(() => {
    if (!warehouse && warehouses?.length) {
      setWarehouse(warehouses[0].id)
    }
  }, [warehouses, warehouse])

  const { data: unreadCount } = useQuery({
    queryKey: ['notifications', 'unread-count'],
    queryFn: () => api.get('/api/v1/notifications/unread-count'),
    refetchInterval: 60000
  })

  const can = auth.hasPermission

  const switchOrg = useMutation({
    mutationFn: (orgId) => auth.switchOrganization(orgId),
    onSuccess: () => {
      queryClient.clear()
      navigate('/')
      setOrgMenuOpen(false)
    },
    onError: (e) => alert(e.message)
  })

  const handleLogout = async () => {
    setUserMenuOpen(false)
    setOrgMenuOpen(false)
    setWarehouseMenuOpen(false)
    setSidebarOpen(false)
    try {
      await auth.logout()
    } finally {
      queryClient.clear()
      navigate('/login', { replace: true })
    }
  }

  const activeWarehouseObj = useMemo(() => {
    if (!warehouses || !warehouse) return null
    return warehouses.find((w) => w.id === warehouse) || null
  }, [warehouses, warehouse])

  // Quick search items indexed for command palette
  const searchableRoutes = useMemo(() => {
    const list = [
      { to: '/', label: 'Overview & Analytics', group: 'Dashboard', icon: LayoutDashboard },
      { to: '/products', label: 'Products & SKUs', group: 'Inventory', icon: Package, perm: P.ProductsRead },
      { to: '/categories', label: 'Categories & Units of Measure', group: 'Inventory', icon: FolderTree, perm: P.CatalogManage },
      { to: '/warehouses', label: 'Warehouses & Locations', group: 'Inventory', icon: Warehouse, perm: P.WarehousesRead },
      { to: '/stock-on-hand', label: 'Stock on Hand (Balances)', group: 'Inventory', icon: Boxes, perm: P.InventoryRead },
      { to: '/movements', label: 'Stock Movements (Audit Ledger)', group: 'Inventory', icon: ClipboardList, perm: P.InventoryRead },
      { to: '/batches', label: 'Batches & FEFO Expiry', group: 'Inventory', icon: Layers, perm: P.InventoryRead },
      { to: '/adjustments', label: 'Stock Adjustments', group: 'Inventory', icon: Scale, perm: P.InventoryAdjust },
      { to: '/transfers', label: 'Warehouse Transfers', group: 'Inventory', icon: ArrowLeftRight, perm: P.InventoryTransfer },
      { to: '/stock-counts', label: 'Physical Stock Counts', group: 'Inventory', icon: ClipboardCheck, perm: P.InventoryCount },
      { to: '/suppliers', label: 'Suppliers & Vendors', group: 'Procurement', icon: Truck, perm: P.SuppliersRead },
      { to: '/purchase-orders', label: 'Purchase Orders', group: 'Procurement', icon: ShoppingCart, perm: P.PurchaseOrdersRead },
      { to: '/goods-receipts', label: 'Goods Receipts & Receiving', group: 'Procurement', icon: PackageCheck, perm: P.ReceiptsCreate },
      { to: '/reports', label: 'Reports & Export Analytics', group: 'Insights', icon: FileText, perm: P.ReportsRead },
      { to: '/audit-logs', label: 'Security & Audit Logs', group: 'Insights', icon: ShieldCheck, perm: P.AuditLogsRead },
      { to: '/notifications', label: 'Notifications & Alerts', group: 'Account', icon: Bell, perm: P.NotificationsRead },
      { to: '/team', label: 'Team Members & Role Access', group: 'Administration', icon: Users, perm: P.UsersManage },
      { to: '/settings', label: 'Organization Settings', group: 'Administration', icon: Settings },
      { to: '/settings/profile', label: 'User Profile & Password', group: 'Administration', icon: User }
    ]
    return list.filter((item) => !item.perm || can(item.perm))
  }, [can])

  const filteredRoutes = useMemo(() => {
    if (!quickSearchQuery.trim()) return searchableRoutes
    const q = quickSearchQuery.toLowerCase()
    return searchableRoutes.filter(
      (r) => r.label.toLowerCase().includes(q) || r.group.toLowerCase().includes(q)
    )
  }, [searchableRoutes, quickSearchQuery])

  // Breadcrumbs generator based on location
  const breadcrumbInfo = useMemo(() => {
    const path = location.pathname
    if (path === '/') return { group: 'Overview', title: 'Dashboard' }
    const match = searchableRoutes.find((r) => r.to === path)
    if (match) return { group: match.group, title: match.label.split('(')[0].trim() }

    // Nested dynamic routes
    if (path.startsWith('/products/')) return { group: 'Inventory', title: 'Product Details' }
    if (path.startsWith('/transfers/')) return { group: 'Inventory', title: 'Transfer Details' }
    if (path.startsWith('/stock-counts/')) return { group: 'Inventory', title: 'Stock Count Details' }
    if (path.startsWith('/purchase-orders/')) return { group: 'Procurement', title: 'Purchase Order Details' }
    if (path.startsWith('/goods-receipts/')) return { group: 'Procurement', title: 'Goods Receipt Details' }
    if (path.startsWith('/settings/profile')) return { group: 'Administration', title: 'Profile & Security' }

    return { group: 'App', title: path.replace('/', '') }
  }, [location.pathname, searchableRoutes])

  // Current active organization details
  const activeOrg = auth.organizations.find((o) => o.id === auth.orgId) || null
  const userInitials = auth.user?.fullName?.split(' ').map((s) => s[0]).slice(0, 2).join('').toUpperCase() || 'U'

  // Navigation Links component
  const navContent = (
    <nav className="flex-1 space-y-0.5 overflow-y-auto px-3 py-2 scrollbar-thin scrollbar-thumb-slate-200 dark:scrollbar-thumb-slate-800" aria-label="Main navigation">
      <NavItem to="/" icon={LayoutDashboard} label="Overview" end collapsed={collapsed} />

      <NavGroup title="Inventory" collapsed={collapsed}>
        {can(P.ProductsRead) && <NavItem to="/products" icon={Package} label="Products" collapsed={collapsed} />}
        {can(P.CatalogManage) && <NavItem to="/categories" icon={FolderTree} label="Categories & Units" collapsed={collapsed} />}
        {can(P.WarehousesRead) && <NavItem to="/warehouses" icon={Warehouse} label="Warehouses" collapsed={collapsed} />}
        {can(P.InventoryRead) && <NavItem to="/stock-on-hand" icon={Boxes} label="Stock on hand" collapsed={collapsed} />}
        {can(P.InventoryRead) && <NavItem to="/movements" icon={ClipboardList} label="Movements" collapsed={collapsed} />}
        {can(P.InventoryRead) && <NavItem to="/batches" icon={Layers} label="Batches & expiry" collapsed={collapsed} />}
        {can(P.InventoryAdjust) && <NavItem to="/adjustments" icon={Scale} label="Adjustments" collapsed={collapsed} />}
        {can(P.InventoryTransfer) && <NavItem to="/transfers" icon={ArrowLeftRight} label="Transfers" collapsed={collapsed} />}
        {can(P.InventoryCount) && <NavItem to="/stock-counts" icon={ClipboardCheck} label="Stock counts" collapsed={collapsed} />}
      </NavGroup>

      <NavGroup title="Procurement" collapsed={collapsed}>
        {can(P.SuppliersRead) && <NavItem to="/suppliers" icon={Truck} label="Suppliers" collapsed={collapsed} />}
        {can(P.PurchaseOrdersRead) && <NavItem to="/purchase-orders" icon={ShoppingCart} label="Purchase orders" collapsed={collapsed} />}
        {can(P.ReceiptsCreate) && <NavItem to="/goods-receipts" icon={PackageCheck} label="Goods receiving" collapsed={collapsed} />}
      </NavGroup>

      {can(P.ReportsRead) && (
        <NavGroup title="Insights" collapsed={collapsed}>
          <NavItem to="/reports" icon={FileText} label="Reports" collapsed={collapsed} />
          {can(P.AuditLogsRead) && <NavItem to="/audit-logs" icon={ShieldCheck} label="Audit log" collapsed={collapsed} />}
        </NavGroup>
      )}

      <NavGroup title="Administration" collapsed={collapsed}>
        {can(P.UsersManage) && <NavItem to="/team" icon={Users} label="Team & roles" collapsed={collapsed} />}
        <NavItem to="/settings" icon={Settings} label="Settings" collapsed={collapsed} />
      </NavGroup>
    </nav>
  )

  return (
    <div className="flex h-screen overflow-hidden bg-slate-50 dark:bg-slate-950 text-slate-800 dark:text-slate-100">
      {/* ============================================================== */}
      {/* DESKTOP SIDEBAR */}
      {/* ============================================================== */}
      <aside
        className={`hidden shrink-0 flex-col border-r border-slate-200/80 bg-white shadow-xs transition-all duration-200 ease-in-out dark:border-slate-800 dark:bg-slate-900/95 lg:flex ${
          collapsed ? 'w-20' : 'w-64'
        }`}
      >
        {/* Brand header */}
        <div className={`flex h-16 items-center border-b border-slate-100 px-4 dark:border-slate-800/80 ${collapsed ? 'justify-center px-0' : 'justify-between'}`}>
          <Link to="/" className="flex items-center gap-3 group">
            <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-emerald-500 to-teal-700 text-white shadow-sm shadow-emerald-500/25 transition-transform duration-200 group-hover:scale-105">
              <Boxes className="h-5 w-5" strokeWidth={2.2} />
            </span>
            {!collapsed && (
              <div className="flex flex-col">
                <div className="flex items-center gap-1.5">
                  <span className="text-base font-bold tracking-tight text-slate-900 dark:text-white">
                    Stock<span className="text-emerald-600 dark:text-emerald-400">Wise</span>
                  </span>
                  <span className="rounded-md bg-emerald-50 px-1.5 py-0.5 text-[10px] font-semibold text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300">
                    Pro
                  </span>
                </div>
                <span className="text-[10px] font-medium text-slate-400">Inventory Ledger SaaS</span>
              </div>
            )}
          </Link>
        </div>

        {/* Workspace / Organization Switcher Card */}
        {auth.organizations.length > 0 && (
          <div className="relative p-2.5 border-b border-slate-100 dark:border-slate-800/60">
            <button
              onClick={() => { setOrgMenuOpen((o) => !o); setUserMenuOpen(false); setWarehouseMenuOpen(false) }}
              title={collapsed ? `${auth.orgName} (${activeOrg?.role || 'Org'})` : undefined}
              className={`w-full flex items-center gap-2.5 rounded-xl border border-slate-200/80 bg-slate-50/70 p-2 text-left text-sm transition-all hover:border-slate-300 hover:bg-slate-100/80 dark:border-slate-800 dark:bg-slate-800/40 dark:hover:border-slate-700 dark:hover:bg-slate-800/70 ${
                collapsed ? 'justify-center p-2' : ''
              }`}
            >
              <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-emerald-600/10 text-emerald-600 font-bold text-xs dark:bg-emerald-400/10 dark:text-emerald-400 border border-emerald-500/20">
                {auth.orgName?.[0]?.toUpperCase() || 'O'}
              </div>
              {!collapsed && (
                <>
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-xs font-semibold text-slate-800 dark:text-slate-200">{auth.orgName}</p>
                    <p className="truncate text-[10px] text-slate-400">{activeOrg?.role || 'Member'}</p>
                  </div>
                  <ChevronDown className="h-4 w-4 shrink-0 text-slate-400 transition-transform duration-150" />
                </>
              )}
            </button>

            {/* Organization Dropdown */}
            {orgMenuOpen && (
              <div className="absolute left-3 right-3 top-full z-40 mt-1.5 rounded-xl border border-slate-200 bg-white p-1.5 shadow-xl dark:border-slate-700 dark:bg-slate-900">
                <div className="px-2.5 py-1.5 text-[11px] font-bold uppercase tracking-wider text-slate-400">
                  Switch Organization
                </div>
                <div className="max-h-56 space-y-0.5 overflow-y-auto">
                  {auth.organizations.map((o) => (
                    <button
                      key={o.id}
                      className={`flex w-full items-center justify-between rounded-lg px-2.5 py-2 text-xs transition-colors ${
                        o.id === auth.orgId
                          ? 'bg-emerald-50 font-semibold text-emerald-900 dark:bg-emerald-950/50 dark:text-emerald-300'
                          : 'text-slate-700 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800'
                      }`}
                      onClick={() => switchOrg.mutate(o.id)}
                    >
                      <div className="flex items-center gap-2 truncate">
                        <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded bg-slate-200/70 text-[10px] font-bold text-slate-600 dark:bg-slate-700 dark:text-slate-300">
                          {o.name[0]?.toUpperCase()}
                        </span>
                        <span className="truncate">{o.name}</span>
                      </div>
                      <div className="flex items-center gap-1.5">
                        <span className="rounded bg-slate-100 px-1.5 py-0.5 text-[10px] text-slate-500 dark:bg-slate-800 dark:text-slate-400">
                          {o.role}
                        </span>
                        {o.id === auth.orgId && <Check className="h-3.5 w-3.5 text-emerald-600 dark:text-emerald-400" />}
                      </div>
                    </button>
                  ))}
                </div>
              </div>
            )}
          </div>
        )}

        {/* Sidebar Nav Links */}
        {navContent}

        {/* Sidebar Footer */}
        <div className="border-t border-slate-100 p-2.5 dark:border-slate-800/80">
          {!collapsed ? (
            <>
              <div className="mb-2 flex items-center justify-between px-2 text-[11px] text-slate-400">
                <span className="flex items-center gap-1.5">
                  <span className="h-2 w-2 rounded-full bg-emerald-500 animate-pulse" />
                  Live system
                </span>
                <span>v1.0.4</span>
              </div>

              {/* User snippet & Sign out in expanded sidebar */}
              <div className="mb-2 flex items-center justify-between rounded-xl border border-slate-200/70 bg-slate-50/70 p-2 dark:border-slate-800 dark:bg-slate-800/50">
                <div className="flex items-center gap-2 min-w-0">
                  <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-gradient-to-tr from-emerald-600 to-teal-500 text-xs font-bold text-white shadow-xs">
                    {userInitials}
                  </span>
                  <div className="min-w-0">
                    <p className="truncate text-xs font-semibold text-slate-800 dark:text-slate-200 leading-tight">
                      {auth.user?.fullName}
                    </p>
                    <p className="truncate text-[10px] text-slate-400 leading-tight">
                      {activeOrg?.role || 'User'}
                    </p>
                  </div>
                </div>
                <button
                  onClick={handleLogout}
                  title="Sign out"
                  className="rounded-lg p-1.5 text-slate-400 hover:bg-rose-50 hover:text-rose-600 dark:hover:bg-rose-950/40 dark:hover:text-rose-400 transition-colors"
                  aria-label="Sign out"
                >
                  <LogOut className="h-4 w-4" />
                </button>
              </div>

              <button
                onClick={handleToggleCollapse}
                className="w-full flex items-center justify-center gap-2 rounded-lg border border-slate-200/70 py-2 px-3 text-xs font-medium text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-800 dark:border-slate-800 dark:text-slate-400 dark:hover:bg-slate-800 dark:hover:text-slate-200"
                aria-label="Collapse sidebar"
              >
                <ChevronLeft className="h-4 w-4" />
                <div className="flex w-full items-center justify-between">
                  <span>Collapse menu</span>
                  <kbd className="rounded border border-slate-200 bg-slate-100 px-1 text-[10px] text-slate-400 dark:border-slate-700 dark:bg-slate-800 font-mono">
                    [
                  </kbd>
                </div>
              </button>
            </>
          ) : (
            <div className="flex flex-col items-center gap-2">
              <button
                onClick={handleLogout}
                title="Sign out"
                className="group relative flex h-9 w-9 items-center justify-center rounded-lg text-slate-400 hover:bg-rose-50 hover:text-rose-600 dark:hover:bg-rose-950/40 dark:hover:text-rose-400 transition-colors"
                aria-label="Sign out"
              >
                <LogOut className="h-4 w-4" />
                <div className="pointer-events-none absolute left-full ml-3 z-50 hidden rounded-md bg-slate-900 px-2.5 py-1 text-xs font-medium text-white shadow-lg dark:bg-slate-800 group-hover:block whitespace-nowrap">
                  Sign out
                </div>
              </button>

              <button
                onClick={handleToggleCollapse}
                className="flex h-9 w-9 items-center justify-center rounded-lg border border-slate-200/70 text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-800 dark:border-slate-800 dark:text-slate-400 dark:hover:bg-slate-800"
                aria-label="Expand sidebar"
                title="Expand sidebar ([)"
              >
                <ChevronLeft className="h-4 w-4 rotate-180" />
              </button>
            </div>
          )}
        </div>
      </aside>

      {/* ============================================================== */}
      {/* MOBILE DRAWER */}
      {/* ============================================================== */}
      {sidebarOpen && (
        <div className="fixed inset-0 z-50 lg:hidden">
          <div className="fixed inset-0 bg-slate-900/60 backdrop-blur-xs transition-opacity" onClick={() => setSidebarOpen(false)} />
          <aside className="fixed inset-y-0 left-0 flex w-72 flex-col bg-white shadow-2xl dark:bg-slate-900">
            <div className="flex h-16 items-center justify-between border-b border-slate-100 px-4 dark:border-slate-800">
              <div className="flex items-center gap-2.5">
                <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-emerald-600 text-white shadow-sm">
                  <Boxes className="h-4 w-4" />
                </span>
                <span className="font-bold text-slate-900 dark:text-white">StockWise</span>
              </div>
              <button
                onClick={() => setSidebarOpen(false)}
                className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-slate-800"
                aria-label="Close menu"
              >
                <X className="h-5 w-5" />
              </button>
            </div>
            {navContent}
            {/* Mobile User & Logout Footer */}
            <div className="border-t border-slate-100 p-4 dark:border-slate-800">
              <div className="flex items-center gap-3 mb-3">
                <span className="flex h-9 w-9 items-center justify-center rounded-lg bg-gradient-to-tr from-emerald-600 to-teal-500 text-xs font-bold text-white shadow-xs">
                  {userInitials}
                </span>
                <div className="min-w-0 flex-1">
                  <p className="truncate text-xs font-semibold text-slate-900 dark:text-white leading-tight">
                    {auth.user?.fullName}
                  </p>
                  <p className="truncate text-[11px] text-slate-400 leading-tight">
                    {auth.user?.email}
                  </p>
                </div>
              </div>
              <button
                onClick={handleLogout}
                className="flex w-full items-center justify-center gap-2 rounded-xl border border-rose-200 bg-rose-50/80 py-2.5 text-xs font-semibold text-rose-700 hover:bg-rose-100 dark:border-rose-900/40 dark:bg-rose-950/30 dark:text-rose-300 transition-colors"
              >
                <LogOut className="h-4 w-4" />
                <span>Sign out</span>
              </button>
            </div>
          </aside>
        </div>
      )}

      {/* ============================================================== */}
      {/* MAIN CONTENT AREA & NAVBAR */}
      {/* ============================================================== */}
      <div className="flex min-w-0 flex-1 flex-col overflow-hidden">
        {/* Top Navbar */}
        <header className="sticky top-0 z-30 flex h-16 shrink-0 items-center justify-between gap-3 border-b border-slate-200/80 bg-white/85 px-4 backdrop-blur-md dark:border-slate-800/80 dark:bg-slate-900/85 sm:px-6">
          {/* Left section: mobile hamburger & breadcrumbs */}
          <div className="flex items-center gap-3 min-w-0">
            <button
              className="flex h-9 w-9 items-center justify-center rounded-lg border border-slate-200 text-slate-500 hover:bg-slate-100 dark:border-slate-700 dark:text-slate-300 dark:hover:bg-slate-800 lg:hidden"
              onClick={() => setSidebarOpen(true)}
              aria-label="Open menu"
            >
              <Menu className="h-5 w-5" />
            </button>

            {/* Breadcrumb Trail */}
            <div className="hidden sm:flex items-center gap-1.5 text-sm min-w-0">
              <span className="text-slate-400 font-medium">{breadcrumbInfo.group}</span>
              <ChevronRight className="h-3.5 w-3.5 text-slate-400 shrink-0" />
              <span className="font-semibold text-slate-800 dark:text-slate-100 truncate">{breadcrumbInfo.title}</span>
            </div>
          </div>

          {/* Center: Command Palette Trigger Button */}
          <div className="flex-1 max-w-md mx-2 hidden md:block">
            <button
              onClick={() => setQuickSearchOpen(true)}
              className="flex w-full items-center justify-between rounded-xl border border-slate-200/90 bg-slate-50/70 px-3.5 py-1.5 text-xs text-slate-400 shadow-2xs transition-all hover:border-emerald-300 hover:bg-white hover:text-slate-600 dark:border-slate-800 dark:bg-slate-800/50 dark:hover:border-slate-700 dark:hover:bg-slate-800/90 dark:hover:text-slate-200"
            >
              <span className="flex items-center gap-2 truncate">
                <Search className="h-3.5 w-3.5 text-slate-400" />
                <span>Search pages, products, orders...</span>
              </span>
              <kbd className="rounded border border-slate-200 bg-white px-1.5 py-0.5 text-[10px] font-semibold text-slate-500 shadow-2xs dark:border-slate-700 dark:bg-slate-900 dark:text-slate-400">
                Ctrl K
              </kbd>
            </button>
          </div>

          {/* Right section: warehouse, notifications, theme toggle, user profile */}
          <div className="flex items-center gap-2 shrink-0">
            {/* Quick search button for small screens */}
            <button
              onClick={() => setQuickSearchOpen(true)}
              className="flex h-9 w-9 items-center justify-center rounded-lg border border-slate-200 text-slate-500 hover:bg-slate-100 dark:border-slate-800 dark:text-slate-400 dark:hover:bg-slate-800 md:hidden"
              aria-label="Quick search"
            >
              <Search className="h-4 w-4" />
            </button>

            {/* Warehouse Filter Dropdown */}
            {can(P.WarehousesRead) && warehouses?.length > 0 && (
              <div className="relative">
                <button
                  onClick={() => { setWarehouseMenuOpen((w) => !w); setUserMenuOpen(false); setOrgMenuOpen(false) }}
                  className="flex items-center gap-2 rounded-xl border border-slate-200 bg-slate-50/80 px-2.5 py-1.5 text-xs font-medium text-slate-700 transition-colors hover:bg-slate-100 dark:border-slate-800 dark:bg-slate-800/50 dark:text-slate-200 dark:hover:bg-slate-800"
                  title="Active warehouse filter"
                >
                  <MapPin className="h-3.5 w-3.5 text-emerald-600 dark:text-emerald-400" />
                  <span className="max-w-[120px] truncate">
                    {activeWarehouseObj ? activeWarehouseObj.name : 'All Warehouses'}
                  </span>
                  <ChevronDown className="h-3.5 w-3.5 text-slate-400" />
                </button>

                {warehouseMenuOpen && (
                  <div className="absolute right-0 z-40 mt-1.5 w-56 rounded-xl border border-slate-200 bg-white p-1.5 shadow-xl dark:border-slate-700 dark:bg-slate-900">
                    <div className="px-2.5 py-1.5 text-[11px] font-bold uppercase tracking-wider text-slate-400">
                      Filter By Warehouse
                    </div>
                    <div className="space-y-0.5 max-h-52 overflow-y-auto">
                      <button
                        className={`flex w-full items-center justify-between rounded-lg px-2.5 py-1.5 text-xs ${
                          !warehouse
                            ? 'bg-emerald-50 font-semibold text-emerald-900 dark:bg-emerald-950/60 dark:text-emerald-300'
                            : 'text-slate-700 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800'
                        }`}
                        onClick={() => {
                          setWarehouse('')
                          localStorage.removeItem('stockwise:warehouse')
                          queryClient.invalidateQueries()
                          setWarehouseMenuOpen(false)
                        }}
                      >
                        <span>All warehouses</span>
                        {!warehouse && <Check className="h-3.5 w-3.5 text-emerald-600 dark:text-emerald-400" />}
                      </button>
                      {warehouses.map((w) => (
                        <button
                          key={w.id}
                          className={`flex w-full items-center justify-between rounded-lg px-2.5 py-1.5 text-xs ${
                            warehouse === w.id
                              ? 'bg-emerald-50 font-semibold text-emerald-900 dark:bg-emerald-950/60 dark:text-emerald-300'
                              : 'text-slate-700 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800'
                          }`}
                          onClick={() => {
                            setWarehouse(w.id)
                            localStorage.setItem('stockwise:warehouse', w.id)
                            queryClient.invalidateQueries()
                            setWarehouseMenuOpen(false)
                          }}
                        >
                          <span className="truncate">{w.name}</span>
                          {warehouse === w.id && <Check className="h-3.5 w-3.5 text-emerald-600 dark:text-emerald-400" />}
                        </button>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            )}

            {/* Dark / Light Theme Toggle */}
            <button
              onClick={toggleTheme}
              className="flex h-9 w-9 items-center justify-center rounded-xl border border-slate-200/80 bg-white text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-800 dark:border-slate-800 dark:bg-slate-900 dark:text-slate-400 dark:hover:bg-slate-800 dark:hover:text-slate-200"
              aria-label={isDark ? 'Switch to light mode' : 'Switch to dark mode'}
              title={isDark ? 'Switch to light mode' : 'Switch to dark mode'}
            >
              {isDark ? (
                <Sun className="h-4 w-4 text-amber-400 transition-transform duration-200 hover:rotate-45" />
              ) : (
                <Moon className="h-4 w-4 text-slate-600 transition-transform duration-200 hover:-rotate-12" />
              )}
            </button>

            {/* Notifications Bell */}
            {can(P.NotificationsRead) && (
              <Link
                to="/notifications"
                className="relative flex h-9 w-9 items-center justify-center rounded-xl border border-slate-200/80 bg-white text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-800 dark:border-slate-800 dark:bg-slate-900 dark:text-slate-400 dark:hover:bg-slate-800 dark:hover:text-slate-200"
                aria-label="Notifications"
                title="Notifications"
              >
                <Bell className="h-4 w-4" />
                {unreadCount > 0 && (
                  <span className="absolute -right-1 -top-1 flex h-4 min-w-4 items-center justify-center rounded-full bg-rose-500 px-1 text-[9px] font-bold text-white ring-2 ring-white dark:ring-slate-900 animate-pulse">
                    {unreadCount > 99 ? '99+' : unreadCount}
                  </span>
                )}
              </Link>
            )}

            <div className="h-5 w-px bg-slate-200 dark:bg-slate-800 mx-1" />

            {/* User Profile Pill & Dropdown */}
            <div className="relative">
              <button
                className="flex items-center gap-2.5 rounded-xl border border-slate-200/80 bg-white p-1.5 transition-all hover:bg-slate-50 hover:border-slate-300 dark:border-slate-800 dark:bg-slate-900 dark:hover:bg-slate-800"
                onClick={() => { setUserMenuOpen((o) => !o); setOrgMenuOpen(false); setWarehouseMenuOpen(false) }}
              >
                <div className="relative">
                  <span className="flex h-7 w-7 items-center justify-center rounded-lg bg-gradient-to-tr from-emerald-600 to-teal-500 text-xs font-bold text-white shadow-xs">
                    {userInitials}
                  </span>
                  <span className="absolute -bottom-0.5 -right-0.5 h-2 w-2 rounded-full bg-emerald-500 ring-1.5 ring-white dark:ring-slate-900" />
                </div>
                <div className="hidden text-left sm:block">
                  <p className="max-w-[110px] truncate text-xs font-semibold text-slate-800 dark:text-slate-200 leading-tight">
                    {auth.user?.fullName?.split(' ')[0]}
                  </p>
                  <p className="max-w-[110px] truncate text-[10px] text-slate-400 leading-tight">
                    {activeOrg?.role || 'User'}
                  </p>
                </div>
                <ChevronDown className={`h-3.5 w-3.5 text-slate-400 transition-transform duration-200 ${userMenuOpen ? 'rotate-180' : ''}`} />
              </button>

              {userMenuOpen && (
                <div className="absolute right-0 z-40 mt-1.5 w-60 rounded-xl border border-slate-200 bg-white p-1.5 shadow-xl dark:border-slate-700 dark:bg-slate-900">
                  <div className="border-b border-slate-100 p-2.5 dark:border-slate-800">
                    <p className="truncate text-xs font-bold text-slate-900 dark:text-white">{auth.user?.fullName}</p>
                    <p className="truncate text-[11px] text-slate-400">{auth.user?.email}</p>
                    <div className="mt-1.5 flex items-center gap-1.5">
                      <span className="inline-flex items-center gap-1 rounded bg-emerald-50 px-1.5 py-0.5 text-[10px] font-semibold text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300">
                        <Building2 className="h-3 w-3" /> {auth.orgName}
                      </span>
                    </div>
                  </div>

                  <div className="py-1 space-y-0.5">
                    <Link
                      to="/settings/profile"
                      className="flex items-center gap-2 rounded-lg px-2.5 py-2 text-xs text-slate-700 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800"
                      onClick={() => setUserMenuOpen(false)}
                    >
                      <User className="h-3.5 w-3.5 text-slate-400" />
                      <span>Profile & Password</span>
                    </Link>
                    <Link
                      to="/settings"
                      className="flex items-center gap-2 rounded-lg px-2.5 py-2 text-xs text-slate-700 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800"
                      onClick={() => setUserMenuOpen(false)}
                    >
                      <Settings className="h-3.5 w-3.5 text-slate-400" />
                      <span>Organization Settings</span>
                    </Link>
                    {can(P.AuditLogsRead) && (
                      <Link
                        to="/audit-logs"
                        className="flex items-center gap-2 rounded-lg px-2.5 py-2 text-xs text-slate-700 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800"
                        onClick={() => setUserMenuOpen(false)}
                      >
                        <ShieldCheck className="h-3.5 w-3.5 text-slate-400" />
                        <span>Security & Audit Log</span>
                      </Link>
                    )}
                  </div>

                  <div className="border-t border-slate-100 pt-1 dark:border-slate-800">
                    <button
                      className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-xs font-medium text-rose-600 transition-colors hover:bg-rose-50 dark:text-rose-400 dark:hover:bg-rose-950/40"
                      onClick={handleLogout}
                    >
                      <LogOut className="h-3.5 w-3.5" />
                      <span>Sign out</span>
                    </button>
                  </div>
                </div>
              )}
            </div>
          </div>
        </header>

        {/* Global Click-away overlay for menus */}
        {(userMenuOpen || orgMenuOpen || warehouseMenuOpen) && (
          <div
            className="fixed inset-0 z-30"
            onClick={() => {
              setUserMenuOpen(false)
              setOrgMenuOpen(false)
              setWarehouseMenuOpen(false)
            }}
          />
        )}

        {/* ============================================================== */}
        {/* QUICK SEARCH / COMMAND PALETTE MODAL */}
        {/* ============================================================== */}
        {quickSearchOpen && (
          <div className="fixed inset-0 z-50 flex items-start justify-center p-4 pt-16 sm:pt-24">
            <div className="fixed inset-0 bg-slate-900/60 backdrop-blur-xs transition-opacity" onClick={() => setQuickSearchOpen(false)} />
            <div className="relative w-full max-w-xl overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl dark:border-slate-700 dark:bg-slate-900 animate-in fade-in zoom-in-95 duration-150">
              <div className="flex items-center gap-3 border-b border-slate-100 px-4 py-3 dark:border-slate-800">
                <Search className="h-4 w-4 text-slate-400" />
                <input
                  autoFocus
                  type="text"
                  placeholder="Jump to page or feature..."
                  className="flex-1 bg-transparent text-sm text-slate-800 placeholder-slate-400 outline-none dark:text-slate-100"
                  value={quickSearchQuery}
                  onChange={(e) => setQuickSearchQuery(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' && filteredRoutes.length > 0) {
                      navigate(filteredRoutes[0].to)
                      setQuickSearchOpen(false)
                    }
                  }}
                />
                <button
                  onClick={() => setQuickSearchOpen(false)}
                  className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600 dark:hover:bg-slate-800"
                >
                  <kbd className="rounded border border-slate-200 bg-slate-50 px-1.5 py-0.5 text-[10px] text-slate-400 dark:border-slate-700 dark:bg-slate-800">ESC</kbd>
                </button>
              </div>

              <div className="max-h-80 overflow-y-auto p-2">
                {filteredRoutes.length === 0 ? (
                  <p className="px-4 py-8 text-center text-xs text-slate-400">No matching pages found.</p>
                ) : (
                  filteredRoutes.map((item, idx) => {
                    const ItemIcon = item.icon
                    return (
                      <button
                        key={item.to}
                        onClick={() => {
                          navigate(item.to)
                          setQuickSearchOpen(false)
                        }}
                        className={`flex w-full items-center justify-between rounded-xl px-3 py-2.5 text-xs text-left transition-colors ${
                          idx === 0
                            ? 'bg-emerald-50 text-emerald-950 font-medium dark:bg-emerald-950/40 dark:text-emerald-200'
                            : 'text-slate-700 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800'
                        }`}
                      >
                        <div className="flex items-center gap-3">
                          <span className={`p-1.5 rounded-lg ${idx === 0 ? 'bg-emerald-600 text-white' : 'bg-slate-100 text-slate-500 dark:bg-slate-800 dark:text-slate-400'}`}>
                            <ItemIcon className="h-4 w-4" />
                          </span>
                          <span className="font-medium">{item.label}</span>
                        </div>
                        <span className="rounded bg-slate-100 px-2 py-0.5 text-[10px] font-semibold text-slate-400 dark:bg-slate-800 dark:text-slate-400">
                          {item.group}
                        </span>
                      </button>
                    )
                  })
                )}
              </div>

              <div className="border-t border-slate-100 bg-slate-50/70 px-4 py-2 text-[11px] text-slate-400 flex items-center justify-between dark:border-slate-800 dark:bg-slate-900/50">
                <span className="flex items-center gap-2">
                  <span>Navigation: <kbd className="font-mono text-[10px]">Enter</kbd> to select</span>
                </span>
                <span>Press <kbd className="font-mono text-[10px]">Ctrl K</kbd> anywhere</span>
              </div>
            </div>
          </div>
        )}

        {/* ============================================================== */}
        {/* ROUTE VIEW CONTENT */}
        {/* ============================================================== */}
        <main className="min-w-0 flex-1 overflow-y-auto p-4 sm:p-6 lg:p-8">
          <Outlet context={{ warehouseId: warehouse, setWarehouseId: setWarehouse }} />
        </main>
      </div>
    </div>
  )
}

export function useOutletWarehouse() {
  const loc = useLocation()
  return loc.state || {}
}
