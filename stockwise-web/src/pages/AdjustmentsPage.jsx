import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Plus, Trash2 } from 'lucide-react'
import { api, hasPermission, qs } from '../lib/api-client.js'
import { P } from '../lib/permissions.js'
import { useAuth } from '../app/providers.jsx'
import { formatQty, formatDateTime } from '../lib/formatters.js'
import { EmptyState, ErrorState, Field, Modal, PageHeader, TableSkeleton, toast } from '../components/ui/index.jsx'

export default function AdjustmentsPage() {
  const auth = useAuth()
  const queryClient = useQueryClient()
  const [modalOpen, setModalOpen] = useState(false)

  // Recent adjustment movements for visibility.
  const recent = useQuery({
    queryKey: ['adjustment-movements'],
    queryFn: () => api.get('/api/v1/reports/adjustments?pageSize=20'),
    enabled: hasPermission(P.InventoryRead)
  })

  const canAdjust = hasPermission(P.InventoryAdjust)

  return (
    <div>
      <PageHeader
        title="Stock adjustments"
        description="Correct stock levels through auditable ledger movements. Posted adjustments are immutable — reverse with another adjustment."
        actions={canAdjust && (
          <button className="btn-primary" onClick={() => setModalOpen(true)}>
            <Plus className="h-4 w-4" /> New adjustment
          </button>
        )}
      />

      <div className="card">
        {recent.isLoading ? <TableSkeleton rows={6} cols={5} /> : recent.isError ? (
          <ErrorState error={recent.error} onRetry={recent.refetch} />
        ) : (recent.data?.items || []).length === 0 ? (
          <EmptyState title="No adjustments yet" description="Adjustments and count variances appear here." />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[700px]">
              <thead>
                <tr><th className="th">When</th><th className="th">Product</th><th className="th">Warehouse</th>
                  <th className="th text-right">Δ Qty</th><th className="th">Reason</th><th className="th">By</th></tr>
              </thead>
              <tbody>
                {recent.data.items.map((m, i) => (
                  <tr key={i} className="table-row">
                    <td className="td whitespace-nowrap text-slate-500">{formatDateTime(m.createdAtUtc, auth.dateFormat)}</td>
                    <td className="td">{m.productName}</td>
                    <td className="td text-slate-500">{m.warehouseName}</td>
                    <td className={`td text-right font-semibold ${m.quantityDelta > 0 ? 'text-emerald-600' : 'text-red-600'}`}>
                      {m.quantityDelta > 0 ? '+' : ''}{Number(m.quantityDelta)}
                    </td>
                    <td className="td max-w-[260px] truncate text-slate-500">{m.reason}</td>
                    <td className="td text-slate-500">{m.userName}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {modalOpen && (
        <AdjustmentModal
          onClose={() => setModalOpen(false)}
          onSaved={() => {
            setModalOpen(false)
            queryClient.invalidateQueries()
          }}
        />
      )}
    </div>
  )
}

function AdjustmentModal({ onClose, onSaved }) {
  const [warehouseId, setWarehouseId] = useState('')
  const [lines, setLines] = useState([{ productId: '', batchId: '', quantityDelta: '', reason: '' }])
  const [idempotencyKey] = useState(() => crypto.randomUUID())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)

  const { data: warehouses } = useQuery({ queryKey: ['warehouses'], queryFn: () => api.get('/api/v1/warehouses') })
  const { data: products } = useQuery({
    queryKey: ['products', 'active-all'],
    queryFn: () => api.get('/api/v1/products?pageSize=200&status=Active')
  })
  const selectedWarehouse = useQuery({
    queryKey: ['warehouse-detail', warehouseId],
    queryFn: () => api.get(`/api/v1/inventory/balances?warehouseId=${warehouseId}&pageSize=200`),
    enabled: !!warehouseId
  })

  const setLine = (i, key, value) => {
    setLines((ls) => ls.map((l, idx) => (idx === i ? { ...l, [key]: value } : l)))
  }

  // Batches for the selected product (batch-tracked only)
  const batchProduct = lines.find((l) => l.productId)
  const batches = useQuery({
    queryKey: ['product-batches', batchProduct?.productId],
    queryFn: () => api.get(`/api/v1/inventory/expiry?includeExpired=false`),
    enabled: !!batchProduct?.productId
  })

  const submit = async (e) => {
    e.preventDefault()
    setError(null)
    if (!warehouseId) return setError('Select a warehouse.')
    for (const l of lines) {
      if (!l.productId) return setError('Select a product on every line.')
      if (!l.quantityDelta || Number(l.quantityDelta) === 0) return setError('Enter a non-zero quantity on every line.')
      if (!l.reason?.trim()) return setError('A reason is required on every line (audit trail).')
    }
    setBusy(true)
    try {
      await api.post('/api/v1/inventory/adjustments', {
        warehouseId,
        idempotencyKey,
        lines: lines.map((l) => ({
          productId: l.productId,
          quantityDelta: Number(l.quantityDelta),
          batchId: l.batchId || null,
          reason: l.reason.trim()
        }))
      })
      toast.success('Adjustment posted to the ledger.')
      onSaved()
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open onClose={onClose} title="New stock adjustment" size="lg"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn-primary" onClick={submit} disabled={busy}>Post adjustment</button>
        </>
      }>
      {error && <div className="mb-3 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700" role="alert">{error}</div>}
      <div className="space-y-4">
        <Field label="Warehouse" required>
          <select className="input" value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)}>
            <option value="">Select warehouse…</option>
            {(warehouses || []).map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
          </select>
        </Field>

        {lines.map((line, i) => (
          <div key={i} className="grid grid-cols-1 gap-3 rounded-lg border border-slate-200 p-3 dark:border-slate-700 sm:grid-cols-12">
            <div className="sm:col-span-4">
              <Field label={i === 0 ? 'Product' : ''}>
                <select className="input" value={line.productId}
                  onChange={(e) => setLine(i, 'productId', e.target.value)}>
                  <option value="">Select product…</option>
                  {(products?.items || []).map((p) => (
                    <option key={p.id} value={p.id}>{p.name} ({p.sku}){p.trackBatches ? ' — batched' : ''}</option>
                  ))}
                </select>
              </Field>
            </div>
            <div className="sm:col-span-2">
              <Field label={i === 0 ? 'Δ Quantity' : ''} hint="Use negative to remove">
                <input type="number" step="any" className="input" value={line.quantityDelta}
                  onChange={(e) => setLine(i, 'quantityDelta', e.target.value)} />
              </Field>
            </div>
            <div className="sm:col-span-4">
              <Field label={i === 0 ? 'Reason (audited)' : ''}>
                <input className="input" value={line.reason} onChange={(e) => setLine(i, 'reason', e.target.value)}
                  placeholder="e.g. Damaged goods, cycle count fix" />
              </Field>
            </div>
            <div className="flex items-end sm:col-span-2">
              {lines.length > 1 && (
                <button type="button" className="btn-ghost p-2 text-red-500" onClick={() => setLines((ls) => ls.filter((_, idx) => idx !== i))}
                  aria-label="Remove line">
                  <Trash2 className="h-4 w-4" />
                </button>
              )}
            </div>
          </div>
        ))}

        <button type="button" className="btn-secondary text-sm" onClick={() => setLines((ls) => [...ls, { productId: '', batchId: '', quantityDelta: '', reason: '' }])}>
          <Plus className="h-4 w-4" /> Add line
        </button>

        <p className="text-xs text-slate-400">
          Adjustments are posted as immutable ledger movements. Negative stock is rejected unless your organization's policy allows it.
        </p>
      </div>
    </Modal>
  )
}
