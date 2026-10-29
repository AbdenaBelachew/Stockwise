import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeftRight, ArrowLeft, Send, PackageCheck, XCircle, Plus, Trash2 } from 'lucide-react'
import { api, hasPermission } from '../lib/api-client.js'
import { P } from '../lib/permissions.js'
import { useAuth } from '../app/providers.jsx'
import { formatDateTime, formatQty } from '../lib/formatters.js'
import { Badge, ConfirmDialog, Field, Modal, PageHeader, toast } from '../components/ui/index.jsx'
import { DataTable, TableToolbar } from '../components/data-display/DataTable.jsx'

export default function TransfersPage() {
  const [params, setParams] = useSearchParams()
  const status = params.get('status') || ''
  const page = parseInt(params.get('page') || '1', 10)
  const pageSize = parseInt(params.get('pageSize') || '5', 10)
  const [createOpen, setCreateOpen] = useState(false)

  const query = useQuery({
    queryKey: ['transfers', { status, page, pageSize }],
    queryFn: () => api.get(`/api/v1/inventory/transfers${qs({ status, page, pageSize })}`),
    enabled: hasPermission(P.InventoryRead)
  })

  const statusColor = { Draft: 'gray', InTransit: 'amber', Received: 'green', Cancelled: 'red' }

  return (
    <div>
      <PageHeader
        title="Transfers"
        description="Move stock between warehouses. Dispatch deducts from the source; receipt adds to the destination."
        actions={hasPermission(P.InventoryTransfer) && (
          <button className="btn-primary" onClick={() => setCreateOpen(true)}>
            <Plus className="h-4 w-4" /> New transfer
          </button>
        )}
      />
      <TableToolbar>
        <select className="input sm:w-44" value={status} onChange={(e) => { const n = new URLSearchParams(params); e.target.value ? n.set('status', e.target.value) : n.delete('status'); n.set('page', '1'); setParams(n) }} aria-label="Filter by status">
          <option value="">All statuses</option>
          {Object.keys(statusColor).map((s) => <option key={s} value={s}>{s}</option>)}
        </select>
      </TableToolbar>
      <div className="mt-4" />
      <DataTable
        loading={query.isLoading}
        error={query.isError ? query.error : null}
        onRetry={query.refetch}
        rows={query.data?.items}
        empty={{ icon: ArrowLeftRight, title: 'No transfers', description: 'Create a transfer to move stock between warehouses.' }}
        minWidth="min-w-[700px]"
        page={query.data?.page}
        pageSize={query.data?.pageSize}
        totalCount={query.data?.totalCount}
        onPage={(p) => { const n = new URLSearchParams(params); n.set('page', String(p)); setParams(n) }}
        onPageSize={(n) => { const next = new URLSearchParams(params); next.set('pageSize', String(n)); next.set('page', '1'); setParams(next) }}
        columns={[
          {
            key: 'number', header: 'Number',
            render: (t) => <Link to={`/transfers/${t.id}`} className="font-mono text-sm font-medium text-accent-600 hover:underline">{t.number}</Link>
          },
          { key: 'sourceWarehouseName', header: 'From', render: (t) => <span className="text-slate-500">{t.sourceWarehouseName}</span> },
          { key: 'destinationWarehouseName', header: 'To', render: (t) => <span className="text-slate-500">{t.destinationWarehouseName}</span> },
          { key: 'status', header: 'Status', render: (t) => <Badge color={statusColor[t.status]}>{t.status}</Badge> },
          {
            key: 'createdAt', header: 'Created',
            render: (t) => <span className="text-slate-500">{formatDateTime(t.createdAt)}</span>
          },
          {
            key: 'actions', header: '', align: 'right',
            render: (t) => <Link to={`/transfers/${t.id}`} className="text-sm font-medium text-accent-600 hover:underline">View</Link>
          }
        ]}
      />

      {createOpen && <CreateTransferModal onClose={() => setCreateOpen(false)} />}
    </div>
  )
}

function CreateTransferModal({ onClose }) {
  const queryClient = useQueryClient()
  const [source, setSource] = useState('')
  const [destination, setDestination] = useState('')
  const [notes, setNotes] = useState('')
  const [lines, setLines] = useState([{ productId: '', batchId: '', quantity: '' }])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)

  const { data: warehouses } = useQuery({ queryKey: ['warehouses'], queryFn: () => api.get('/api/v1/warehouses') })
  const { data: products } = useQuery({
    queryKey: ['products', 'active-all'],
    queryFn: () => api.get('/api/v1/products?pageSize=200&status=Active')
  })
  const sourceBalances = useQuery({
    queryKey: ['balances', source],
    queryFn: () => api.get(`/api/v1/inventory/balances?warehouseId=${source}&pageSize=200`),
    enabled: !!source
  })
  const batchProduct = lines.find((l) => l.productId)
  const expiry = useQuery({
    queryKey: ['expiry', source],
    queryFn: () => api.get(`/api/v1/inventory/expiry?includeExpired=false${source ? `&warehouseId=${source}` : ''}`),
    enabled: !!batchProduct?.productId
  })
  const selectedProduct = products?.items.find((p) => p.id === batchProduct?.productId)

  const submit = async (e) => {
    e.preventDefault()
    setError(null)
    if (!source || !destination) return setError('Select source and destination warehouses.')
    if (source === destination) return setError('Source and destination must differ.')
    for (const l of lines) {
      if (!l.productId) return setError('Select a product on every line.')
      if (!l.quantity || Number(l.quantity) <= 0) return setError('Quantity must be positive.')
      if (selectedProduct?.trackBatches && !l.batchId) return setError(`'${selectedProduct.name}' is batch-tracked: select a batch.`)
    }
    setBusy(true)
    try {
      await api.post('/api/v1/inventory/transfers', {
        sourceWarehouseId: source,
        destinationWarehouseId: destination,
        notes,
        idempotencyKey: crypto.randomUUID(),
        lines: lines.map((l) => ({ productId: l.productId, batchId: l.batchId || null, quantity: Number(l.quantity) }))
      })
      toast.success('Transfer created as draft. Dispatch it to move the stock.')
      queryClient.invalidateQueries({ queryKey: ['transfers'] })
      onClose()
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open onClose={onClose} title="New transfer" size="lg"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn-primary" onClick={submit} disabled={busy}>Create draft</button>
        </>
      }>
      {error && <div className="mb-3 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700" role="alert">{error}</div>}
      <div className="space-y-4">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="From warehouse" required>
            <select className="input" value={source} onChange={(e) => setSource(e.target.value)}>
              <option value="">Select…</option>
              {(warehouses || []).map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
            </select>
          </Field>
          <Field label="To warehouse" required>
            <select className="input" value={destination} onChange={(e) => setDestination(e.target.value)}>
              <option value="">Select…</option>
              {(warehouses || []).filter((w) => w.id !== source).map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
            </select>
          </Field>
        </div>

        {lines.map((line, i) => (
          <div key={i} className="grid grid-cols-1 gap-3 rounded-lg border border-slate-200 p-3 dark:border-slate-700 sm:grid-cols-12">
            <div className="sm:col-span-5">
              <Field label={i === 0 ? 'Product' : ''}>
                <select className="input" value={line.productId} onChange={(e) => setLine(setLines, i, 'productId', e.target.value, 'batchId')}>
                  <option value="">Select product…</option>
                  {(products?.items || []).filter((p) => sourceBalances.data?.items.some((b) => b.productId === p.id && b.quantity > 0)).map((p) => (
                    <option key={p.id} value={p.id}>{p.name} ({p.sku}){p.trackBatches ? ' — batched' : ''}</option>
                  ))}
                </select>
              </Field>
            </div>
            {selectedProduct?.trackBatches && (
              <div className="sm:col-span-3">
                <Field label={i === 0 ? 'Batch' : ''}>
                  <select className="input" value={line.batchId} onChange={(e) => setLine(setLines, i, 'batchId', e.target.value)}>
                    <option value="">Select batch…</option>
                    {(expiry.data || []).filter((b) => b.productId === line.productId && b.quantityRemaining > 0).map((b) => (
                      <option key={b.batchId} value={b.batchId}>{b.batchNumber} — {Number(b.quantityRemaining)} left</option>
                    ))}
                  </select>
                </Field>
              </div>
            )}
            <div className="sm:col-span-2">
              <Field label={i === 0 ? 'Quantity' : ''}>
                <input type="number" step="any" min="0" className="input" value={line.quantity} onChange={(e) => setLine(setLines, i, 'quantity', e.target.value)} />
              </Field>
            </div>
            <div className="flex items-end sm:col-span-2">
              {lines.length > 1 && (
                <button type="button" className="btn-ghost p-2 text-red-500" onClick={() => setLines((ls) => ls.filter((_, idx) => idx !== i))} aria-label="Remove line">
                  <Trash2 className="h-4 w-4" />
                </button>
              )}
            </div>
          </div>
        ))}
        <button type="button" className="btn-secondary text-sm" onClick={() => setLines((ls) => [...ls, { productId: '', batchId: '', quantity: '' }])}>
          <Plus className="h-4 w-4" /> Add line
        </button>
        <Field label="Notes"><input className="input" value={notes} onChange={(e) => setNotes(e.target.value)} /></Field>
      </div>
    </Modal>
  )
}

function setLine(setLines, i, key, value, clearKey) {
  setLines((ls) => ls.map((l, idx) => (idx === i ? { ...l, [key]: value, ...(clearKey ? { [clearKey]: '' } : {}) } : l)))
}

export function TransferDetailPage() {
  const { id } = useParams()
  const auth = useAuth()
  const queryClient = useQueryClient()
  const [confirm, setConfirm] = useState(null) // 'dispatch' | 'receive' | 'cancel'

  const transfer = useQuery({
    queryKey: ['transfers', id],
    queryFn: () => api.get(`/api/v1/inventory/transfers/${id}`)
  })

  const action = useMutation({
    mutationFn: (verb) => api.post(`/api/v1/inventory/transfers/${id}/${verb}`, {}),
    onSuccess: (_, verb) => {
      toast.success(verb === 'dispatch' ? 'Stock dispatched from source warehouse.' : verb === 'receive' ? 'Stock received at destination.' : 'Transfer cancelled.')
      queryClient.invalidateQueries({ queryKey: ['transfers'] })
      queryClient.invalidateQueries({ queryKey: ['balances'] })
      setConfirm(null)
    },
    onError: (e) => {
      toast.error(e.message)
      setConfirm(null)
    }
  })

  if (transfer.isLoading) return <TableSkeleton rows={6} cols={4} />
  if (transfer.isError) return <ErrorState error={transfer.error} onRetry={transfer.refetch} />
  const t = transfer.data

  return (
    <div>
      <div className="mb-4">
        <Link to="/transfers" className="flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700 dark:text-slate-400">
          <ArrowLeft className="h-4 w-4" /> Transfers
        </Link>
      </div>
      <PageHeader
        title={`Transfer ${t.number}`}
        description={`${t.sourceWarehouseName} → ${t.destinationWarehouseName}`}
        actions={
          <>
            {t.status === 'Draft' && hasPermission(P.InventoryTransfer) && (
              <button className="btn-primary" onClick={() => setConfirm('dispatch')}><Send className="h-4 w-4" /> Dispatch</button>
            )}
            {t.status === 'InTransit' && hasPermission(P.InventoryTransfer) && (
              <button className="btn-primary" onClick={() => setConfirm('receive')}><PackageCheck className="h-4 w-4" /> Receive</button>
            )}
            {(t.status === 'Draft' || t.status === 'InTransit') && hasPermission(P.InventoryTransfer) && (
              <button className="btn-secondary text-red-600" onClick={() => setConfirm('cancel')}><XCircle className="h-4 w-4" /> Cancel</button>
            )}
          </>
        }
      />

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        <div className="card p-5">
          <h2 className="mb-4 text-sm font-semibold">Details</h2>
          <dl className="space-y-3 text-sm">
            <Row label="Status"><Badge color={{ Draft: 'gray', InTransit: 'amber', Received: 'green', Cancelled: 'red' }[t.status]}>{t.status}</Badge></Row>
            <Row label="Source">{t.sourceWarehouseName}</Row>
            <Row label="Destination">{t.destinationWarehouseName}</Row>
            <Row label="Created">{formatDateTime(t.createdAt, auth.dateFormat)}</Row>
            <Row label="Dispatched">{t.dispatchedAt ? formatDateTime(t.dispatchedAt, auth.dateFormat) : '—'}</Row>
            <Row label="Received">{t.receivedAt ? formatDateTime(t.receivedAt, auth.dateFormat) : '—'}</Row>
            <Row label="Notes">{t.notes || '—'}</Row>
          </dl>
        </div>
        <div className="card lg:col-span-2">
          <div className="border-b border-slate-100 px-4 py-3 dark:border-slate-800">
            <h2 className="text-sm font-semibold">Lines</h2>
          </div>
          <table className="w-full">
            <thead><tr><th className="th">Product</th><th className="th">Batch</th><th className="th text-right">Quantity</th></tr></thead>
            <tbody>
              {t.lines.map((l, i) => (
                <tr key={i} className="table-row">
                  <td className="td">{l.productName}<p className="text-xs text-slate-400">{l.sku}</p></td>
                  <td className="td font-mono text-xs text-slate-500">{l.batchNumber || '—'}</td>
                  <td className="td text-right font-semibold tabular-nums">{formatQty(l.quantity, l.unitSymbol)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <ConfirmDialog
        open={confirm === 'dispatch'}
        onClose={() => setConfirm(null)}
        title="Dispatch transfer?"
        message={`Stock will be deducted from ${t.sourceWarehouseName} immediately and marked in transit until received.`}
        confirmLabel="Dispatch" busy={action.isPending}
        onConfirm={() => action.mutate('dispatch')}
      />
      <ConfirmDialog
        open={confirm === 'receive'}
        onClose={() => setConfirm(null)}
        title="Receive transfer?"
        message={`Stock will be added to ${t.destinationWarehouseName} with the same batch identities.`}
        confirmLabel="Receive" busy={action.isPending}
        onConfirm={() => action.mutate('receive')}
      />
      <ConfirmDialog
        open={confirm === 'cancel'}
        onClose={() => setConfirm(null)}
        title="Cancel transfer?"
        message={t.status === 'InTransit' ? 'In-transit stock will be returned to the source warehouse via a compensating movement.' : 'The draft transfer will be cancelled.'}
        confirmLabel="Cancel transfer" danger busy={action.isPending}
        onConfirm={() => action.mutate('cancel')}
      />
    </div>
  )
}

function Row({ label, children }) {
  return (
    <div className="flex items-center justify-between gap-4">
      <dt className="text-slate-500 dark:text-slate-400">{label}</dt>
      <dd className="text-right font-medium">{children}</dd>
    </div>
  )
}
