import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import {
  Package, Boxes, DollarSign, AlertTriangle, XCircle, Clock, ShoppingCart,
  ClipboardCheck, ArrowDownRight, ArrowUpRight, FileText
} from 'lucide-react'
import {
  Area, AreaChart, Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis
} from 'recharts'
import { api } from '../lib/api-client.js'
import { formatMoney, formatDateTime } from '../lib/formatters.js'
import { useAuth } from '../app/providers.jsx'
import { Badge, ErrorState, PageHeader, Skeleton, StatCard, TableSkeleton } from '../components/ui/index.jsx'

export default function DashboardPage() {
  const auth = useAuth()
  const summary = useQuery({
    queryKey: ['dashboard', 'summary'],
    queryFn: () => api.get('/api/v1/dashboard/summary'),
    refetchInterval: 120000
  })
  const trends = useQuery({
    queryKey: ['dashboard', 'trends'],
    queryFn: () => api.get('/api/v1/dashboard/trends?days=30')
  })
  const top = useQuery({
    queryKey: ['dashboard', 'top-products'],
    queryFn: () => api.get('/api/v1/dashboard/top-products?take=8')
  })
  const alerts = useQuery({
    queryKey: ['dashboard', 'alerts'],
    queryFn: () => api.get('/api/v1/dashboard/alerts')
  })
  const movements = useQuery({
    queryKey: ['inventory', 'movements', 'recent'],
    queryFn: () => api.get('/api/v1/inventory/movements?pageSize=8')
  })

  const s = summary.data

  return (
    <div>
      <PageHeader
        title="Overview"
        description={`Inventory and procurement health for ${auth.orgName}.`}
        actions={
          <>
            <Link to="/reports" className="btn-secondary"><FileText className="h-4 w-4" /> Reports</Link>
            <Link to="/products" className="btn-primary"><Package className="h-4 w-4" /> Products</Link>
          </>
        }
      />

      {summary.isLoading ? (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
          {Array.from({ length: 8 }).map((_, i) => <Skeleton key={i} className="h-28 rounded-lg" />)}
        </div>
      ) : summary.isError ? (
        <div className="card"><ErrorState error={summary.error} onRetry={summary.refetch} /></div>
      ) : (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard title="Active products" value={s.activeProducts} icon={Package} />
          <StatCard title="Inventory value" value={formatMoney(s.inventoryValue, auth.currency)} sub={`${Number(s.totalUnits).toLocaleString()} units on hand`} icon={DollarSign} tone="success" />
          <StatCard title="Low stock" value={s.lowStockCount} sub="At or below reorder point" icon={AlertTriangle} tone="warning" href="/stock-on-hand?stockStatus=low" />
          <StatCard title="Out of stock" value={s.outOfStockCount} icon={XCircle} tone="danger" href="/stock-on-hand?stockStatus=out" />
          <StatCard title="Expiring soon" value={s.expiringSoonCount} sub={`Within alert window`} icon={Clock} tone="warning" href="/batches" />
          <StatCard title="Expired batches" value={s.expiredCount} icon={XCircle} tone="danger" href="/batches" />
          <StatCard title="Open purchase orders" value={s.openPurchaseOrders} icon={ShoppingCart} href="/purchase-orders?status=Ordered" />
          <StatCard title="Awaiting approval" value={s.pendingApprovalCount} icon={ClipboardCheck} tone="info" href="/purchase-orders?status=PendingApproval" />
        </div>
      )}

      <div className="mt-6 grid grid-cols-1 gap-6 xl:grid-cols-3">
        {/* Movement value trend */}
        <div className="card xl:col-span-2">
          <div className="flex items-center justify-between border-b border-slate-100 px-4 py-3 dark:border-slate-800">
            <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-200">Inventory movement value — last 30 days</h2>
            <div className="flex gap-4 text-xs text-slate-500">
              <span className="flex items-center gap-1.5"><ArrowUpRight className="h-3.5 w-3.5 text-emerald-500" /> Inbound</span>
              <span className="flex items-center gap-1.5"><ArrowDownRight className="h-3.5 w-3.5 text-red-500" /> Outbound</span>
            </div>
          </div>
          <div className="h-64 p-4">
            {trends.isLoading ? <TableSkeleton rows={4} cols={3} /> : trends.isError ? (
              <ErrorState error={trends.error} onRetry={trends.refetch} />
            ) : (
              <ResponsiveContainer width="100%" height="100%">
                <AreaChart data={trends.data || []} margin={{ top: 5, right: 10, left: 0, bottom: 0 }}>
                  <defs>
                    <linearGradient id="in" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stopColor="#10b981" stopOpacity={0.25} />
                      <stop offset="100%" stopColor="#10b981" stopOpacity={0} />
                    </linearGradient>
                    <linearGradient id="out" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stopColor="#ef4444" stopOpacity={0.25} />
                      <stop offset="100%" stopColor="#ef4444" stopOpacity={0} />
                    </linearGradient>
                  </defs>
                  <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                  <XAxis dataKey="date" tickFormatter={(d) => d.slice(5)} tick={{ fontSize: 11 }} />
                  <YAxis tick={{ fontSize: 11 }} tickFormatter={(v) => `${(v / 1000).toFixed(0)}k`} />
                  <Tooltip formatter={(v) => formatMoney(v, auth.currency)} />
                  <Area type="monotone" dataKey="inboundValue" stroke="#10b981" fill="url(#in)" strokeWidth={2} />
                  <Area type="monotone" dataKey="outboundValue" stroke="#ef4444" fill="url(#out)" strokeWidth={2} />
                </AreaChart>
              </ResponsiveContainer>
            )}
          </div>
        </div>

        {/* Alerts */}
        <div className="card">
          <div className="flex items-center justify-between border-b border-slate-100 px-4 py-3 dark:border-slate-800">
            <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-200">Alerts</h2>
            <Link to="/notifications" className="text-xs font-medium text-accent-600 hover:underline">View all</Link>
          </div>
          <div className="max-h-64 overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800">
            {alerts.isLoading ? <TableSkeleton rows={4} cols={1} /> : (alerts.data || []).length === 0 ? (
              <p className="px-4 py-8 text-center text-sm text-slate-400">No active alerts. All clear.</p>
            ) : (
              (alerts.data || []).slice(0, 6).map((a) => (
                <Link key={a.id} to={a.link || '/notifications'} className="block px-4 py-3 hover:bg-slate-50 dark:hover:bg-slate-800/50">
                  <div className="flex items-start justify-between gap-2">
                    <p className="text-sm font-medium text-slate-700 dark:text-slate-200">{a.title}</p>
                    <Badge color={a.severity === 'Critical' ? 'red' : a.severity === 'Warning' ? 'amber' : 'blue'}>{a.severity}</Badge>
                  </div>
                  <p className="mt-0.5 line-clamp-2 text-xs text-slate-500 dark:text-slate-400">{a.message}</p>
                </Link>
              ))
            )}
          </div>
        </div>
      </div>

      <div className="mt-6 grid grid-cols-1 gap-6 xl:grid-cols-2">
        {/* Top moving products */}
        <div className="card">
          <div className="flex items-center justify-between border-b border-slate-100 px-4 py-3 dark:border-slate-800">
            <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-200">Top-moving products — last 30 days</h2>
          </div>
          <div className="h-64 p-4">
            {top.isLoading ? <TableSkeleton rows={4} cols={2} /> : (top.data || []).length === 0 ? (
              <p className="flex h-full items-center justify-center text-sm text-slate-400">No movements recorded in this period yet.</p>
            ) : (
              <ResponsiveContainer width="100%" height="100%">
                <BarChart data={top.data} layout="vertical" margin={{ left: 30, right: 10 }}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                  <XAxis type="number" tick={{ fontSize: 11 }} />
                  <YAxis type="category" dataKey="productName" width={140} tick={{ fontSize: 11 }} />
                  <Tooltip />
                  <Bar dataKey="movementQuantity" fill="#059669" radius={[0, 4, 4, 0]} name="Units moved" />
                </BarChart>
              </ResponsiveContainer>
            )}
          </div>
        </div>

        {/* Recent movements */}
        <div className="card">
          <div className="flex items-center justify-between border-b border-slate-100 px-4 py-3 dark:border-slate-800">
            <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-200">Recent inventory movements</h2>
            <Link to="/movements" className="text-xs font-medium text-accent-600 hover:underline">View ledger</Link>
          </div>
          <div className="max-h-64 overflow-y-auto">
            {movements.isLoading ? <TableSkeleton rows={4} cols={3} /> : (movements.data?.items || []).length === 0 ? (
              <p className="px-4 py-8 text-center text-sm text-slate-400">No movements yet. Post an opening balance to get started.</p>
            ) : (
              <table className="w-full">
                <tbody>
                  {(movements.data?.items || []).map((m) => (
                    <tr key={m.id} className="table-row">
                      <td className="td max-w-[180px] truncate">{m.productName}</td>
                      <td className="td">
                        <span className={m.quantityDelta > 0 ? 'font-semibold text-emerald-600' : 'font-semibold text-red-600'}>
                          {m.quantityDelta > 0 ? '+' : ''}{Number(m.quantityDelta)}
                        </span>
                      </td>
                      <td className="td text-slate-500">{m.movementType.replace(/([A-Z])/g, ' $1').trim()}</td>
                      <td className="td text-right text-xs text-slate-400">{formatDateTime(m.createdAtUtc, auth.dateFormat)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
