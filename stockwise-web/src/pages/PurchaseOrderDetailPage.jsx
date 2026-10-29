import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, Send, CheckCircle2, XCircle, Truck, Printer, PackageCheck, ShoppingCart } from 'lucide-react'
import { api, hasPermission } from '../lib/api-client.js'
import { P } from '../lib/permissions.js'
import { useAuth } from '../app/providers.jsx'
import { formatMoney, formatDateTime, formatDate, formatQty } from '../lib/formatters.js'
import { Badge, ConfirmDialog, ErrorState, Field, Modal, PageHeader, TableSkeleton, toast } from '../components/ui/index.jsx'

const statusColors = {
  Draft: 'gray', PendingApproval: 'amber', Approved: 'blue', Ordered: 'purple',
  PartiallyReceived: 'blue', Received: 'green', Rejected: 'red', Cancelled: 'gray'
}

export default function PurchaseOrderDetailPage() {
  const { id } = useParams()
  const auth = useAuth()
  const queryClient = useQueryClient()
  const [confirm, setConfirm] = useState(null) // submit | approve | reject | cancel | order
  const [rejectReason, setRejectReason] = useState('')
  const [receiveOpen, setReceiveOpen] = useState(false)

  const po = useQuery({
    queryKey: ['purchase-orders', id],
    queryFn: () => api.get(`/api/v1/purchase-orders/${id}`)
  })

  const action = useMutation({
    mutationFn: ({ verb, body }) => api.post(`/api/v1/purchase-orders/${id}/${verb}`, body || {}),
    onSuccess: (_, vars) => {
      const msgs = {
        submit: 'Submitted for approval.', approve: 'Purchase order approved.',
        reject: 'Purchase order rejected.', cancel: 'Purchase order cancelled.',
        order: 'Marked as ordered.'
      }
      toast.success(msgs[vars.verb] || 'Done.')
      queryClient.invalidateQueries({ queryKey: ['purchase-orders'] })
      setConfirm(null)
      setRejectReason('')
    },
    onError: (e) => {
      toast.error(e.message)
      setConfirm(null)
    }
  })

  if (po.isLoading) return <TableSkeleton rows={6} cols={5} />
  if (po.isError) return <ErrorState error={po.error} onRetry={po.refetch} />
  const p = po.data

  const status = p.status
  const canSubmit = status === 'Draft' && hasPermission(P.PurchaseOrdersSubmit)
  const canApprove = status === 'PendingApproval' && hasPermission(P.PurchaseOrdersApprove)
  const canCancel = ['Draft', 'PendingApproval', 'Approved', 'Ordered', 'PartiallyReceived'].includes(status) && hasPermission(P.PurchaseOrdersCancel)
  const canOrder = status === 'Approved' && hasPermission(P.PurchaseOrdersSubmit)
  const canReceive = ['Approved', 'Ordered', 'PartiallyReceived'].includes(status) && hasPermission(P.ReceiptsCreate)

  return (
    <div>
      <div className="mb-4">
        <Link to="/purchase-orders" className="flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700 dark:text-slate-400">
          <ArrowLeft className="h-4 w-4" /> Purchase orders
        </Link>
      </div>
      <PageHeader
        title={`Purchase order ${p.number}`}
        description={`${p.supplierName} · ${p.warehouseName}`}
        actions={
          <>
            <button className="btn-secondary" onClick={() => window.print()}><Printer className="h-4 w-4" /> Print</button>
            {canOrder && <button className="btn-secondary" onClick={() => setConfirm('order')}><Truck className="h-4 w-4" /> Mark ordered</button>}
            {canSubmit && <button className="btn-primary" onClick={() => setConfirm('submit')}><Send className="h-4 w-4" /> Submit for approval</button>}
            {canApprove && <button className="btn-primary" onClick={() => setConfirm('approve')}><CheckCircle2 className="h-4 w-4" /> Approve</button>}
            {canApprove && <button className="btn-danger" onClick={() => setConfirm('reject')}><XCircle className="h-4 w-4" /> Reject</button>}
            {canReceive && <button className="btn-primary" onClick={() => setReceiveOpen(true)}><PackageCheck className="h-4 w-4" /> Receive goods</button>}
            {canCancel && <button className="btn-secondary text-red-600" onClick={() => setConfirm('cancel')}><XCircle className="h-4 w-4" /> Cancel</button>}
          </>
        }
      />

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        <div className="card p-5">
          <h2 className="mb-4 text-sm font-semibold">Details</h2>
          <dl className="space-y-3 text-sm">
            <Row label="Status"><Badge color={statusColors[status]}>{status}</Badge></Row>
            <Row label="Supplier">{p.supplierName}</Row>
            <Row label="Receiving warehouse">{p.warehouseName}</Row>
            <Row label="Expected delivery">{p.expectedDeliveryDate ? formatDate(p.expectedDeliveryDate, auth.dateFormat) : '—'}</Row>
            <Row label="Created by">{p.createdByName}</Row>
            <Row label="Created">{formatDateTime(p.createdAt, auth.dateFormat)}</Row>
            <Row label="Currency">{p.currencyCode}</Row>
            <Row label="Notes">{p.notes || '—'}</Row>
          </dl>
          <div className="mt-4 border-t border-slate-100 pt-3 text-sm dark:border-slate-800">
            <div className="flex justify-between"><span className="text-slate-500">Subtotal</span><span className="tabular-nums">{formatMoney(p.subTotal, p.currencyCode)}</span></div>
            <div className="flex justify-between"><span className="text-slate-500">Tax</span><span className="tabular-nums">{formatMoney(p.taxTotal, p.currencyCode)}</span></div>
            <div className="mt-1 flex justify-between font-semibold"><span>Total</span><span className="tabular-nums">{formatMoney(p.total, p.currencyCode)}</span></div>
          </div>
        </div>

        <div className="card lg:col-span-2">
          <div className="border-b border-slate-100 px-4 py-3 dark:border-slate-800">
            <h2 className="text-sm font-semibold">Line items</h2>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[680px]">
              <thead>
                <tr><th className="th">Product</th><th className="th text-right">Ordered</th><th className="th text-right">Received</th>
                  <th className="th text-right">Unit cost</th><th className="th text-right">Tax</th><th className="th text-right">Line total</th><th className="th" /></tr>
              </thead>
              <tbody>
                {p.lines.map((l) => {
                  const outstanding = Number(l.orderedQuantity) - Number(l.receivedQuantity)
                  return (
                    <tr key={l.id} className="table-row">
                      <td className="td">{l.productName}<p className="text-xs text-slate-400">{l.sku}</p></td>
                      <td className="td text-right tabular-nums">{formatQty(l.orderedQuantity, l.unitSymbol)}</td>
                      <td className="td text-right tabular-nums">
                        {formatQty(l.receivedQuantity, l.unitSymbol)}
                        {outstanding > 0 && status !== 'Received' && <p className="text-xs text-amber-600">{formatQty(outstanding, l.unitSymbol)} outstanding</p>}
                      </td>
                      <td className="td text-right tabular-nums">{formatMoney(l.unitCost, p.currencyCode)}</td>
                      <td className="td text-right tabular-nums">{Number(l.taxPercent)}%</td>
                      <td className="td text-right tabular-nums">{formatMoney(l.lineTotal, p.currencyCode)}</td>
                      <td className="td text-right">
                        {outstanding > 0 && <Badge color={l.receivedQuantity > 0 ? 'blue' : 'gray'}>{l.receivedQuantity > 0 ? 'Partial' : 'Pending'}</Badge>}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>

          <div className="border-t border-slate-100 px-4 py-3 dark:border-slate-800">
            <h2 className="mb-2 text-sm font-semibold">Approval & receipt history</h2>
            <ol className="space-y-1.5">
              {(p.history || []).map((h, i) => (
                <li key={i} className="flex items-center gap-2 text-sm text-slate-600 dark:text-slate-300">
                  <Badge color={h.action === 'Approved' ? 'green' : h.action === 'Rejected' ? 'red' : 'gray'}>{h.action}</Badge>
                  <span className="font-medium">{h.actorName}</span>
                  <span className="text-xs text-slate-400">{formatDateTime(h.atUtc, auth.dateFormat)}</span>
                  {h.reason && <span className="text-xs text-slate-500">— {h.reason}</span>}
                </li>
              ))}
            </ol>
          </div>
        </div>
      </div>

      {receiveOpen && (
        <ReceiveModal
          po={p}
          onClose={() => setReceiveOpen(false)}
          onSaved={() => {
            setReceiveOpen(false)
            queryClient.invalidateQueries({ queryKey: ['purchase-orders'] })
          }}
        />
      )}

      <ConfirmDialog open={confirm === 'submit'} onClose={() => setConfirm(null)} title="Submit for approval?"
        message={`Order ${p.number} (${formatMoney(p.total, p.currencyCode)}) will be sent for approval.`}
        confirmLabel="Submit" busy={action.isPending} onConfirm={() => action.mutate({ verb: 'submit' })} />
      <ConfirmDialog open={confirm === 'approve'} onClose={() => setConfirm(null)} title="Approve purchase order?"
        message={`Order ${p.number} will be approved. You will then be able to mark it ordered and receive goods.`}
        confirmLabel="Approve" busy={action.isPending} onConfirm={() => action.mutate({ verb: 'approve' })} />
      <Modal open={confirm === 'reject'} onClose={() => setConfirm(null)} title="Reject purchase order" size="sm"
        footer={
          <>
            <button className="btn-secondary" onClick={() => setConfirm(null)}>Cancel</button>
            <button className="btn-danger" disabled={!rejectReason.trim()} onClick={() => action.mutate({ verb: 'reject', body: { reason: rejectReason } })}>Reject</button>
          </>
        }>
        <Field label="Reason (required, audited)" required>
          <textarea className="input h-24" value={rejectReason} onChange={(e) => setRejectReason(e.target.value)} autoFocus />
        </Field>
      </Modal>
      <ConfirmDialog open={confirm === 'cancel'} onClose={() => setConfirm(null)} title="Cancel purchase order?"
        message="Cancelled orders cannot receive goods. No stock changes occur." confirmLabel="Cancel order" danger
        busy={action.isPending} onConfirm={() => action.mutate({ verb: 'cancel' })} />
      <ConfirmDialog open={confirm === 'order'} onClose={() => setConfirm(null)} title="Mark as ordered?"
        message="Records that the approved order was sent to the supplier. Stock is not affected until goods are received." confirmLabel="Mark ordered"
        busy={action.isPending} onConfirm={() => action.mutate({ verb: 'order' })} />
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

function ReceiveModal({ po, onClose, onSaved }) {
  const [lines, setLines] = useState(() => po.lines.map((l) => ({
    purchaseOrderLineId: l.id, productId: l.productId,
    outstanding: Number(l.orderedQuantity) - Number(l.receivedQuantity),
    productName: l.productName, sku: l.sku, unitSymbol: l.unitSymbol,
    trackBatchesHint: null,
    quantityAccepted: '', quantityRejected: '0', rejectReason: '',
    batchNumber: '', expiryDate: ''
  })))
  const [warehouseId, setWarehouseId] = useState('')
  const [notes, setNotes] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)

  const { data: warehouses } = useQuery({ queryKey: ['warehouses'], queryFn: () => api.get('/api/v1/warehouses') })
  const { data: products } = useQuery({
    queryKey: ['products', 'receive-detail'],
    queryFn: () => api.get('/api/v1/products?pageSize=200'),
    enabled: !!po
  })
  const setLine = (i, key, value) => setLines((ls) => ls.map((l, idx) => (idx === i ? { ...l, [key]: value } : l)))
  const productMap = Object.fromEntries((products?.items || []).map((p) => [p.id, p]))

  const submit = async (e) => {
    e.preventDefault()
    setError(null)
    const effectiveLines = lines.filter((l) => Number(l.quantityAccepted) > 0 || Number(l.quantityRejected) > 0)
    if (effectiveLines.length === 0) return setError('Record at least one accepted or rejected quantity.')
    for (const l of effectiveLines) {
      const product = productMap[l.productId]
      if (Number(l.quantityAccepted) > l.outstanding) return setError(`'${l.productName}': receiving more than the outstanding ${l.outstanding} is not allowed.`)
      if (product?.trackBatches && Number(l.quantityAccepted) > 0 && !l.batchNumber) return setError(`'${l.productName}': batch/lot number is required.`)
      if (product?.trackExpiry && Number(l.quantityAccepted) > 0 && !l.expiryDate) return setError(`'${l.productName}': expiry date is required.`)
      if (Number(l.quantityRejected) > 0 && !l.rejectReason?.trim()) return setError(`'${l.productName}': a rejection reason is required.`)
    }
    setBusy(true)
    try {
      const res = await api.post(`/api/v1/purchase-orders/${po.id}/receipts`, {
        purchaseOrderId: po.id,
        warehouseId: warehouseId || undefined,
        notes,
        idempotencyKey: crypto.randomUUID(),
        lines: effectiveLines.map((l) => ({
          purchaseOrderLineId: l.purchaseOrderLineId,
          quantityAccepted: Number(l.quantityAccepted || 0),
          quantityRejected: Number(l.quantityRejected || 0),
          rejectReason: l.rejectReason || null,
          batchNumber: l.batchNumber || null,
          expiryDate: l.expiryDate ? new Date(l.expiryDate).toISOString() : null
        }))
      })
      toast.success(`Goods receipt ${res.number} posted: ${res.purchaseOrderStatus}.`)
      onSaved()
    } catch (err) {
      setError(err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open onClose={onClose} title={`Receive goods — ${po.number}`} size="xl"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn-primary" onClick={submit} disabled={busy}>Post receipt</button>
        </>
      }>
      {error && <div className="mb-3 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700" role="alert">{error}</div>}
      <div className="space-y-3">
        <Field label="Receiving warehouse" hint="Defaults to the order's warehouse.">
          <select className="input sm:w-72" value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)}>
            <option value="">{po.warehouseName} (order default)</option>
            {(warehouses || []).map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
          </select>
        </Field>

        {lines.map((l, i) => {
          const product = productMap[l.productId]
          const batched = product?.trackBatches
          return (
            <div key={l.purchaseOrderLineId} className="grid grid-cols-2 gap-3 rounded-lg border border-slate-200 p-3 dark:border-slate-700 sm:grid-cols-12">
              <div className="sm:col-span-3">
                <p className="text-sm font-medium">{l.productName}</p>
                <p className="text-xs text-slate-400">{l.sku} · {l.outstanding} {l.unitSymbol} outstanding{batched ? ' · batched' : ''}</p>
              </div>
              <div className="sm:col-span-2">
                <Field label="Accepted"><input type="number" step="any" min="0" max={l.outstanding} className="input" value={l.quantityAccepted} onChange={(e) => setLine(i, 'quantityAccepted', e.target.value)} /></Field>
              </div>
              <div className="sm:col-span-2">
                <Field label="Rejected"><input type="number" step="any" min="0" className="input" value={l.quantityRejected} onChange={(e) => setLine(i, 'quantityRejected', e.target.value)} /></Field>
              </div>
              {batched && (
                <>
                  <div className="sm:col-span-2">
                    <Field label="Batch/lot"><input className="input" value={l.batchNumber} onChange={(e) => setLine(i, 'batchNumber', e.target.value)} /></Field>
                  </div>
                  <div className="sm:col-span-2">
                    <Field label="Expiry date"><input type="date" className="input" value={l.expiryDate} onChange={(e) => setLine(i, 'expiryDate', e.target.value)} /></Field>
                  </div>
                </>
              )}
              {Number(l.quantityRejected) > 0 && (
                <div className="col-span-2 sm:col-span-2">
                  <Field label="Reject reason"><input className="input" value={l.rejectReason} onChange={(e) => setLine(i, 'rejectReason', e.target.value)} /></Field>
                </div>
              )}
            </div>
          )
        })}
        <Field label="Notes"><input className="input" value={notes} onChange={(e) => setNotes(e.target.value)} /></Field>
        <p className="text-xs text-slate-400">
          Receipt posts immutable ledger movements atomically. Batch-tracked products create batch records with expiry dates for FEFO picking.
        </p>
      </div>
    </Modal>
  )
}
