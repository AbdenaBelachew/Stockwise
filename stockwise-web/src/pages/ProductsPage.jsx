import { useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Package, Plus, Search, Upload, Download, Archive, Eye, AlertTriangle } from 'lucide-react'
import { api, hasPermission, qs } from '../lib/api-client.js'
import { P } from '../lib/permissions.js'
import { useAuth } from '../app/providers.jsx'
import {
  Badge, ConfirmDialog, Field, Modal, PageHeader, toast
} from '../components/ui/index.jsx'
import { DataTable, TableToolbar } from '../components/data-display/DataTable.jsx'

export default function ProductsPage() {
  const auth = useAuth()
  const queryClient = useQueryClient()
  const [params, setParams] = useSearchParams()
  const search = params.get('search') || ''
  const category = params.get('categoryId') || ''
  const status = params.get('status') || 'Active'
  const stockStatus = params.get('stockStatus') || ''
  const page = parseInt(params.get('page') || '1', 10)
  const pageSize = parseInt(params.get('pageSize') || '5', 10)
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState(null)
  const [importOpen, setImportOpen] = useState(false)
  const [archiving, setArchiving] = useState(null)

  const { data: categories } = useQuery({
    queryKey: ['categories'],
    queryFn: () => api.get('/api/v1/categories')
  })

  const query = useQuery({
    queryKey: ['products', { search, category, status, stockStatus, page }],
    queryFn: () => api.get(`/api/v1/products${qs({ search, categoryId: category, status, stockStatus, page, pageSize })}`)
  })

  const setParam = (key, value) => {
    const next = new URLSearchParams(params)
    if (value) next.set(key, value)
    else next.delete(key)
    if (key !== 'page') next.set('page', '1')
    setParams(next)
  }

  const archive = useMutation({
    mutationFn: (id) => api.post(`/api/v1/products/${id}/archive`, {}),
    onSuccess: () => {
      toast.success('Product archived.')
      setArchiving(null)
      queryClient.invalidateQueries({ queryKey: ['products'] })
    },
    onError: (e) => toast.error(e.message)
  })

  const fileRef = useRef(null)

  return (
    <div>
      <PageHeader
        title="Products"
        description="Your product catalog with reorder points, costing and tracking options."
        actions={
          <>
            {hasPermission(P.ProductsCreate) && (
              <button className="btn-secondary" onClick={() => setImportOpen(true)}>
                <Upload className="h-4 w-4" /> Import CSV
              </button>
            )}
            <a className="btn-secondary" href={`/api/v1/products/export${qs({ status, search, categoryId: category })}`}>
              <Download className="h-4 w-4" /> Export
            </a>
            {hasPermission(P.ProductsCreate) && (
              <button className="btn-primary" onClick={() => setFormOpen(true)}>
                <Plus className="h-4 w-4" /> New product
              </button>
            )}
          </>
        }
      />

      <TableToolbar>
        <div className="relative flex-1">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            className="input pl-9"
            placeholder="Search by name, SKU or barcode…"
            value={search}
            onChange={(e) => setParam('search', e.target.value)}
            aria-label="Search products"
          />
        </div>
        <select className="input sm:w-44" value={category} onChange={(e) => setParam('categoryId', e.target.value)} aria-label="Filter by category">
          <option value="">All categories</option>
          {(categories || []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
        <select className="input sm:w-40" value={status} onChange={(e) => setParam('status', e.target.value)} aria-label="Filter by status">
          <option value="Active">Active</option>
          <option value="Archived">Archived</option>
          <option value="">All statuses</option>
        </select>
        <select className="input sm:w-40" value={stockStatus} onChange={(e) => setParam('stockStatus', e.target.value)} aria-label="Filter by stock status">
          <option value="">Any stock level</option>
          <option value="out">Out of stock</option>
          <option value="low">Low stock</option>
          <option value="in">In stock</option>
        </select>
      </TableToolbar>

      <div className="mt-4" />
      <DataTable
        loading={query.isLoading}
        error={query.isError ? query.error : null}
        onRetry={query.refetch}
        rows={query.data?.items}
        empty={{
          icon: Package,
          title: 'No products yet',
          description: 'Create your first product or import a CSV file to get started.',
          action: hasPermission(P.ProductsCreate) && (
            <div className="flex gap-2">
              <button className="btn-primary" onClick={() => setFormOpen(true)}><Plus className="h-4 w-4" /> New product</button>
              <button className="btn-secondary" onClick={() => setImportOpen(true)}><Upload className="h-4 w-4" /> Import</button>
            </div>
          )
        }}
        minWidth="min-w-[900px]"
        page={query.data?.page}
        pageSize={query.data?.pageSize}
        totalCount={query.data?.totalCount}
        onPage={(p) => setParam('page', String(p))}
        onPageSize={(n) => setParam('pageSize', String(n))}
        columns={[
          {
            key: 'name', header: 'Product',
            render: (p) => (
              <div>
                <Link to={`/products/${p.id}`} className="font-medium text-slate-800 hover:text-accent-600 dark:text-slate-200">
                  {p.name}
                </Link>
                <p className="text-xs text-slate-400">{p.sku}{p.barcode ? ` · ${p.barcode}` : ''}</p>
              </div>
            )
          },
          { key: 'categoryName', header: 'Category', render: (p) => <span className="text-slate-500">{p.categoryName || '—'}</span> },
          {
            key: 'onHandTotal', header: 'On hand', align: 'right',
            render: (p) => {
              const stockColor = p.onHandTotal <= 0 ? 'red' : p.onHandTotal <= p.reorderPoint ? 'amber' : 'green'
              return <Badge color={stockColor}>{Number(p.onHandTotal)} {p.unitSymbol}</Badge>
            }
          },
          {
            key: 'reorderPoint', header: 'Reorder point', align: 'right',
            render: (p) => <span className="tabular-nums text-slate-500">{Number(p.reorderPoint)}</span>
          },
          {
            key: 'costPrice', header: 'Cost', align: 'right',
            render: (p) => <span className="tabular-nums text-slate-500">{p.costPrice != null ? `${Number(p.costPrice).toFixed(2)} ${p.currencyCode}` : '—'}</span>
          },
          {
            key: 'status', header: 'Status',
            render: (p) => <Badge color={p.status === 'Active' ? 'green' : 'gray'}>{p.status}</Badge>
          },
          {
            key: 'tracking', header: 'Tracking',
            render: (p) => (
              <span className="text-xs text-slate-500">
                {[p.trackBatches && 'Batch', p.trackExpiry && 'Expiry'].filter(Boolean).join(' · ') || '—'}
              </span>
            )
          },
          {
            key: 'actions', header: '', align: 'right',
            render: (p) => (
              <div className="flex justify-end gap-1">
                <Link to={`/products/${p.id}`} className="btn-ghost p-1.5" title="View details"><Eye className="h-4 w-4" /></Link>
                {p.status === 'Active' && hasPermission(P.ProductsArchive) && (
                  <button className="btn-ghost p-1.5 text-red-500" title="Archive" onClick={() => setArchiving(p)}>
                    <Archive className="h-4 w-4" />
                  </button>
                )}
              </div>
            )
          }
        ]}
      />

      <ProductFormModal
        open={formOpen}
        onClose={() => setFormOpen(false)}
        categories={categories}
      />

      <ImportModal open={importOpen} onClose={() => setImportOpen(false)} />

      <ConfirmDialog
        open={!!archiving}
        onClose={() => setArchiving(null)}
        title="Archive product?"
        message={`Products with inventory history cannot be deleted. '${archiving?.name}' will be archived and hidden from daily workflows.`}
        confirmLabel="Archive"
        danger
        busy={archive.isPending}
        onConfirm={() => archive.mutate(archiving.id)}
      />
    </div>
  )
}

function ProductFormModal({ open, onClose, categories }) {
  const queryClient = useQueryClient()
  const [form, setForm] = useState(defaultForm())
  const [errors, setErrors] = useState({})
  const [saving, setSaving] = useState(false)

  const { data: units } = useQuery({
    queryKey: ['units'],
    queryFn: () => api.get('/api/v1/units'),
    enabled: open
  })
  const { data: suppliers } = useQuery({
    queryKey: ['suppliers', 'all'],
    queryFn: () => api.get('/api/v1/suppliers?pageSize=200'),
    enabled: open
  })

  const set = (key) => (e) => {
    const value = e?.target ? (e.target.type === 'checkbox' ? e.target.checked : e.target.value) : e
    setForm((f) => ({ ...f, [key]: value }))
  }

  const submit = async (e) => {
    e.preventDefault()
    setErrors({})
    setSaving(true)
    try {
      await api.post('/api/v1/products', {
        ...form,
        costPrice: form.costPrice === '' ? null : Number(form.costPrice),
        salePrice: form.salePrice === '' ? null : Number(form.salePrice),
        reorderPoint: Number(form.reorderPoint || 0),
        targetStockLevel: form.targetStockLevel === '' ? null : Number(form.targetStockLevel),
        categoryId: form.categoryId || null,
        defaultSupplierId: form.defaultSupplierId || null,
        minShelfLifeDays: form.minShelfLifeDays === '' ? null : Number(form.minShelfLifeDays)
      })
      toast.success('Product created.')
      queryClient.invalidateQueries({ queryKey: ['products'] })
      setForm(defaultForm())
      onClose()
    } catch (err) {
      if (err.errors) setErrors(Object.fromEntries(Object.entries(err.errors).map(([k, v]) => [k, v[0]])))
      else toast.error(err.message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="New product"
      size="lg"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={saving}>Cancel</button>
          <button className="btn-primary" onClick={submit} disabled={saving}>Create product</button>
        </>
      }
    >
      <form onSubmit={submit} className="grid grid-cols-1 gap-4 sm:grid-cols-2" noValidate>
        <Field label="SKU" required error={errors.sku}>
          <input className="input" value={form.sku} onChange={set('sku')} />
        </Field>
        <Field label="Barcode">
          <input className="input" value={form.barcode} onChange={set('barcode')} />
        </Field>
        <div className="sm:col-span-2">
          <Field label="Name" required error={errors.name}>
            <input className="input" value={form.name} onChange={set('name')} />
          </Field>
        </div>
        <Field label="Category">
          <select className="input" value={form.categoryId} onChange={set('categoryId')}>
            <option value="">None</option>
            {(categories || []).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </Field>
        <Field label="Unit of measure" required error={errors.unitId}>
          <select className="input" value={form.unitId} onChange={set('unitId')}>
            <option value="">Select unit…</option>
            {(units || []).map((u) => <option key={u.id} value={u.id}>{u.name} ({u.symbol})</option>)}
          </select>
        </Field>
        <Field label="Cost price" error={errors.costPrice}>
          <input type="number" step="0.0001" min="0" className="input" value={form.costPrice} onChange={set('costPrice')} />
        </Field>
        <Field label="Sale price" error={errors.salePrice}>
          <input type="number" step="0.0001" min="0" className="input" value={form.salePrice} onChange={set('salePrice')} />
        </Field>
        <Field label="Reorder point" required error={errors.reorderPoint}>
          <input type="number" step="any" min="0" className="input" value={form.reorderPoint} onChange={set('reorderPoint')} />
        </Field>
        <Field label="Target stock level">
          <input type="number" step="any" min="0" className="input" value={form.targetStockLevel} onChange={set('targetStockLevel')} />
        </Field>
        <Field label="Default supplier">
          <select className="input" value={form.defaultSupplierId} onChange={set('defaultSupplierId')}>
            <option value="">None</option>
            {(suppliers?.items || []).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </select>
        </Field>
        <div className="flex flex-col justify-center gap-2 sm:col-span-1">
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
          <Field label="Min. remaining shelf life (days)" hint="Reject receiving batches with less remaining life.">
            <input type="number" min="0" className="input" value={form.minShelfLifeDays} onChange={set('minShelfLifeDays')} />
          </Field>
        )}
      </form>
    </Modal>
  )
}

function defaultForm() {
  return {
    sku: '', barcode: '', name: '', categoryId: '', unitId: '', costPrice: '', salePrice: '',
    reorderPoint: '', targetStockLevel: '', defaultSupplierId: '', trackBatches: false,
    trackExpiry: false, minShelfLifeDays: ''
  }
}

function ImportModal({ open, onClose }) {
  const queryClient = useQueryClient()
  const fileRef = useRef(null)
  const [preview, setPreview] = useState(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(null)

  const doPreview = async () => {
    setError(null)
    setPreview(null)
    const file = fileRef.current?.files?.[0]
    if (!file) return setError('Choose a CSV file first.')
    setBusy(true)
    try {
      const fd = new FormData()
      fd.append('file', file)
      setPreview(await api.postForm('/api/v1/products/import/preview', fd))
    } catch (e) {
      setError(e.message)
    } finally {
      setBusy(false)
    }
  }

  const doCommit = async () => {
    const file = fileRef.current?.files?.[0]
    if (!file) return
    setBusy(true)
    try {
      const fd = new FormData()
      fd.append('file', file)
      const res = await api.postForm('/api/v1/products/import/commit', fd)
      toast.success(`Imported ${res.created} product(s).`)
      queryClient.invalidateQueries({ queryKey: ['products'] })
      onClose()
    } catch (e) {
      setError(e.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Import products from CSV"
      size="lg"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>Close</button>
          <button className="btn-secondary" onClick={doPreview} disabled={busy}>Validate & preview</button>
          {preview && preview.validRows > 0 && (
            <button className="btn-primary" onClick={doCommit} disabled={busy}>
              Import {preview.validRows} valid row(s)
            </button>
          )}
        </>
      }
    >
      <div className="space-y-4">
        <p className="text-sm text-slate-500">
          Required columns: <code className="rounded bg-slate-100 px-1 text-xs dark:bg-slate-800">SKU</code>,{' '}
          <code className="rounded bg-slate-100 px-1 text-xs dark:bg-slate-800">Name</code>. Optional:{' '}
          <code className="rounded bg-slate-100 px-1 text-xs dark:bg-slate-800">Barcode</code>, Category, Unit, CostPrice, SalePrice,
          ReorderPoint, TrackBatches, TrackExpiry. Units and categories must already exist; rows with unknown units are rejected.
        </p>
        <input ref={fileRef} type="file" accept=".csv" className="block w-full text-sm" onChange={doPreview} />
        {busy && <p className="text-sm text-slate-400">Processing…</p>}
        {error && <div className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700" role="alert">{error}</div>}
        {preview && (
          <div>
            <div className="mb-2 flex flex-wrap gap-3 text-sm">
              <span><strong>{preview.totalRows}</strong> total rows</span>
              <span className="text-emerald-600">{preview.validRows} valid</span>
              {preview.errorRows > 0 && (
                <span className="flex items-center gap-1 text-red-600">
                  <AlertTriangle className="h-4 w-4" /> {preview.errorRows} with errors
                </span>
              )}
            </div>
            <div className="max-h-56 overflow-y-auto rounded-md border border-slate-200 dark:border-slate-700">
              <table className="w-full">
                <thead>
                  <tr><th className="th">Row</th><th className="th">SKU</th><th className="th">Name</th><th className="th">Result</th></tr>
                </thead>
                <tbody>
                  {preview.rows.map((r) => (
                    <tr key={r.rowNumber} className="table-row">
                      <td className="td text-slate-400">{r.rowNumber}</td>
                      <td className="td">{r.sku}</td>
                      <td className="td">{r.name}</td>
                      <td className="td">
                        {r.error ? <span className="text-xs text-red-600">{r.error}</span> : <Badge color="green">OK</Badge>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {preview.errorRows > 0 && (
              <p className="mt-2 text-xs text-amber-600">Only valid rows are imported. Fix the errors, re-upload, then import again.</p>
            )}
          </div>
        )}
      </div>
    </Modal>
  )
}
