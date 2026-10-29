import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useQuery, useQueryClient, useMutation } from '@tanstack/react-query'
import { ArrowLeft, Archive, RotateCcw, Pencil, TrendingUp, Layers } from 'lucide-react'
import { api, hasPermission } from '../lib/api-client.js'
import { P } from '../lib/permissions.js'
import { useAuth } from '../app/providers.jsx'
import { formatMoney, formatQty, formatDateTime } from '../lib/formatters.js'
import {
  Badge, ConfirmDialog, ErrorState, Field, Modal, PageHeader, Skeleton, TableSkeleton, toast
} from '../components/ui/index.jsx'

export default function ProductDetailPage() {
  const { id } = useParams()
  const auth = useAuth()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [editOpen, setEditOpen] = useState(false)
  const [archiveOpen, setArchiveOpen] = useState(false)

  const product = useQuery({
    queryKey: ['products', id],
    queryFn: () => api.get(`/api/v1/products/${id}`)
  })
  const movements = useQuery({
    queryKey: ['products', id, 'movements'],
    queryFn: () => api.get(`/api/v1/products/${id}/movements?limit=50`)
  })

  const archive = useMutation({
    mutationFn: () => api.post(`/api/v1/products/${id}/archive`, {}),
    onSuccess: () => {
      toast.success('Product archived.')
      queryClient.invalidateQueries({ queryKey: ['products'] })
      setArchiveOpen(false)
      navigate('/products')
    },
    onError: (e) => alert(e.message)
  })
  const restore = useMutation({
    mutationFn: () => api.post(`/api/v1/products/${id}/restore`, {}),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['products', id] }),
    onError: (e) => alert(e.message)
  })

  if (product.isLoading) return <TableSkeleton rows={6} cols={4} />
  if (product.isError) return <ErrorState error={product.error} onRetry={product.refetch} />
  const p = product.data

  return (
    <div>
      <div className="mb-4">
        <Link to="/products" className="flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700 dark:text-slate-400">
          <ArrowLeft className="h-4 w-4" /> Products
        </Link>
      </div>
      <PageHeader
        title={p.name}
        description={`${p.sku}${p.barcode ? ` · Barcode ${p.barcode}` : ''}`}
        actions={
          <>
            {p.status === 'Archived' && hasPermission(P.ProductsArchive) && (
              <button className="btn-secondary" onClick={() => restore.mutate()}><RotateCcw className="h-4 w-4" /> Restore</button>
            )}
            {hasPermission(P.ProductsArchive) && p.status === 'Active' && (
              <button className="btn-secondary text-red-600" onClick={() => setArchiveOpen(true)}><Archive className="h-4 w-4" /> Archive</button>
            )}
            {hasPermission(P.ProductsUpdate) && p.status === 'Active' && (
              <button className="btn-primary" onClick={() => setEditOpen(true)}><Pencil className="h-4 w-4" /> Edit</button>
            )}
          </>
        }
      />

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        <div className="card p-5 lg:col-span-1">
          <h2 className="mb-4 text-sm font-semibold text-slate-700 dark:text-slate-200">Details</h2>
          <dl className="space-y-3 text-sm">
            <Row label="Status"><Badge color={p.status === 'Active' ? 'green' : 'gray'}>{p.status}</Badge></Row>
            <Row label="On hand"><span className="font-semibold">{formatQty(p.onHandTotal, p.unitSymbol)}</span></Row>
            <Row label="Stock value">{p.onHandTotal && p.costPrice ? formatMoney(Number(p.onHandTotal) * Number(p.costPrice), auth.currency) : '—'}</Row>
            <Row label="Category">{p.categoryName || '—'}</Row>
            <Row label="Unit">{p.unitName} ({p.unitSymbol})</Row>
            <Row label="Cost price">{p.costPrice != null ? formatMoney(p.costPrice, p.currencyCode) : '—'}</Row>
            <Row label="Sale price">{p.salePrice != null ? formatMoney(p.salePrice, p.currencyCode) : '—'}</Row>
            <Row label="Cost method">{p.costMethod}</Row>
            <Row label="Reorder point">{formatQty(p.reorderPoint)}</Row>
            <Row label="Target stock">{p.targetStockLevel != null ? formatQty(p.targetStockLevel) : '—'}</Row>
            <Row label="Batch tracking">{p.trackBatches ? 'Enabled' : 'Disabled'}</Row>
            <Row label="Expiry tracking">{p.trackExpiry ? 'Enabled' : 'Disabled'}</Row>
            {p.minShelfLifeDays != null && <Row label="Min. shelf life">{p.minShelfLifeDays} days</Row>}
            <Row label="Created">{formatDateTime(p.createdAt, auth.dateFormat)}</Row>
          </dl>
          <div className="mt-5 flex gap-2">
            <Link to={`/movements?productId=${p.id}`} className="btn-secondary flex-1 justify-center">
              <TrendingUp className="h-4 w-4" /> Movement history
            </Link>
          </div>
        </div>

        <div className="card lg:col-span-2">
          <div className="border-b border-slate-100 px-4 py-3 dark:border-slate-800">
            <h2 className="flex items-center gap-2 text-sm font-semibold text-slate-700 dark:text-slate-200">
              <Layers className="h-4 w-4" /> Recent movements
            </h2>
          </div>
          {movements.isLoading ? <TableSkeleton rows={6} cols={4} /> : (movements.data || []).length === 0 ? (
            <p className="px-4 py-10 text-center text-sm text-slate-400">
              No movements recorded. Post an opening balance to add stock.
            </p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full">
                <thead>
                  <tr>
                    <th className="th">Date</th><th className="th">Type</th><th className="th text-right">Qty</th>
                    <th className="th">Warehouse</th><th className="th">Batch</th><th className="th">Reference</th>
                    <th className="th">By</th>
                  </tr>
                </thead>
                <tbody>
                  {movements.data.map((m) => (
                    <tr key={m.id} className="table-row">
                      <td className="td whitespace-nowrap text-slate-500">{formatDateTime(m.createdAtUtc, auth.dateFormat)}</td>
                      <td className="td">{m.movementType.replace(/([A-Z])/g, ' $1').trim()}</td>
                      <td className={`td text-right font-semibold ${m.quantityDelta > 0 ? 'text-emerald-600' : 'text-red-600'}`}>
                        {m.quantityDelta > 0 ? '+' : ''}{Number(m.quantityDelta)}
                      </td>
                      <td className="td text-slate-500">{m.warehouseName}</td>
                      <td className="td text-slate-500">{m.batchNumber || '—'}</td>
                      <td className="td text-slate-500">{m.referenceNumber || m.referenceType || '—'}</td>
                      <td className="td text-slate-500">{m.userName}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>

      {editOpen && (
        <EditModal
          open={editOpen}
          onClose={() => setEditOpen(false)}
          product={p}
          onSaved={() => {
            setEditOpen(false)
            queryClient.invalidateQueries({ queryKey: ['products'] })
          }}
        />
      )}

      <ConfirmDialog
        open={archiveOpen}
        onClose={() => setArchiveOpen(false)}
        title="Archive product?"
        message={`'${p.name}' will be archived. Its history remains intact.`}
        confirmLabel="Archive"
        danger
        busy={archive.isPending}
        onConfirm={() => archive.mutate()}
      />
    </div>
  )
}

function Row({ label, children }) {
  return (
    <div className="flex items-center justify-between gap-4">
      <dt className="text-slate-500 dark:text-slate-400">{label}</dt>
      <dd className="text-right font-medium text-slate-700 dark:text-slate-200">{children}</dd>
    </div>
  )
}

function EditModal({ open, onClose, product, onSaved }) {
  const queryClient = useQueryClient()
  const [saving, setSaving] = useState(false)
  const [errors, setErrors] = useState({})
  const { data: units } = useQuery({ queryKey: ['units'], queryFn: () => api.get('/api/v1/units'), enabled: open })
  const { data: categories } = useQuery({ queryKey: ['categories'], queryFn: () => api.get('/api/v1/categories?includeInactive=true'), enabled: open })
  const { data: suppliers } = useQuery({ queryKey: ['suppliers', 'all'], queryFn: () => api.get('/api/v1/suppliers?pageSize=200'), enabled: open })

  const [form, setForm] = useState(() => ({
    sku: product.sku, barcode: product.barcode || '', name: product.name,
    categoryId: product.categoryId || '', unitId: product.unitId,
    costPrice: product.costPrice ?? '', salePrice: product.salePrice ?? '',
    reorderPoint: product.reorderPoint, targetStockLevel: product.targetStockLevel ?? '',
    defaultSupplierId: product.defaultSupplierId || '',
    trackBatches: product.trackBatches, trackExpiry: product.trackExpiry,
    minShelfLifeDays: product.minShelfLifeDays ?? ''
  }))

  const set = (key) => (e) => {
    const value = e?.target ? (e.target.type === 'checkbox' ? e.target.checked : e.target.value) : e
    setForm((f) => ({ ...f, [key]: value }))
  }

  const submit = async (e) => {
    e.preventDefault()
    setSaving(true)
    setErrors({})
    try {
      await api.put(`/api/v1/products/${product.id}`, {
        ...form,
        costPrice: form.costPrice === '' ? null : Number(form.costPrice),
        salePrice: form.salePrice === '' ? null : Number(form.salePrice),
        reorderPoint: Number(form.reorderPoint || 0),
        targetStockLevel: form.targetStockLevel === '' ? null : Number(form.targetStockLevel),
        categoryId: form.categoryId || null,
        defaultSupplierId: form.defaultSupplierId || null,
        minShelfLifeDays: form.minShelfLifeDays === '' ? null : Number(form.minShelfLifeDays)
      })
      toast.success('Product updated.')
      onSaved()
    } catch (err) {
      if (err.errors) setErrors(Object.fromEntries(Object.entries(err.errors).map(([k, v]) => [k, v[0]])))
      else setErrors({ _: err.message })
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open={open} onClose={onClose} title={`Edit ${product.name}`} size="lg"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={saving}>Cancel</button>
          <button className="btn-primary" onClick={submit} disabled={saving}>Save changes</button>
        </>
      }
    >
      {errors._ && <div className="mb-3 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700" role="alert">{errors._}</div>}
      <form onSubmit={submit} className="grid grid-cols-1 gap-4 sm:grid-cols-2" noValidate>
        <Field label="SKU" required error={errors.sku}><input className="input" value={form.sku} onChange={set('sku')} /></Field>
        <Field label="Barcode"><input className="input" value={form.barcode} onChange={set('barcode')} /></Field>
        <div className="sm:col-span-2">
          <Field label="Name" required error={errors.name}><input className="input" value={form.name} onChange={set('name')} /></Field>
        </div>
        <Field label="Category">
          <select className="input" value={form.categoryId} onChange={set('categoryId')}>
            <option value="">None</option>
            {(categories || []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </Field>
        <Field label="Unit of measure" required error={errors.unitId}>
          <select className="input" value={form.unitId} onChange={set('unitId')}>
            {(units || []).map((u) => <option key={u.id} value={u.id}>{u.name} ({u.symbol})</option>)}
          </select>
        </Field>
        <Field label="Cost price"><input type="number" step="0.0001" min="0" className="input" value={form.costPrice} onChange={set('costPrice')} /></Field>
        <Field label="Sale price"><input type="number" step="0.0001" min="0" className="input" value={form.salePrice} onChange={set('salePrice')} /></Field>
        <Field label="Reorder point" required><input type="number" step="any" min="0" className="input" value={form.reorderPoint} onChange={set('reorderPoint')} /></Field>
        <Field label="Target stock level"><input type="number" step="any" min="0" className="input" value={form.targetStockLevel} onChange={set('targetStockLevel')} /></Field>
        <Field label="Default supplier">
          <select className="input" value={form.defaultSupplierId} onChange={set('defaultSupplierId')}>
            <option value="">None</option>
            {(suppliers?.items || []).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
        </Field>
        <div className="flex flex-col justify-center gap-2">
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" className="h-4 w-4 rounded" checked={form.trackBatches} onChange={set('trackBatches')} />
            Track batches / lots
          </label>
          <label className={`flex items-center gap-2 text-sm ${!form.trackBatches ? 'opacity-50' : ''}`}>
            <input type="checkbox" className="h-4 w-4 rounded" disabled={!form.trackBatches} checked={form.trackExpiry} onChange={set('trackExpiry')} />
            Track expiry dates
          </label>
        </div>
        {form.trackExpiry && (
          <Field label="Min. remaining shelf life (days)">
            <input type="number" min="0" className="input" value={form.minShelfLifeDays} onChange={set('minShelfLifeDays')} />
          </Field>
        )}
      </form>
    </Modal>
  )
}
