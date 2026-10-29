import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ShoppingCart, Plus, Search, Send, CheckCircle2, XCircle, Trash2, Printer } from 'lucide-react'
import { api, hasPermission, qs } from '../lib/api-client.js'
import { P } from '../lib/permissions.js'
import { useAuth } from '../app/providers.jsx'
import { formatMoney, formatDateTime, formatDate } from '../lib/formatters.js'
import { Badge, ConfirmDialog, Field, Modal, PageHeader, toast } from '../components/ui/index.jsx'
import { DataTable, TableToolbar } from '../components/data-display/DataTable.jsx'

const statusColors = {
  Draft: 'gray', PendingApproval: 'amber', Approved: 'blue', Ordered: 'purple',
  PartiallyReceived: 'blue', Received: 'green', Rejected: 'red', Cancelled: 'gray'
}

export default function PurchaseOrdersPage() {
  const auth = useAuth()
  const queryClient = useQueryClient()
  const [params, setParams] = useQueryState()
  const status = params.get('status') || ''
  const search = params.get('search') || ''
  const page = parseInt(params.get('page') || '1', 10)
  const pageSize = parseInt(params.get('pageSize') || '5', 10)
  const [createOpen, setCreateOpen] = useState(false)

  const query = useQuery({
    queryKey: ['purchase-orders', { status, search, page }],
    queryFn: () => api.get(`/api/v1/purchase-orders${qs({ status, search, page, pageSize })}`),
    enabled: hasPermission(P.PurchaseOrdersRead)
  })

  const setParam = (key, value) => {
    const next = new URLSearchParams(params)
    if (value) next.set(key, value)
    else next.delete(key)
    if (key !== 'page') next.set('page', '1')
    setParams(next)
  }

  return (
    <div>
      <PageHeader
        title="Purchase orders"
        description="Replenishment orders with configurable approval and partial receiving."
        actions={hasPermission(P.PurchaseOrdersCreate) && (
          <button className="btn-primary" onClick={() => setCreateOpen(true)}><Plus className="h-4 w-4" /> New purchase order</button>
        )}
      />
      <TableToolbar>
        <div className="relative flex-1">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input className="input pl-9" placeholder="Search number or supplier…" value={search} onChange={(e) => setParam('search', e.target.value)} aria-label="Search purchase orders" />
        </div>
        <select className="input sm:w-52" value={status} onChange={(e) => setParam('status', e.target.value)} aria-label="Filter by status">
          <option value="">All statuses</option>
          {Object.keys(statusColors).map((s) => <option key={s} value={s}>{s}</option>)}
        </select>
      </TableToolbar>
      <div className="mt-4" />
      <DataTable
        loading={query.isLoading}
        error={query.isError ? query.error : null}
        onRetry={query.refetch}
        rows={query.data?.items}
        empty={{ icon: ShoppingCart, title: 'No purchase orders', description: 'Create one to start replenishing stock.' }}
        minWidth="min-w-[820px]"
        page={query.data?.page}
        pageSize={query.data?.pageSize}
        totalCount={query.data?.totalCount}
        onPage={(p) => setParam('page', String(p))}
        onPageSize={(n) => setParam('pageSize', String(n))}
        columns={[
          {
            key: 'number', header: 'Number',
            render: (po) => (
              <Link className="font-mono text-sm font-medium text-accent-600 hover:underline" to={`/purchase-orders/${po.id}`}>{po.number}</Link>
            )
          },
          { key: 'supplierName', header: 'Supplier', className: 'text-slate-500' },
          {
            key: 'status', header: 'Status',
            render: (po) => <Badge color={statusColors[po.status]}>{po.status}</Badge>
          },
          {
            key: 'total', header: 'Total', align: 'right',
            render: (po) => <span className="tabular-nums">{formatMoney(po.total, po.currencyCode)}</span>
          },
          {
            key: 'expectedDeliveryDate', header: 'Expected',
            render: (po) => <span className="text-slate-500">{po.expectedDeliveryDate ? formatDate(po.expectedDeliveryDate, auth.dateFormat) : '—'}</span>
          },
          {
            key: 'createdAt', header: 'Created',
            render: (po) => <span className="text-slate-500">{formatDateTime(po.createdAt, auth.dateFormat)}</span>
          },
          {
            key: 'actions', header: '', align: 'right',
            render: (po) => (
              <Link className="text-sm font-medium text-accent-600 hover:underline" to={`/purchase-orders/${po.id}`}>Open</Link>
            )
          }
        ]}
      />

      {createOpen && <CreatePoModal onClose={() => setCreateOpen(false)} />}
    </div>
  )
}

function useQueryState() {
  const navigate = useNavigate()
  const [params] = useState(() => new URLSearchParams(window.location.search))
  const setParams = (next) => navigate({ search: next.toString() }, { replace: true })
  return [params, setParams]
}

export function CreatePoModal({ open, onClose, presetSupplierId }) {
  const queryClient = useQueryClient()
  const [supplierId, setSupplierId] = useState(presetSupplierId || '')
  const [warehouseId, setWarehouseId] = useState('')
  const [expected, setExpected] = useState('')
  const [notes, setNotes] = useState('')
  const [lines, setLines] = useState([{ productId: '', quantity: '', unitCost: '', discountPercent: '0', taxPercent: '0' }])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)

  const { data: suppliers } = useQuery({ queryKey: ['suppliers', 'active'], queryFn: () => api.get('/api/v1/suppliers?active=true&pageSize=200'), enabled: open })
  const { data: warehouses } = useQuery({ queryKey: ['warehouses'], queryFn: () => api.get('/api/v1/warehouses'), enabled: open })
  const { data: products } = useQuery({ queryKey: ['products', 'active-all'], queryFn: () => api.get('/api/v1/products?pageSize=200&status=Active'), enabled: open })

  const setLine = (i, key, value) => setLines((ls) => ls.map((l, idx) => (idx === i ? { ...l, [key]: value } : l)))
  const lineTotal = (l) => {
    const q = Number(l.quantity || 0), c = Number(l.unitCost || 0), d = Number(l.discountPercent || 0) / 100, t = Number(l.taxPercent || 0) / 100
    return q * c * (1 - d) * (1 + t)
  }
  const total = lines.reduce((s, l) => s + lineTotal(l), 0)

  const submit = async (e) => {
    e.preventDefault()
    setError(null)
    if (!supplierId) return setError('Select a supplier.')
    if (!warehouseId) return setError('Select a warehouse.')
    for (const l of lines) {
      if (!l.productId) return setError('Select a product on every line.')
      if (!Number(l.quantity)) return setError('Quantity must be positive on every line.')
      if (l.unitCost === '' || Number(l.unitCost) < 0) return setError('Unit cost is required on every line.')
    }
    setBusy(true)
    try {
      const res = await api.post('/api/v1/purchase-orders', {
        supplierId, warehouseId,
        expectedDeliveryDate: expected ? new Date(expected).toISOString() : null,
        notes,
        lines: lines.map((l) => ({
          productId: l.productId, quantity: Number(l.quantity), unitCost: Number(l.unitCost),
          discountPercent: Number(l.discountPercent || 0), taxPercent: Number(l.taxPercent || 0)
        }))
      })
      toast.success(`Purchase order ${res.number} created as draft.`)
      queryClient.invalidateQueries({ queryKey: ['purchase-orders'] })
      onClose()
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open={open !== false} onClose={onClose} title="New purchase order" size="lg"
      footer={
        <>
          <span className="mr-auto text-sm text-slate-500">Total: <strong>{formatMoney(total)}</strong></span>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn-primary" onClick={submit} disabled={busy}>Create draft</button>
        </>
      }>
      {error && <div className="mb-3 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700" role="alert">{error}</div>}
      <div className="space-y-4">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <Field label="Supplier" required>
            <select className="input" value={supplierId} onChange={(e) => setSupplierId(e.target.value)}>
              <option value="">Select…</option>
              {(suppliers?.items || []).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
            </select>
          </Field>
          <Field label="Receiving warehouse" required>
            <select className="input" value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)}>
              <option value="">Select…</option>
              {(warehouses || []).map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
            </select>
          </Field>
          <Field label="Expected delivery"><input type="date" className="input" value={expected} onChange={(e) => setExpected(e.target.value)} /></Field>
        </div>

        {lines.map((l, i) => (
          <div key={i} className="grid grid-cols-1 gap-3 rounded-lg border border-slate-200 p-3 dark:border-slate-700 sm:grid-cols-12">
            <div className="sm:col-span-4">
              <Field label={i === 0 ? 'Product' : ''}>
                <select className="input" value={l.productId} onChange={(e) => {
                  const p = products?.items.find((x) => x.id === e.target.value)
                  setLine(i, 'productId', e.target.value)
                  if (p?.costPrice != null && l.unitCost === '') setLine(i, 'unitCost', String(p.costPrice))
                }}>
                  <option value="">Select product…</option>
                  {(products?.items || []).map((p) => <option key={p.id} value={p.id}>{p.name} ({p.sku})</option>)}
                </select>
              </Field>
            </div>
            <div className="sm:col-span-2"><Field label={i === 0 ? 'Quantity' : ''}><input type="number" step="any" min="0" className="input" value={l.quantity} onChange={(e) => setLine(i, 'quantity', e.target.value)} /></Field></div>
            <div className="sm:col-span-2"><Field label={i === 0 ? 'Unit cost' : ''}><input type="number" step="0.0001" min="0" className="input" value={l.unitCost} onChange={(e) => setLine(i, 'unitCost', e.target.value)} /></Field></div>
            <div className="sm:col-span-2"><Field label={i === 0 ? 'Discount %' : ''}><input type="number" step="any" min="0" max="100" className="input" value={l.discountPercent} onChange={(e) => setLine(i, 'discountPercent', e.target.value)} /></Field></div>
            <div className="sm:col-span-1"><Field label={i === 0 ? 'Tax %' : ''}><input type="number" step="any" min="0" className="input" value={l.taxPercent} onChange={(e) => setLine(i, 'taxPercent', e.target.value)} /></Field></div>
            <div className="flex items-end sm:col-span-1">
              {lines.length > 1 && <button type="button" className="btn-ghost p-2 text-red-500" onClick={() => setLines((ls) => ls.filter((_, idx) => idx !== i))} aria-label="Remove line"><Trash2 className="h-4 w-4" /></button>}
            </div>
          </div>
        ))}
        <button type="button" className="btn-secondary text-sm" onClick={() => setLines((ls) => [...ls, { productId: '', quantity: '', unitCost: '', discountPercent: '0', taxPercent: '0' }])}>
          <Plus className="h-4 w-4" /> Add line
        </button>
        <Field label="Notes"><input className="input" value={notes} onChange={(e) => setNotes(e.target.value)} /></Field>
      </div>
    </Modal>
  )
}
