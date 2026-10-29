import { useMemo, useState } from 'react'
import { useQuery, keepPreviousData } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import {
  Download, FileText, DollarSign, Boxes, ShoppingCart, ArrowLeftRight,
  TrendingUp, PieChart as PieIcon, Warehouse as WarehouseIcon, ClipboardList
} from 'lucide-react'
import { api, qs, hasPermission } from '../lib/api-client.js'
import { useAuth } from '../app/providers.jsx'
import { formatMoney, formatDateTime, formatDate } from '../lib/formatters.js'
import { P } from '../lib/permissions.js'
import {
  Badge, ErrorState, EmptyState, Field, PageHeader, Pagination, TableSkeleton
} from '../components/ui/index.jsx'
import {
  ChartCard, ValueDonut, HBarChart, TrendAreaChart,
  useChartTheme, compactNumber, PALETTE, PO_STATUS_COLORS
} from '../components/data-display/report-charts.jsx'

// ---------------- Period selection ----------------

const PERIODS = [
  { key: '7d', label: '7 days', days: 7 },
  { key: '30d', label: '30 days', days: 30 },
  { key: '90d', label: '90 days', days: 90 },
  { key: '365d', label: '12 months', days: 365 },
  { key: 'all', label: 'All time' }
]

// Resolves the UI preset to UTC bounds. "to" is left open (now) unless the
// user picked a custom end date; backend defaults handle the rest.
function resolvePeriod(preset, customFrom, customTo) {
  if (preset === 'custom') {
    return {
      fromIso: customFrom ? new Date(`${customFrom}T00:00:00Z`).toISOString() : undefined,
      toIso: customTo ? new Date(`${customTo}T23:59:59Z`).toISOString() : undefined
    }
  }
  if (preset === 'all') return { fromIso: undefined, toIso: undefined }
  const days = PERIODS.find((p) => p.key === preset).days
  const from = new Date()
  from.setUTCHours(0, 0, 0, 0)
  from.setUTCDate(from.getUTCDate() - (days - 1))
  return { fromIso: from.toISOString(), toIso: undefined }
}

function describePeriod(preset, customFrom, customTo) {
  if (preset === 'all') return 'All time'
  if (preset === 'custom') {
    const from = customFrom ? formatDate(customFrom) : 'the beginning'
    const to = customTo ? formatDate(customTo) : 'today'
    return `${from} – ${to}`
  }
  const p = PERIODS.find((x) => x.key === preset)
  return `Last ${p.label.toLowerCase()}`
}

// ---------------- Detail report definitions ----------------

const REPORTS = [
  { key: 'stock-on-hand', label: 'Stock on hand', desc: 'Quantity and value per product and warehouse.', warehouseFilter: true },
  { key: 'inventory-valuation', label: 'Inventory valuation', desc: 'Total value at weighted-average cost, aggregated per product.' },
  { key: 'movements', label: 'Movement history', desc: 'Every posted ledger movement in the period.' },
  { key: 'purchases', label: 'Purchase orders', desc: 'Orders created in the period with status and totals.' },
  { key: 'adjustments', label: 'Adjustments & variances', desc: 'Manual adjustments and stock-count variances.' }
]

const STOCK_BADGES = { in: ['green', 'In stock'], low: ['amber', 'Low'], out: ['red', 'Out of stock'] }
const PO_BADGES = {
  Draft: 'gray', PendingApproval: 'amber', Approved: 'blue', Ordered: 'purple',
  PartiallyReceived: 'blue', Received: 'green', Rejected: 'red', Cancelled: 'gray'
}

const humanize = (s) => String(s ?? '').replace(/([A-Z])/g, ' $1').trim()

// Column renderers for each report. Keys mirror the API DTOs (camelCase).
function reportColumns(auth) {
  const money = (v) => formatMoney(v, auth.currency)
  return {
    'stock-on-hand': [
      { key: 'sku', label: 'SKU', mono: true },
      { key: 'productName', label: 'Product' },
      { key: 'categoryName', label: 'Category' },
      { key: 'warehouseName', label: 'Warehouse' },
      { key: 'quantity', label: 'On hand', align: 'right', render: (r) => `${Number(r.quantity).toLocaleString(undefined, { maximumFractionDigits: 3 })} ${r.unitSymbol || ''}`.trim() },
      { key: 'avgUnitCost', label: 'Avg unit cost', align: 'right', render: (r) => money(r.avgUnitCost) },
      { key: 'totalValue', label: 'Total value', align: 'right', strong: true, render: (r) => money(r.totalValue) },
      { key: 'stockStatus', label: 'Status', render: (r) => {
          const [color, label] = STOCK_BADGES[r.stockStatus] || ['gray', r.stockStatus]
          return <Badge color={color}>{label}</Badge>
        } }
    ],
    'inventory-valuation': [
      { key: 'sku', label: 'SKU', mono: true },
      { key: 'productName', label: 'Product' },
      { key: 'categoryName', label: 'Category' },
      { key: 'quantity', label: 'Quantity', align: 'right', render: (r) => Number(r.quantity).toLocaleString(undefined, { maximumFractionDigits: 3 }) },
      { key: 'avgUnitCost', label: 'Avg unit cost', align: 'right', render: (r) => money(r.avgUnitCost) },
      { key: 'totalValue', label: 'Total value', align: 'right', strong: true, render: (r) => money(r.totalValue) }
    ],
    movements: [
      { key: 'createdAtUtc', label: 'Date', render: (r) => formatDateTime(r.createdAtUtc, auth.dateFormat) },
      { key: 'sku', label: 'SKU', mono: true },
      { key: 'productName', label: 'Product' },
      { key: 'warehouseName', label: 'Warehouse' },
      { key: 'movementType', label: 'Type', render: (r) => humanize(r.movementType) },
      { key: 'quantityDelta', label: 'Qty', align: 'right', render: (r) => <QtyDelta value={r.quantityDelta} /> },
      { key: 'unitCost', label: 'Unit cost', align: 'right', render: (r) => (r.unitCost == null ? '—' : money(r.unitCost)) },
      { key: 'referenceNumber', label: 'Reference', render: (r) => r.referenceNumber || '—' },
      { key: 'reason', label: 'Reason', render: (r) => r.reason || '—' },
      { key: 'userName', label: 'User' }
    ],
    purchases: [
      { key: 'number', label: 'Number', mono: true },
      { key: 'supplierName', label: 'Supplier' },
      { key: 'status', label: 'Status', render: (r) => <Badge color={PO_BADGES[r.status] || 'gray'}>{humanize(r.status)}</Badge> },
      { key: 'total', label: 'Total', align: 'right', strong: true, render: (r) => formatMoney(r.total, r.currencyCode || auth.currency) },
      { key: 'createdAt', label: 'Created', render: (r) => formatDateTime(r.createdAt, auth.dateFormat) },
      { key: 'expectedDeliveryDate', label: 'Expected delivery', render: (r) => (r.expectedDeliveryDate ? formatDate(r.expectedDeliveryDate, auth.dateFormat) : '—') },
      { key: 'createdBy', label: 'Created by' }
    ],
    adjustments: [
      { key: 'createdAtUtc', label: 'Date', render: (r) => formatDateTime(r.createdAtUtc, auth.dateFormat) },
      { key: 'sku', label: 'SKU', mono: true },
      { key: 'productName', label: 'Product' },
      { key: 'warehouseName', label: 'Warehouse' },
      { key: 'quantityDelta', label: 'Qty change', align: 'right', render: (r) => <QtyDelta value={r.quantityDelta} /> },
      { key: 'reason', label: 'Reason', render: (r) => r.reason || '—' },
      { key: 'userName', label: 'User' }
    ]
  }
}

function QtyDelta({ value }) {
  const n = Number(value)
  if (n === 0) return <span className="tabular-nums text-slate-400">0</span>
  return (
    <span className={`tabular-nums font-semibold ${n > 0 ? 'text-emerald-600 dark:text-emerald-400' : 'text-red-600 dark:text-red-400'}`}>
      {n > 0 ? '+' : ''}{n.toLocaleString(undefined, { maximumFractionDigits: 3 })}
    </span>
  )
}

// ---------------- Page ----------------

export default function ReportsPage() {
  const auth = useAuth()

  if (!hasPermission(P.ReportsRead)) {
    return (
      <EmptyState
        icon={FileText}
        title="No access to reports"
        description="You do not have the Reports.Read permission. Ask an administrator to grant it."
      />
    )
  }

  return <ReportsContent auth={auth} />
}

function ReportsContent({ auth }) {
  const theme = useChartTheme()

  // Period drives the charts and the dated reports.
  const [preset, setPreset] = useState('90d')
  const [customFrom, setCustomFrom] = useState('')
  const [customTo, setCustomTo] = useState('')
  const { fromIso, toIso } = resolvePeriod(preset, customFrom, customTo)
  const periodLabel = describePeriod(preset, customFrom, customTo)

  // Detail table state.
  const [report, setReport] = useState('stock-on-hand')
  const [warehouseId, setWarehouseId] = useState('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)

  const pickReport = (key) => { setReport(key); setPage(1); setWarehouseId('') }

  const charts = useQuery({
    queryKey: ['reports', 'charts', fromIso, toIso],
    queryFn: () => api.get(`/api/v1/reports/charts${qs({ fromUtc: fromIso, toUtc: toIso })}`)
  })

  const trendDays = preset === 'all' || preset === 'custom' ? 90 : PERIODS.find((p) => p.key === preset).days
  const trends = useQuery({
    queryKey: ['reports', 'trends', trendDays],
    queryFn: () => api.get(`/api/v1/dashboard/trends?days=${trendDays}`)
  })

  const top = useQuery({
    queryKey: ['reports', 'top-products', fromIso, toIso],
    queryFn: () => api.get(`/api/v1/dashboard/top-products${qs({ from: fromIso, to: toIso, take: 8 })}`)
  })

  const warehouses = useQuery({
    queryKey: ['warehouses', 'list'],
    queryFn: () => api.get('/api/v1/warehouses')
  })

  const tableQuery = useQuery({
    queryKey: ['report', report, fromIso, toIso, warehouseId, page, pageSize],
    queryFn: () => api.get(`/api/v1/reports/${report}${qs({
      fromUtc: fromIso, toUtc: toIso, warehouseId: warehouseId || undefined, page, pageSize
    })}`),
    placeholderData: keepPreviousData
  })

  // ---- Aggregates for KPI cards ----
  const c = charts.data
  const kpis = useMemo(() => {
    if (!c) return null
    const inventoryValue = c.valueByCategory.reduce((s, x) => s + Number(x.value), 0)
    const totalUnits = c.valueByCategory.reduce((s, x) => s + Number(x.quantity), 0)
    const spend = c.purchaseStatus.reduce((s, x) => s + Number(x.total), 0)
    const orders = c.purchaseStatus.reduce((s, x) => s + x.count, 0)
    const turnover = (trends.data || []).reduce((s, d) => s + Number(d.inboundValue) + Number(d.outboundValue), 0)
    return { inventoryValue, totalUnits, spend, orders, turnover }
  }, [c, trends.data])

  const categoryData = useMemo(() => {
    if (!c) return []
    const sorted = [...c.valueByCategory].sort((a, b) => b.value - a.value)
    const topN = sorted.slice(0, 6)
    const rest = sorted.slice(6)
    const rows = topN.map((x, i) => ({ name: x.category, value: Number(x.value), color: PALETTE[i % PALETTE.length] }))
    if (rest.length > 0) {
      rows.push({ name: `Other (${rest.length})`, value: rest.reduce((s, x) => s + Number(x.value), 0), color: PALETTE[7] })
    }
    return rows
  }, [c])

  const warehouseData = useMemo(() =>
    (c?.valueByWarehouse || []).slice(0, 8).map((x) => ({ name: x.warehouse, value: Number(x.value) })),
  [c])

  const statusData = useMemo(() =>
    (c?.purchaseStatus || []).map((x) => ({ name: humanize(x.status), count: x.count, total: Number(x.total), color: PO_STATUS_COLORS[x.status] || '#64748b' })),
  [c])

  const healthData = useMemo(() => {
    const h = c?.stockHealth
    if (!h) return []
    return [
      { name: 'In stock', value: h.inStock, color: '#10b981' },
      { name: 'Low stock', value: h.lowStock, color: '#f59e0b' },
      { name: 'Out of stock', value: h.outOfStock, color: '#ef4444' }
    ]
  }, [c])

  const healthTotal = healthData.reduce((s, d) => s + d.value, 0)

  const money = (v) => formatMoney(v, auth.currency)
  const moneyTooltip = (value) => money(value)

  const current = REPORTS.find((r) => r.key === report)
  const rows = tableQuery.data?.items || []
  const totalCount = Number(tableQuery.data?.totalCount || 0)
  const columns = reportColumns(auth)

  const exportCsv = () => {
    const search = qs({ fromUtc: fromIso, toUtc: toIso, warehouseId: warehouseId || undefined, page, pageSize, format: 'csv' })
    window.open(`/api/v1/reports/${report}${search}`, '_blank')
  }

  const showWarehouseFilter = current.warehouseFilter

  return (
    <div>
      <PageHeader
        title="Reports"
        description={`Inventory analytics for ${auth.orgName || 'your organization'} — all aggregates computed on the server. Scope: ${periodLabel.toLowerCase()}.`}
        actions={<button className="btn-secondary" onClick={exportCsv}><Download className="h-4 w-4" /> Export CSV</button>}
      />

      {/* Period selector */}
      <div className="card mb-6 flex flex-col gap-3 p-4 lg:flex-row lg:items-center lg:justify-between">
        <div className="flex flex-wrap items-center gap-1 rounded-lg bg-slate-100 p-1 dark:bg-slate-800" role="group" aria-label="Reporting period">
          {PERIODS.map((p) => (
            <button
              key={p.key}
              onClick={() => { setPreset(p.key); setCustomFrom(''); setCustomTo('') }}
              className={`rounded-md px-3 py-1.5 text-xs font-medium transition-colors ${
                preset === p.key
                  ? 'bg-white text-accent-700 shadow-sm dark:bg-slate-900 dark:text-accent-300'
                  : 'text-slate-500 hover:text-slate-700 dark:text-slate-400 dark:hover:text-slate-200'
              }`}
            >
              {p.label}
            </button>
          ))}
        </div>
        <div className="flex flex-wrap items-end gap-3">
          <Field label="From (UTC)">
            <input
              type="date" className="input w-40" aria-label="Period start"
              value={preset === 'custom' ? customFrom : (fromIso ? fromIso.slice(0, 10) : '')}
              onChange={(e) => {
                const v = e.target.value
                setCustomFrom(v)
                if (customTo && v > customTo) setCustomTo(v)
                setPreset('custom')
              }}
            />
          </Field>
          <Field label="To (UTC)">
            <input
              type="date" className="input w-40" aria-label="Period end"
              value={preset === 'custom' ? customTo : (toIso ? toIso.slice(0, 10) : '')}
              onChange={(e) => { setCustomTo(e.target.value); setPreset('custom') }}
            />
          </Field>
          {preset === 'custom' && (
            <button className="btn-secondary px-3 py-2 text-xs" onClick={() => { setPreset('90d'); setCustomFrom(''); setCustomTo('') }}>
              Reset
            </button>
          )}
        </div>
      </div>

      {/* KPI cards */}
      {charts.isLoading ? (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
          {Array.from({ length: 4 }).map((_, i) => <SkeletonCard key={i} />)}
        </div>
      ) : charts.isError ? (
        <div className="card"><ErrorState error={charts.error} onRetry={charts.refetch} /></div>
      ) : (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <KpiCard icon={DollarSign} tone="success" label="Inventory value" value={money(kpis.inventoryValue)} sub={`${compactNumber(kpis.totalUnits)} units on hand`} />
          <KpiCard
            icon={Boxes}
            tone={c.stockHealth.lowStock + c.stockHealth.outOfStock > 0 ? 'warning' : 'success'}
            label="Stock health"
            value={`${c.stockHealth.inStock} healthy`}
            sub={`${c.stockHealth.lowStock} low · ${c.stockHealth.outOfStock} out of stock`}
            href="/stock-on-hand"
          />
          <KpiCard icon={ShoppingCart} tone="info" label="Purchase spend" value={money(kpis.spend)} sub={`${kpis.orders} orders · ${periodLabel.toLowerCase()}`} href="/purchase-orders" />
          <KpiCard icon={ArrowLeftRight} label={`Turnover (last ${trendDays} days)`} value={money(kpis.turnover)} sub="Inbound + outbound movement value" href="/movements" />
        </div>
      )}

      {/* Trend + stock health */}
      <div className="mt-6 grid grid-cols-1 gap-6 xl:grid-cols-3">
        <ChartCard
          title="Movement value trend" subtitle={`Daily inbound vs outbound, last ${trendDays} days`}
          isLoading={trends.isLoading} isError={trends.isError} error={trends.error} onRetry={trends.refetch}
          isEmpty={(trends.data || []).length === 0} emptyIcon={TrendingUp}
          actions={
            <div className="flex gap-3 text-xs text-slate-500 dark:text-slate-400">
              <span className="flex items-center gap-1.5"><span className="h-2 w-2 rounded-full bg-emerald-500" /> Inbound</span>
              <span className="flex items-center gap-1.5"><span className="h-2 w-2 rounded-full bg-red-500" /> Outbound</span>
            </div>
          }
        >
          <TrendAreaChart data={trends.data || []} formatMoney={money} theme={theme} />
        </ChartCard>

        <ChartCard
          title="Stock health" subtitle="Active products vs their reorder point" height="h-64"
          isLoading={charts.isLoading} isError={charts.isError} error={charts.error} onRetry={charts.refetch}
          isEmpty={healthTotal === 0} emptyIcon={Boxes} emptyTitle="No active stock yet"
        >
          <ValueDonut
            data={healthData} theme={theme}
            centerValue={healthTotal.toLocaleString()} centerLabel="products"
            formatValue={(v) => v.toLocaleString()}
          />
        </ChartCard>
      </div>

      {/* Category / warehouse / purchase status / top products */}
      <div className="mt-6 grid grid-cols-1 gap-6 xl:grid-cols-2">
        <ChartCard
          title="Inventory value by category" subtitle={`Current weighted-average value · ${periodLabel.toLowerCase()}`}
          isLoading={charts.isLoading} isError={charts.isError} error={charts.error} onRetry={charts.refetch}
          isEmpty={categoryData.length === 0} emptyIcon={PieIcon} emptyTitle="No stock on hand yet"
          height="h-72"
        >
          <ValueDonut
            data={categoryData} theme={theme}
            centerValue={money(kpis?.inventoryValue || 0)} centerLabel="total value"
            formatValue={money}
          />
        </ChartCard>

        <ChartCard
          title="Inventory value by warehouse" subtitle="Where your capital is sitting"
          isLoading={charts.isLoading} isError={charts.isError} error={charts.error} onRetry={charts.refetch}
          isEmpty={warehouseData.length === 0} emptyIcon={WarehouseIcon} emptyTitle="No warehouse balances"
          height="h-72"
        >
          <HBarChart data={warehouseData} nameKey="name" valueKey="value" formatTooltip={moneyTooltip} theme={theme} color="#0ea5e9" />
        </ChartCard>

        <ChartCard
          title="Purchase orders by status" subtitle={`Count and spend for orders created ${periodLabel.toLowerCase()}`}
          isLoading={charts.isLoading} isError={charts.isError} error={charts.error} onRetry={charts.refetch}
          isEmpty={statusData.length === 0} emptyIcon={ClipboardList} emptyTitle="No purchase orders in this period"
          height="h-72"
        >
          <HBarChart
            data={statusData} nameKey="name" valueKey="count" theme={theme} colorByEntry
            formatTooltip={(v, name, entry) => {
              const row = statusData.find((d) => d.name === name)
              return [`${v} orders · ${money(row?.total || 0)} spend`, name]
            }}
          />
        </ChartCard>

        <ChartCard
          title="Top-moving products" subtitle={`By units moved, ${periodLabel.toLowerCase()}`}
          isLoading={top.isLoading} isError={top.isError} error={top.error} onRetry={top.refetch}
          isEmpty={(top.data || []).length === 0} emptyIcon={TrendingUp} emptyTitle="No movements recorded in this period"
          height="h-72"
        >
          <HBarChart
            data={(top.data || []).map((t) => ({ name: t.productName, value: Number(t.movementQuantity), value2: Number(t.movementValue) }))}
            nameKey="name" valueKey="value" theme={theme} color="#10b981"
            formatTooltip={(v, name, entry) => [`${compactNumber(v)} units · ${money(entry?.payload?.value2 || 0)} value`, name]}
          />
        </ChartCard>
      </div>

      {/* Detail reports */}
      <div className="card mt-6">
        <div className="flex flex-col gap-3 px-4 pt-1 sm:flex-row sm:items-center sm:justify-between">
          <div className="min-w-0 flex-1">
            <TabsInline tabs={REPORTS} active={report} onChange={pickReport} />
          </div>
          <div className="flex shrink-0 items-center gap-2 pb-2">
            {showWarehouseFilter && (
              <select
                className="input w-44 py-1.5 text-xs" aria-label="Filter by warehouse"
                value={warehouseId}
                onChange={(e) => { setWarehouseId(e.target.value); setPage(1) }}
              >
                <option value="">All warehouses</option>
                {(warehouses.data || []).map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
              </select>
            )}
            <button className="btn-secondary px-3 py-1.5 text-xs" onClick={exportCsv}>
              <Download className="h-3.5 w-3.5" /> CSV
            </button>
          </div>
        </div>

        <p className="border-b border-slate-100 px-4 text-xs text-slate-400 dark:border-slate-800 dark:text-slate-500">
          {current.desc}{tableQuery.data?.definitions ? ` — ${tableQuery.data.definitions}` : ''}
        </p>

        {tableQuery.isLoading ? (
          <TableSkeleton rows={8} cols={5} />
        ) : tableQuery.isError ? (
          <ErrorState error={tableQuery.error} onRetry={tableQuery.refetch} />
        ) : rows.length === 0 ? (
          <EmptyState icon={FileText} title="No data for this report" description="Adjust the period or record some activity first." />
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full">
                <thead>
                  <tr>
                    {columns[report].map((col) => (
                      <th key={col.key} className={`th ${col.align === 'right' ? 'text-right' : ''}`}>{col.label}</th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row, i) => (
                    <tr key={i} className="table-row">
                      {columns[report].map((col) => (
                        <td key={col.key} className={`td ${col.align === 'right' ? 'text-right' : ''} ${col.mono ? 'font-mono text-xs' : ''} ${col.strong ? 'font-semibold' : ''}`}>
                          {col.render ? col.render(row) : row[col.key] ?? '—'}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pagination
              page={page} pageSize={pageSize} totalCount={totalCount}
              onPage={setPage}
              onPageSize={(n) => { setPageSize(n); setPage(1) }}
            />
          </>
        )}
      </div>
    </div>
  )
}

// Underline-style inline tabs matching the app's Tabs component look.
function TabsInline({ tabs, active, onChange }) {
  return (
    <nav className="-mb-px flex gap-4 overflow-x-auto" aria-label="Report types">
      {tabs.map((t) => (
        <button
          key={t.key}
          onClick={() => onChange(t.key)}
          className={`whitespace-nowrap border-b-2 px-1 py-2.5 text-sm font-medium transition-colors ${
            active === t.key
              ? 'border-accent-600 text-accent-600 dark:border-accent-500 dark:text-accent-400'
              : 'border-transparent text-slate-500 hover:border-slate-300 hover:text-slate-700 dark:text-slate-400'
          }`}
          aria-current={active === t.key ? 'page' : undefined}
        >
          {t.label}
        </button>
      ))}
    </nav>
  )
}

function KpiCard({ icon: Icon, label, value, sub, tone = 'default', href }) {
  const tones = {
    default: 'text-slate-400 dark:text-slate-500',
    success: 'text-emerald-500',
    warning: 'text-amber-500',
    info: 'text-sky-500'
  }
  const Wrapper = href ? Link : 'div'
  return (
    <Wrapper to={href} className="card block p-4 transition-shadow hover:shadow-md">
      <div className="flex items-center justify-between">
        <p className="text-sm font-medium text-slate-500 dark:text-slate-400">{label}</p>
        {Icon && <Icon className={`h-5 w-5 ${tones[tone]}`} strokeWidth={1.75} />}
      </div>
      <p className="mt-2 text-2xl font-semibold tabular-nums text-slate-800 dark:text-slate-100">{value}</p>
      {sub && <p className="mt-1 text-xs text-slate-400 dark:text-slate-500">{sub}</p>}
    </Wrapper>
  )
}

function SkeletonCard() {
  return (
    <div className="card h-[104px] animate-pulse rounded-lg bg-slate-100 dark:bg-slate-800" />
  )
}
