import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, ClipboardCheck, Plus, XCircle, CheckCheck, Send, UploadCloud } from 'lucide-react'
import { api, hasPermission, qs } from '../lib/api-client.js'
import { P } from '../lib/permissions.js'
import { useAuth } from '../app/providers.jsx'
import { formatDateTime, formatQty } from '../lib/formatters.js'
import { Badge, ConfirmDialog, EmptyState, ErrorState, Field, Modal, PageHeader, Pagination, TableSkeleton, toast } from '../components/ui/index.jsx'
import { DataTable, TableToolbar } from '../components/data-display/DataTable.jsx'

export default function StockCountsPage() {
  const auth = useAuth()
  const queryClient = useQueryClient()
  const [params, setParams] = useQueryPageState()
  const page = parseInt(params.get('page') || '1', 10)
  const pageSize = parseInt(params.get('pageSize') || '5', 10)
  const setPage = (p) => { const n = new URLSearchParams(params); n.set('page', String(p)); setParams(n) }
  const setPageSize = (n) => { const next = new URLSearchParams(params); next.set('pageSize', String(n)); next.set('page', '1'); setParams(next) }
  const [createOpen, setCreateOpen] = useState(false)

  const query = useQuery({
    queryKey: ['stock-counts', page, pageSize],
    queryFn: () => api.get(`/api/v1/inventory/stock-counts${qs({ page, pageSize })}`),
    enabled: hasPermission(P.InventoryRead)
  })
  const statusColor = { Draft: 'gray', Submitted: 'amber', Approved: 'blue', Posted: 'green', Cancelled: 'red' }

  return (
    <div>
      <PageHeader
        title="Stock counts"
        description="Count sessions snapshot expected quantities at creation; approved variances post as ledger movements exactly once."
        actions={hasPermission(P.InventoryCount) && (
          <button className="btn-primary" onClick={() => setCreateOpen(true)}><Plus className="h-4 w-4" /> New count</button>
        )}
      />
      <div className="mt-4" />
      <DataTable
        loading={query.isLoading}
        error={query.isError ? query.error : null}
        onRetry={query.refetch}
        rows={query.data?.items}
        empty={{ icon: ClipboardCheck, title: 'No counts yet', description: 'Create a count session for a warehouse to verify stock.' }}
        minWidth="min-w-[680px]"
        page={query.data?.page}
        pageSize={query.data?.pageSize}
        totalCount={query.data?.totalCount}
        onPage={setPage}
        onPageSize={setPageSize}
        columns={[
          {
            key: 'number', header: 'Number',
            render: (c) => <Link className="font-mono text-sm font-medium text-accent-600 hover:underline" to={`/stock-counts/${c.id}`}>{c.number}</Link>
          },
          { key: 'warehouseName', header: 'Warehouse', render: (c) => <span className="text-slate-500">{c.warehouseName}</span> },
          { key: 'status', header: 'Status', render: (c) => <Badge color={statusColor[c.status]}>{c.status}</Badge> },
          { key: 'blindCount', header: 'Blind', render: (c) => <span className="text-slate-500">{c.blindCount ? 'Yes' : 'No'}</span> },
          {
            key: 'createdAt', header: 'Created',
            render: (c) => <span className="text-slate-500">{formatDateTime(c.createdAt, auth.dateFormat)}</span>
          },
          {
            key: 'actions', header: '', align: 'right',
            render: (c) => <Link className="text-sm font-medium text-accent-600 hover:underline" to={`/stock-counts/${c.id}`}>Open</Link>
          }
        ]}
      />
      {createOpen && <CreateCountModal onClose={() => setCreateOpen(false)} />}
    </div>
  )
}
function useQueryPageState() {
  const navigate = useNavigate()
  const [params] = useState(() => new URLSearchParams(window.location.search))
  const setParams = (next) => navigate({ search: next.toString() }, { replace: true })
  return [params, setParams]
}

function CreateCountModal({ onClose }) {
  const queryClient = useQueryClient()
  const [warehouseId, setWarehouseId] = useState('')
  const [blindCount, setBlindCount] = useState(false)
  const [notes, setNotes] = useState('')
  const [selected, setSelected] = useState(new Set())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)

  const { data: warehouses } = useQuery({ queryKey: ['warehouses'], queryFn: () => api.get('/api/v1/warehouses') })
  const balances = useQuery({
    queryKey: ['balances', warehouseId],
    queryFn: () => api.get(`/api/v1/inventory/balances?warehouseId=${warehouseId}&pageSize=200`),
    enabled: !!warehouseId
  })

  const toggle = (pid) => {
    setSelected((s) => {
      const n = new Set(s)
      if (n.has(pid)) n.delete(pid)
      else n.add(pid)
      return n
    })
  }

  const submit = async (e) => {
    e.preventDefault()
    setError(null)
    if (!warehouseId) return setError('Select a warehouse.')
    if (selected.size === 0) return setError('Select at least one product to count.')
    setBusy(true)
    try {
      const res = await api.post('/api/v1/inventory/stock-counts', {
        warehouseId, blindCount, notes, productIds: [...selected].map((id) => ({ productId: id }))
      })
      toast.success(`Count ${res.number} created.`)
      queryClient.invalidateQueries({ queryKey: ['stock-counts'] })
      onClose()
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open onClose={onClose} title="New stock count" size="lg"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn-primary" onClick={submit} disabled={busy}>
            Create count ({selected.size} product{selected.size === 1 ? '' : 's'})
          </button>
        </>
      }>
      {error && <div className="mb-3 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700" role="alert">{error}</div>}
      <div className="space-y-4">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="Warehouse" required>
            <select className="input" value={warehouseId} onChange={(e) => { setWarehouseId(e.target.value); setSelected(new Set()) }}>
              <option value="">Select…</option>
              {(warehouses || []).map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
            </select>
          </Field>
          <div className="flex flex-col justify-center gap-2">
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" className="h-4 w-4" checked={blindCount} onChange={(e) => setBlindCount(e.target.checked)} />
              Blind count — hide expected quantities from the counter
            </label>
          </div>
        </div>
        <Field label="Notes"><input className="input" value={notes} onChange={(e) => setNotes(e.target.value)} /></Field>
        {warehouseId && (
          <div className="max-h-64 overflow-y-auto rounded-md border border-slate-200 dark:border-slate-700">
            {balances.isLoading ? <TableSkeleton rows={4} cols={2} /> : (balances.data?.items || []).length === 0 ? (
              <p className="p-4 text-center text-sm text-slate-400">No products with balances in this warehouse.</p>
            ) : (
              <table className="w-full">
                <tbody>
                  {balances.data.items.map((b) => (
                    <tr key={b.productId} className="table-row">
                      <td className="td">
                        <label className="flex cursor-pointer items-center gap-3">
                          <input type="checkbox" className="h-4 w-4" checked={selected.has(b.productId)} onChange={() => toggle(b.productId)} />
                          <span>
                            <span className="font-medium">{b.productName}</span>
                            <span className="ml-2 text-xs text-slate-400">{b.sku}</span>
                          </span>
                        </label>
                      </td>
                      <td className="td text-right text-slate-500 tabular-nums">{formatQty(b.quantity, b.unitSymbol)} on hand</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        )}
      </div>
    </Modal>
  )
}

export function StockCountDetailPage() {
  const { id } = useParams()
  const auth = useAuth()
  const queryClient = useQueryClient()
  const [counts, setCounts] = useState({})
  const [confirm, setConfirm] = useState(null)

  const count = useQuery({
    queryKey: ['stock-counts', id],
    queryFn: () => api.get(`/api/v1/inventory/stock-counts/${id}`)
  })
  const statusColor = { Draft: 'gray', Submitted: 'amber', Approved: 'blue', Posted: 'green', Cancelled: 'red' }

  const action = useMutation({
    mutationFn: ({ verb, body }) => api.post(`/api/v1/inventory/stock-counts/${id}/${verb}`, body || {}),
    onSuccess: (_, vars) => {
      const msgs = { submit: 'Count submitted.', approve: 'Count approved.', post: 'Variances posted to the ledger.', cancel: 'Count cancelled.' }
      toast.success(msgs[vars.verb] || 'Done.')
      queryClient.invalidateQueries({ queryKey: ['stock-counts'] })
      queryClient.invalidateQueries({ queryKey: ['balances'] })
      setConfirm(null)
    },
    onError: (e) => {
      toast.error(e.message)
      setConfirm(null)
    }
  })

  const saveCounts = useMutation({
    mutationFn: (lines) => api.post(`/api/v1/inventory/stock-counts/${id}/count`, { lines }),
    onSuccess: () => {
      toast.success('Counted quantities submitted.')
      queryClient.invalidateQueries({ queryKey: ['stock-counts'] })
    },
    onError: (e) => toast.error(e.message)
  })

  if (count.isLoading) return <TableSkeleton rows={6} cols={4} />
  if (count.isError) return <ErrorState error={count.error} onRetry={count.refetch} />
  const c = count.data
  const canCount = hasPermission(P.InventoryCount) && c.status === 'Draft' && !c.blindCount
  const linesWithCounts = c.lines.every((l) => l.countedQuantity != null)

  return (
    <div>
      <div className="mb-4">
        <Link to="/stock-counts" className="flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700 dark:text-slate-400">
          <ArrowLeft className="h-4 w-4" /> Stock counts
        </Link>
      </div>
      <PageHeader
        title={`Count ${c.number}`}
        description={`${c.warehouseName}${c.blindCount ? ' · blind count' : ''}`}
        actions={
          <>
            {c.status === 'Draft' && c.blindCount && hasPermission(P.InventoryCount) && (
              <button className="btn-primary" onClick={() => setConfirm('submit')} disabled={!linesWithCounts}>
                <Send className="h-4 w-4" /> Submit for review
              </button>
            )}
            {c.status === 'Draft' && !c.blindCount && hasPermission(P.InventoryCount) && (
              <button className="btn-secondary" onClick={() => saveCounts.mutate(Object.entries(counts).map(([pid, v]) => ({ productId: pid, countedQuantity: Number(v), reason: 'Cycle count' })))}
                disabled={Object.keys(counts).length === 0 || saveCounts.isPending}>
                <UploadCloud className="h-4 w-4" /> Save counted values
              </button>
            )}
            {c.status === 'Submitted' && hasPermission(P.InventoryCountApprove) && (
              <button className="btn-primary" onClick={() => setConfirm('approve')}><CheckCheck className="h-4 w-4" /> Approve</button>
            )}
            {c.status === 'Approved' && hasPermission(P.InventoryCountApprove) && (
              <button className="btn-primary" onClick={() => setConfirm('post')}>Post variances to ledger</button>
            )}
            {(c.status === 'Draft' || c.status === 'Submitted') && hasPermission(P.InventoryCount) && (
              <button className="btn-secondary text-red-600" onClick={() => setConfirm('cancel')}><XCircle className="h-4 w-4" /> Cancel</button>
            )}
          </>
        }
      />

      <div className="card">
        <div className="border-b border-slate-100 px-4 py-3 dark:border-slate-800">
          <div className="flex items-center justify-between">
            <h2 className="text-sm font-semibold">Lines</h2>
            <Badge color={statusColor[c.status]}>{c.status}</Badge>
          </div>
          {c.status === 'Draft' && c.blindCount && (
            <p className="mt-1 text-xs text-slate-400">Blind count: record counted quantities via the API counter view; expected values are hidden until posted.</p>
          )}
        </div>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[640px]">
            <thead><tr><th className="th">Product</th><th className="th text-right">Expected</th><th className="th text-right">Counted</th><th className="th text-right">Variance</th><th className="th">Reason</th></tr></thead>
            <tbody>
              {c.lines.map((l) => {
                const canEdit = canCount && l.countedQuantity == null
                const value = counts[l.productId] ?? (l.countedQuantity ?? '')
                return (
                  <tr key={`${l.productId}-${l.batchId ?? '0'}`} className="table-row">
                    <td className="td">{l.productName}<p className="text-xs text-slate-400">{l.sku}</p></td>
                    <td className="td text-right text-slate-500 tabular-nums">{l.expectedQuantity == null ? '•••' : formatQty(l.expectedQuantity)}</td>
                    <td className="td text-right">
                      {canEdit ? (
                        <input type="number" step="any" min="0" className="input ml-auto w-28 text-right" value={value}
                          onChange={(e) => setCounts((s) => ({ ...s, [l.productId]: e.target.value }))}
                          aria-label={`Counted quantity for ${l.productName}`} />
                      ) : (
                        <span className="tabular-nums">{l.countedQuantity != null ? formatQty(l.countedQuantity) : '—'}</span>
                      )}
                    </td>
                    <td className={`td text-right tabular-nums font-semibold ${
                      l.variance == null ? 'text-slate-400' : l.variance === 0 ? 'text-slate-500' : l.variance > 0 ? 'text-emerald-600' : 'text-red-600'
                    }`}>
                      {l.variance == null ? '—' : `${l.variance > 0 ? '+' : ''}${Number(l.variance)}`}
                    </td>
                    <td className="td text-slate-500">{l.reason || '—'}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      </div>

      <ConfirmDialog open={confirm === 'approve'} onClose={() => setConfirm(null)} title="Approve count?"
        message="The count will be approved and ready to post." confirmLabel="Approve" busy={action.isPending}
        onConfirm={() => action.mutate({ verb: 'approve' })} />
      <ConfirmDialog open={confirm === 'post'} onClose={() => setConfirm(null)} title="Post variances?"
        message="Approved variances will be posted as immutable ledger movements. This can only happen once." confirmLabel="Post variances"
        busy={action.isPending} onConfirm={() => action.mutate({ verb: 'post' })} />
      <ConfirmDialog open={confirm === 'cancel'} onClose={() => setConfirm(null)} title="Cancel count?"
        message="The count session will be cancelled. No stock changes occur." confirmLabel="Cancel count" danger busy={action.isPending}
        onConfirm={() => action.mutate({ verb: 'cancel' })} />
    </div>
  )
}
