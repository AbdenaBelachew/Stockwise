import { useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  LayoutDashboard, Package, FolderTree, Warehouse, Layers, ArrowLeftRight, ClipboardCheck,
  Truck, ShoppingCart, FileText, Bell, Users, Settings, ChevronDown, Menu, X, Search,
  PackageCheck, Boxes, ClipboardList, LogOut, Building2, ShieldCheck, Scale, ChevronLeft, Check
} from 'lucide-react'
import { useAuth } from '../../app/providers.jsx'
import { P } from '../../lib/permissions.js'
import { api, getOrgId } from '../../lib/api-client.js'

function NavItem({ to, icon: Icon, label, end, collapsed }) {
  return (
    <NavLink
      to={to}
      end={end}
      title={collapsed ? label : undefined}
      className={({ isActive }) =>
        `group relative flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors ${
          collapsed ? 'justify-center px-0' : ''
        } ${
          isActive
            ? 'bg-accent-50 text-accent-700 dark:bg-accent-900/40 dark:text-accent-300'
            : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900 dark:text-slate-300 dark:hover:bg-slate-800 dark:hover:text-white'
        }`
      }
    >
      {({ isActive }) => (
        <>
          {/* Active indicator bar */}
          <span
            aria-hidden="true"
            className={`absolute left-0 top-1/2 h-5 w-1 -translate-y-1/2 rounded-r-full bg-accent-600 transition-opacity dark:bg-accent-400 ${
              isActive ? 'opacity-100' : 'opacity-0'
            }`}
          />
          <Icon
            className={`h-[18px] w-[18px] shrink-0 transition-colors ${isActive ? 'text-accent-600 dark:text-accent-400' : 'text-slate-400 group-hover:text-slate-600 dark:group-hover:text-slate-300'}`}
            strokeWidth={1.75}
          />
          {!collapsed && <span className="truncate">{label}</span>}
        </>
      )}
    </NavLink>
  )
}

function NavGroup({ title, children, collapsed }) {
  if (collapsed) {
    return (
      <div className="mb-1 px-3 py-2" title={title}>
        <div className="border-t border-slate-200 dark:border-slate-700" aria-label={title} />
      </div>
    )
  }
  return (
    <div className="mb-1">
      <p className="mb-1 mt-3 px-3 text-[11px] font-semibold uppercase tracking-wider text-slate-400 dark:text-slate-500">
        {title}
      </p>
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
  const [collapsed, setCollapsed] = useState(false)
  const [userMenuOpen, setUserMenuOpen] = useState(false)
  const [orgMenuOpen, setOrgMenuOpen] = useState(false)
  const [warehouse, setWarehouse] = useState(() => localStorage.getItem('stockwise:warehouse') || '')

  // Close mobile sidebar on navigation
  useEffect(() => setSidebarOpen(false), [location.pathname])

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

  const nav = (
    <nav className="flex-1 space-y-0.5 overflow-y-auto px-2 py-3" aria-label="Main navigation">
      <NavItem to="/" icon={LayoutDashboard} label="Overview" end collapsed={collapsed} />
      <NavGroup title="Inventory" collapsed={collapsed}>
        {can(P.ProductsRead) && <NavItem to="/products" icon={Package} label="Products" collapsed={collapsed} />}
        {can(P.CatalogManage) && <NavItem to="/categories" icon={FolderTree} label="Categories & Units" collapsed={collapsed} />}
        {can(P.WarehousesRead) && <NavItem to="/warehouses" icon={Warehouse} label="Warehouses" collapsed={collapsed} />}
        {can(P.InventoryRead) && <NavItem to="/stock-on-hand" icon={Boxes} label="Stock on hand" collapsed={collapsed} />}
        {can(P.InventoryRead) && <NavItem to="/movements" icon={ClipboardList} label="Stock movements" collapsed={collapsed} />}
        {can(P.InventoryRead) && <NavItem to="/batches" icon={Layers} label="Batches & expiry" collapsed={collapsed} />}
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
          <NavItem to="/audit-logs" icon={ShieldCheck} label="Audit log" collapsed={collapsed} />
        </NavGroup>
      )}
      <NavGroup title="Administration" collapsed={collapsed}>
        {can(P.UsersManage) && <NavItem to="/team" icon={Users} label="Team & roles" collapsed={collapsed} />}
        <NavItem to="/settings" icon={Settings} label="Settings" collapsed={collapsed} />
      </NavGroup>
    </nav>
  )

  return (
    <div className="flex h-screen overflow-hidden">
      {/* Desktop sidebar */}
      <aside
        className={`hidden shrink-0 flex-col border-r border-slate-200 bg-white transition-[width] duration-150 dark:border-slate-800 dark:bg-slate-900 lg:flex ${
          collapsed ? 'w-16' : 'w-60'
        }`}
      >
        <div className={`flex h-14 items-center gap-2.5 border-b border-slate-200 px-4 dark:border-slate-800 ${collapsed ? 'justify-center px-0' : ''}`}>
          <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-accent-600 text-white shadow-sm">
            <Boxes className="h-5 w-5" strokeWidth={2} />
          </span>
          {!collapsed && (
            <span className="text-base font-bold tracking-tight text-slate-800 dark:text-slate-100">
              Stock<span className="text-accent-600 dark:text-accent-400">Wise</span>
            </span>
          )}
        </div>
        {nav}
        <button
          onClick={() => setCollapsed((c) => !c)}
          className="flex items-center justify-center gap-2 border-t border-slate-200 px-3 py-2.5 text-xs text-slate-400 hover:text-slate-600 dark:border-slate-800"
          aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
        >
          <ChevronLeft className={`h-4 w-4 transition-transform ${collapsed ? 'rotate-180' : ''}`} />
          {!collapsed && 'Collapse'}
        </button>
      </aside>

      {/* Mobile sidebar drawer */}
      {sidebarOpen && (
        <div className="fixed inset-0 z-40 lg:hidden">
          <div className="absolute inset-0 bg-slate-900/50" onClick={() => setSidebarOpen(false)} />
          <aside className="absolute inset-y-0 left-0 flex w-64 flex-col bg-white dark:bg-slate-900">
            <div className="flex h-14 items-center justify-between border-b border-slate-200 px-4 dark:border-slate-800">
              <div className="flex items-center gap-2">
                <Boxes className="h-6 w-6 text-accent-600" />
                <span className="font-bold">StockWise</span>
              </div>
              <button onClick={() => setSidebarOpen(false)} aria-label="Close menu"><X className="h-5 w-5" /></button>
            </div>
            {nav}
          </aside>
        </div>
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        {/* Top bar */}
        <header className="flex h-14 shrink-0 items-center gap-3 border-b border-slate-200 bg-white px-4 dark:border-slate-800 dark:bg-slate-900">
          <button className="lg:hidden" onClick={() => setSidebarOpen(true)} aria-label="Open menu">
            <Menu className="h-5 w-5" />
          </button>

          {/* Organization switcher */}
          {auth.organizations.length > 0 && (
            <div className="relative">
              <button
                className="flex items-center gap-2 rounded-md border border-slate-200 px-3 py-1.5 text-sm dark:border-slate-700"
                onClick={() => { setOrgMenuOpen((o) => !o); setUserMenuOpen(false) }}
              >
                <Building2 className="h-4 w-4 text-slate-400" />
                <span className="max-w-[160px] truncate font-medium">{auth.orgName}</span>
                <ChevronDown className="h-4 w-4 text-slate-400" />
              </button>
              {orgMenuOpen && (
                <div className="absolute left-0 z-30 mt-1 w-64 rounded-lg border border-slate-200 bg-white py-1 shadow-lg dark:border-slate-700 dark:bg-slate-900">
                  <p className="px-3 py-1.5 text-xs font-semibold uppercase text-slate-400">Organizations</p>
                  {auth.organizations.map((o) => (
                    <button
                      key={o.id}
                      className="flex w-full items-center justify-between px-3 py-2 text-sm text-left hover:bg-slate-50 dark:hover:bg-slate-800"
                      onClick={() => switchOrg.mutate(o.id)}
                    >
                      <span className="truncate">{o.name}</span>
                      <span className="ml-2 flex items-center gap-2 text-xs text-slate-400">
                        {o.role}
                        {o.id === auth.orgId && <Check className="h-4 w-4 text-accent-600" />}
                      </span>
                    </button>
                  ))}
                </div>
              )}
            </div>
          )}

          {/* Warehouse selector */}
          {can(P.WarehousesRead) && warehouses?.length > 0 && (
            <select
              className="w-44 rounded-md border border-slate-200 bg-white px-2.5 py-1.5 text-sm dark:border-slate-700 dark:bg-slate-900"
              value={warehouse}
              onChange={(e) => {
                setWarehouse(e.target.value)
                localStorage.setItem('stockwise:warehouse', e.target.value)
                queryClient.invalidateQueries()
              }}
              aria-label="Active warehouse filter"
            >
              <option value="">All warehouses</option>
              {warehouses.map((w) => (
                <option key={w.id} value={w.id}>{w.name}</option>
              ))}
            </select>
          )}

          <div className="ml-auto flex items-center gap-1">
            {can(P.NotificationsRead) && (
              <Link to="/notifications" className="relative rounded-md p-2 text-slate-500 hover:bg-slate-100 dark:hover:bg-slate-800" aria-label="Notifications">
                <Bell className="h-5 w-5" />
                {unreadCount > 0 && (
                  <span className="absolute -right-0.5 -top-0.5 flex h-5 min-w-5 items-center justify-center rounded-full bg-red-500 px-1 text-[10px] font-bold text-white">
                    {unreadCount > 99 ? '99+' : unreadCount}
                  </span>
                )}
              </Link>
            )}

            {/* User menu */}
            <div className="relative">
              <button
                className="flex items-center gap-2 rounded-md p-1.5 hover:bg-slate-100 dark:hover:bg-slate-800"
                onClick={() => { setUserMenuOpen((o) => !o); setOrgMenuOpen(false) }}
              >
                <span className="flex h-8 w-8 items-center justify-center rounded-full bg-accent-600 text-sm font-semibold text-white">
                  {auth.user?.fullName?.split(' ').map((s) => s[0]).slice(0, 2).join('') || '?'}
                </span>
                <span className="hidden max-w-[120px] truncate text-sm font-medium sm:block">{auth.user?.fullName}</span>
                <ChevronDown className="h-4 w-4 text-slate-400" />
              </button>
              {userMenuOpen && (
                <div className="absolute right-0 z-30 mt-1 w-56 rounded-lg border border-slate-200 bg-white py-1 shadow-lg dark:border-slate-700 dark:bg-slate-900">
                  <div className="border-b border-slate-100 px-3 py-2 dark:border-slate-800">
                    <p className="truncate text-sm font-medium">{auth.user?.fullName}</p>
                    <p className="truncate text-xs text-slate-400">{auth.user?.email}</p>
                  </div>
                  <Link to="/settings/profile" className="block px-3 py-2 text-sm hover:bg-slate-50 dark:hover:bg-slate-800" onClick={() => setUserMenuOpen(false)}>
                    Profile & password
                  </Link>
                  <Link to="/settings" className="block px-3 py-2 text-sm hover:bg-slate-50 dark:hover:bg-slate-800" onClick={() => setUserMenuOpen(false)}>
                    Organization settings
                  </Link>
                  <button
                    className="flex w-full items-center gap-2 px-3 py-2 text-left text-sm text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20"
                    onClick={() => { setUserMenuOpen(false); auth.logout(); navigate('/login') }}
                  >
                    <LogOut className="h-4 w-4" /> Sign out
                  </button>
                </div>
              )}
            </div>
          </div>
        </header>

        {/* Click-away for menus */}
        {(userMenuOpen || orgMenuOpen) && (
          <div className="fixed inset-0 z-20" onClick={() => { setUserMenuOpen(false); setOrgMenuOpen(false) }} />
        )}

        <main className="min-w-0 flex-1 overflow-y-auto p-4 sm:p-6">
          <Outlet context={{ warehouseId: warehouse, setWarehouseId: setWarehouse }} />
        </main>
      </div>
    </div>
  )
}

export function useOutletWarehouse() {
  const loc = useLocation()
  // warehouse selection is passed via Outlet context in AppShell
  return loc.state || {}
}
