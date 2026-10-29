import { useEffect, useMemo, useState } from 'react'
import {
  Area, AreaChart, Bar, BarChart, CartesianGrid, Cell, Pie, PieChart,
  ResponsiveContainer, Tooltip, XAxis, YAxis
} from 'recharts'
import { ErrorState, EmptyState, Skeleton } from '../ui/index.jsx'

// Categorical palette for multi-series charts (accessible on light and dark).
export const PALETTE = ['#0ea5e9', '#8b5cf6', '#f59e0b', '#10b981', '#ef4444', '#14b8a6', '#e879f9', '#64748b']

export const PO_STATUS_COLORS = {
  Draft: '#94a3b8',
  PendingApproval: '#f59e0b',
  Approved: '#0ea5e9',
  Ordered: '#8b5cf6',
  PartiallyReceived: '#06b6d4',
  Received: '#10b981',
  Rejected: '#ef4444',
  Cancelled: '#64748b'
}

// Compact number for axes: 12345 -> "12.3k"
export function compactNumber(value) {
  return new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 }).format(value)
}

// Tracks the app's class-based dark mode so chart chrome (grid, ticks, tooltip)
// stays legible when the theme flips without a page reload.
function useIsDark() {
  const [dark, setDark] = useState(() => document.documentElement.classList.contains('dark'))
  useEffect(() => {
    const el = document.documentElement
    const observer = new MutationObserver(() => setDark(el.classList.contains('dark')))
    observer.observe(el, { attributes: true, attributeFilter: ['class'] })
    return () => observer.disconnect()
  }, [])
  return dark
}

export function useChartTheme() {
  const dark = useIsDark()
  return useMemo(() => ({
    dark,
    grid: dark ? '#1e293b' : '#e2e8f0',
    axisLine: dark ? '#334155' : '#cbd5e1',
    tick: { fontSize: 11, fill: dark ? '#94a3b8' : '#64748b' },
    tooltip: {
      contentStyle: {
        backgroundColor: dark ? '#0f172a' : '#ffffff',
        border: `1px solid ${dark ? '#334155' : '#e2e8f0'}`,
        borderRadius: 8,
        fontSize: 12,
        boxShadow: '0 4px 12px rgb(0 0 0 / 0.15)'
      },
      itemStyle: { color: dark ? '#e2e8f0' : '#334155', padding: '2px 0' },
      labelStyle: { color: dark ? '#94a3b8' : '#64748b', fontWeight: 600, marginBottom: 4 }
    }
  }), [dark])
}

// Standard chart card: header with title/subtitle, and body states
// (loading skeleton, error, empty) so callers only pass the chart itself.
export function ChartCard({ title, subtitle, actions, height = 'h-64', isLoading, isError, error, onRetry, isEmpty, emptyIcon, emptyTitle, emptyDescription, children }) {
  return (
    <div className="card">
      <div className="flex items-start justify-between gap-3 border-b border-slate-100 px-4 py-3 dark:border-slate-800">
        <div>
          <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-200">{title}</h2>
          {subtitle && <p className="mt-0.5 text-xs text-slate-400 dark:text-slate-500">{subtitle}</p>}
        </div>
        {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
      </div>
      <div className={`p-4 ${height}`}>
        {isLoading ? (
          <Skeleton className="h-full w-full rounded-lg" />
        ) : isError ? (
          <ErrorState error={error} onRetry={onRetry} />
        ) : isEmpty ? (
          <EmptyState icon={emptyIcon} title={emptyTitle || 'No data in this period'} description={emptyDescription} />
        ) : (
          children
        )}
      </div>
    </div>
  )
}

// Donut with a centered headline value and a value/share legend beside it.
export function ValueDonut({ data, centerValue, centerLabel, formatValue, theme }) {
  const total = data.reduce((sum, d) => sum + d.value, 0)
  return (
    <div className="flex h-full flex-col items-center gap-4 sm:flex-row">
      <div className="relative h-48 w-48 shrink-0">
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie data={data} dataKey="value" nameKey="name" innerRadius="70%" outerRadius="94%" paddingAngle={data.length > 1 ? 2 : 0} strokeWidth={0}>
              {data.map((d) => <Cell key={d.name} fill={d.color} />)}
            </Pie>
            <Tooltip {...theme.tooltip} formatter={(value, name) => [formatValue(value), name]} />
          </PieChart>
        </ResponsiveContainer>
        <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
          <span className="text-lg font-semibold tabular-nums text-slate-800 dark:text-slate-100">{centerValue}</span>
          <span className="text-xs text-slate-400 dark:text-slate-500">{centerLabel}</span>
        </div>
      </div>
      <div className="flex min-w-0 flex-1 flex-col justify-center gap-1.5 overflow-y-auto py-1">
        {data.map((d) => (
          <div key={d.name} className="flex items-center gap-2 text-xs">
            <span className="h-2.5 w-2.5 shrink-0 rounded-sm" style={{ backgroundColor: d.color }} />
            <span className="min-w-0 flex-1 truncate text-slate-600 dark:text-slate-300" title={d.name}>{d.name}</span>
            <span className="tabular-nums font-medium text-slate-700 dark:text-slate-200">{formatValue(d.value)}</span>
            <span className="w-9 text-right tabular-nums text-slate-400 dark:text-slate-500">
              {total > 0 ? `${Math.round((d.value / total) * 100)}%` : '—'}
            </span>
          </div>
        ))}
      </div>
    </div>
  )
}

// Horizontal bar chart — good for long category labels (statuses, names).
export function HBarChart({ data, nameKey, valueKey, formatTooltip, theme, colorByEntry = false, color = '#0ea5e9' }) {
  return (
    <ResponsiveContainer width="100%" height="100%">
      <BarChart data={data} layout="vertical" margin={{ top: 4, right: 16, bottom: 0, left: 4 }}>
        <CartesianGrid strokeDasharray="3 3" stroke={theme.grid} horizontal={false} />
        <XAxis type="number" tick={theme.tick} tickFormatter={compactNumber} axisLine={{ stroke: theme.axisLine }} tickLine={false} />
        <YAxis type="category" dataKey={nameKey} width={140} tick={theme.tick} axisLine={false} tickLine={false} />
        <Tooltip {...theme.tooltip} cursor={{ fill: theme.dark ? 'rgba(148,163,184,0.08)' : 'rgba(100,116,139,0.08)' }} formatter={formatTooltip} />
        <Bar dataKey={valueKey} radius={[0, 4, 4, 0]} maxBarSize={22}>
          {data.map((entry, i) => (
            <Cell key={i} fill={colorByEntry ? entry.color || color : color} />
          ))}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  )
}

// Daily inbound vs outbound value as two mirrored gradient areas.
export function TrendAreaChart({ data, formatMoney, theme, height = '100%' }) {
  return (
    <ResponsiveContainer width="100%" height={height}>
      <AreaChart data={data} margin={{ top: 5, right: 10, left: 0, bottom: 0 }}>
        <defs>
          <linearGradient id="rpt-in" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#10b981" stopOpacity={0.28} />
            <stop offset="100%" stopColor="#10b981" stopOpacity={0} />
          </linearGradient>
          <linearGradient id="rpt-out" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#ef4444" stopOpacity={0.28} />
            <stop offset="100%" stopColor="#ef4444" stopOpacity={0} />
          </linearGradient>
        </defs>
        <CartesianGrid strokeDasharray="3 3" stroke={theme.grid} vertical={false} />
        <XAxis dataKey="date" tickFormatter={(d) => (d || '').slice(5)} tick={theme.tick} axisLine={{ stroke: theme.axisLine }} tickLine={false} />
        <YAxis tick={theme.tick} tickFormatter={compactNumber} axisLine={false} tickLine={false} />
        <Tooltip {...theme.tooltip} formatter={(value, name) => [formatMoney(value), name]} />
        <Area type="monotone" dataKey="inboundValue" name="Inbound" stroke="#10b981" fill="url(#rpt-in)" strokeWidth={2} />
        <Area type="monotone" dataKey="outboundValue" name="Outbound" stroke="#ef4444" fill="url(#rpt-out)" strokeWidth={2} />
      </AreaChart>
    </ResponsiveContainer>
  )
}
