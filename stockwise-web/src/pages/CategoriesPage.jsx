import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { FolderTree, Plus, Search, Trash2, Pencil, Ruler } from 'lucide-react'
import { api } from '../lib/api-client.js'
import { hasPermission } from '../lib/api-client.js'
import { P } from '../lib/permissions.js'
import { Badge, ConfirmDialog, EmptyState, ErrorState, Field, Modal, PageHeader, TableSkeleton, toast } from '../components/ui/index.jsx'

export default function CategoriesPage() {
  const queryClient = useQueryClient()
  const [includeInactive, setIncludeInactive] = useState(true)
  const [categoryModal, setCategoryModal] = useState(null) // { name, parentCategoryId, description, isActive } | null
  const [unitModal, setUnitModal] = useState(null)
  const [confirmDelete, setConfirmDelete] = useState(null)

  const categories = useQuery({
    queryKey: ['categories', includeInactive],
    queryFn: () => api.get(`/api/v1/categories?includeInactive=${includeInactive}`)
  })
  const units = useQuery({ queryKey: ['units'], queryFn: () => api.get('/api/v1/units') })

  const canManage = hasPermission(P.CatalogManage)

  const archiveCategory = useMutationSave(
    async (id) => {
      await api.post(`/api/v1/categories/${id}/archive`, {})
      toast.success('Category archived.')
      queryClient.invalidateQueries({ queryKey: ['categories'] })
    },
    () => setConfirmDelete(null)
  )

  return (
    <div>
      <PageHeader
        title="Categories & Units"
        description="Organize products with categories and define units of measure."
        actions={
          canManage && (
            <>
              <button className="btn-secondary" onClick={() => setUnitModal({ name: '', symbol: '', allowsFractional: false })}>
                <Ruler className="h-4 w-4" /> New unit
              </button>
              <button className="btn-primary" onClick={() => setCategoryModal({ name: '', parentCategoryId: '', description: '', isActive: true })}>
                <Plus className="h-4 w-4" /> New category
              </button>
            </>
          )
        }
      />

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-5">
        <div className="card lg:col-span-3">
          <div className="flex items-center justify-between border-b border-slate-100 px-4 py-3 dark:border-slate-800">
            <h2 className="flex items-center gap-2 text-sm font-semibold"><FolderTree className="h-4 w-4" /> Categories</h2>
            <label className="flex items-center gap-2 text-xs text-slate-500">
              <input type="checkbox" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} className="h-3.5 w-3.5" />
              Show inactive
            </label>
          </div>
          {categories.isLoading ? <TableSkeleton rows={5} cols={2} /> : categories.isError ? (
            <ErrorState error={categories.error} onRetry={categories.refetch} />
          ) : (categories.data || []).length === 0 ? (
            <EmptyState icon={FolderTree} title="No categories" description="Create a category to group products." />
          ) : (
            <table className="w-full">
              <thead><tr><th className="th">Name</th><th className="th">Parent</th><th className="th">Status</th><th className="th" /></tr></thead>
              <tbody>
                {(categories.data || []).map((c) => (
                  <tr key={c.id} className="table-row">
                    <td className="td font-medium">{c.name}</td>
                    <td className="td text-slate-500">{c.parentCategoryName || '—'}</td>
                    <td className="td"><Badge color={c.isActive ? 'green' : 'gray'}>{c.isActive ? 'Active' : 'Inactive'}</Badge></td>
                    <td className="td text-right">
                      {canManage && (
                        <div className="flex justify-end gap-1">
                          <button className="btn-ghost p-1.5" onClick={() => setCategoryModal(c)} aria-label={`Edit ${c.name}`}>
                            <Pencil className="h-4 w-4" />
                          </button>
                          {c.isActive && (
                            <button className="btn-ghost p-1.5 text-red-500" onClick={() => setConfirmDelete(c)} aria-label={`Archive ${c.name}`}>
                              <Trash2 className="h-4 w-4" />
                            </button>
                          )}
                        </div>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>

        <div className="card lg:col-span-2">
          <div className="border-b border-slate-100 px-4 py-3 dark:border-slate-800">
            <h2 className="flex items-center gap-2 text-sm font-semibold"><Ruler className="h-4 w-4" /> Units of measure</h2>
          </div>
          {units.isLoading ? <TableSkeleton rows={5} cols={2} /> : (
            <table className="w-full">
              <thead><tr><th className="th">Name</th><th className="th">Symbol</th><th className="th">Fractional</th><th className="th" /></tr></thead>
              <tbody>
                {(units.data || []).map((u) => (
                  <tr key={u.id} className="table-row">
                    <td className="td font-medium">{u.name}</td>
                    <td className="td text-slate-500">{u.symbol}</td>
                    <td className="td">{u.allowsFractional ? <Badge color="blue">Yes</Badge> : <Badge>No</Badge>}</td>
                    <td className="td text-right">
                      {canManage && (
                        <button className="btn-ghost p-1.5" onClick={() => setUnitModal(u)} aria-label={`Edit ${u.name}`}>
                          <Pencil className="h-4 w-4" />
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>

      {categoryModal && (
        <CategoryModal
          value={categoryModal}
          categories={categories.data || []}
          onClose={() => setCategoryModal(null)}
          onSaved={() => {
            setCategoryModal(null)
            queryClient.invalidateQueries({ queryKey: ['categories'] })
          }}
        />
      )}
      {unitModal && (
        <UnitModal
          value={unitModal}
          onClose={() => setUnitModal(null)}
          onSaved={() => {
            setUnitModal(null)
            queryClient.invalidateQueries({ queryKey: ['units'] })
          }}
        />
      )}
      <ConfirmDialog
        open={!!confirmDelete}
        onClose={() => setConfirmDelete(null)}
        title="Archive category?"
        message={`'${confirmDelete?.name}' will be marked inactive. Categories referenced by products are never deleted.`}
        confirmLabel="Archive"
        danger
        busy={archiveCategory.busy}
        onConfirm={() => archiveCategory.run(confirmDelete.id)}
      />
    </div>
  )
}

function useMutationSave(fn, after) {
  const [busy, setBusy] = useState(false)
  return {
    busy,
    run: async (...args) => {
      setBusy(true)
      try { await fn(...args); after?.() } catch (e) { toast.error(e.message) } finally { setBusy(false) }
    }
  }
}

function CategoryModal({ value, categories, onClose, onSaved }) {
  const isEdit = !!value.id
  const [form, setForm] = useState({
    name: value.name || '', parentCategoryId: value.parentCategoryId || '',
    description: value.description || '', isActive: value.isActive !== false
  })
  const [busy, setBusy] = useState(false)

  const submit = async (e) => {
    e.preventDefault()
    setBusy(true)
    try {
      const payload = { ...form, parentCategoryId: form.parentCategoryId || null }
      if (isEdit) await api.put(`/api/v1/categories/${value.id}`, payload)
      else await api.post('/api/v1/categories', payload)
      toast.success(isEdit ? 'Category updated.' : 'Category created.')
      onSaved()
    } catch (err) {
      toast.error(err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open onClose={onClose} title={isEdit ? 'Edit category' : 'New category'} size="sm"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn-primary" onClick={submit} disabled={busy}>{isEdit ? 'Save' : 'Create'}</button>
        </>
      }>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Field label="Name" required>
          <input className="input" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} autoFocus />
        </Field>
        <Field label="Parent category">
          <select className="input" value={form.parentCategoryId} onChange={(e) => setForm({ ...form, parentCategoryId: e.target.value })}>
            <option value="">None</option>
            {categories.filter((c) => c.id !== value.id).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </Field>
        <Field label="Description">
          <textarea className="input h-20" value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} />
        </Field>
        {isEdit && (
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" className="h-4 w-4" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />
            Active
          </label>
        )}
      </form>
    </Modal>
  )
}

function UnitModal({ value, onClose, onSaved }) {
  const isEdit = !!value.id
  const [form, setForm] = useState({ name: value.name || '', symbol: value.symbol || '', allowsFractional: !!value.allowsFractional })
  const [busy, setBusy] = useState(false)

  const submit = async (e) => {
    e.preventDefault()
    setBusy(true)
    try {
      if (isEdit) await api.put(`/api/v1/units/${value.id}`, form)
      else await api.post('/api/v1/units', form)
      toast.success(isEdit ? 'Unit updated.' : 'Unit created.')
      onSaved()
    } catch (err) {
      toast.error(err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open onClose={onClose} title={isEdit ? 'Edit unit' : 'New unit'} size="sm"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn-primary" onClick={submit} disabled={busy}>{isEdit ? 'Save' : 'Create'}</button>
        </>
      }>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Field label="Name" required hint="e.g. Piece, Box, Kilogram">
          <input className="input" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} autoFocus />
        </Field>
        <Field label="Symbol" required hint="Short code, e.g. pcs, kg">
          <input className="input" value={form.symbol} onChange={(e) => setForm({ ...form, symbol: e.target.value })} />
        </Field>
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" className="h-4 w-4" checked={form.allowsFractional}
            onChange={(e) => setForm({ ...form, allowsFractional: e.target.checked })} />
          Allow fractional quantities (e.g. 0.5 kg)
        </label>
      </form>
    </Modal>
  )
}
