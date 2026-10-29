import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Warehouse as WarehouseIcon, Plus, Pencil } from 'lucide-react'
import { api, hasPermission } from '../lib/api-client.js'
import { P } from '../lib/permissions.js'
import { Badge, EmptyState, ErrorState, Field, Modal, PageHeader, TableSkeleton, toast } from '../components/ui/index.jsx'

export default function WarehousesPage() {
  const queryClient = useQueryClient()
  const [modal, setModal] = useState(null)
  const [includeInactive, setIncludeInactive] = useState(true)

  const warehouses = useQuery({
    queryKey: ['warehouses', includeInactive],
    queryFn: () => api.get(`/api/v1/warehouses?includeInactive=${includeInactive}`)
  })
  const canManage = hasPermission(P.WarehousesManage)

  return (
    <div>
      <PageHeader
        title="Warehouses & branches"
        description="Locations where stock is stored. Stock balances are tracked per warehouse."
        actions={
          canManage && (
            <button className="btn-primary" onClick={() => setModal({ name: '', code: '', address: '', city: '', country: '', isActive: true })}>
              <Plus className="h-4 w-4" /> New warehouse
            </button>
          )
        }
      />
      <div className="card">
        <div className="flex items-center justify-between border-b border-slate-100 px-4 py-3 dark:border-slate-800">
          <label className="flex items-center gap-2 text-xs text-slate-500">
            <input type="checkbox" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} className="h-3.5 w-3.5" />
            Show inactive
          </label>
        </div>
        {warehouses.isLoading ? <TableSkeleton rows={4} cols={4} /> : warehouses.isError ? (
          <ErrorState error={warehouses.error} onRetry={warehouses.refetch} />
        ) : (warehouses.data || []).length === 0 ? (
          <EmptyState icon={WarehouseIcon} title="No warehouses" description="Create your first storage location." />
        ) : (
          <table className="w-full">
            <thead><tr><th className="th">Name</th><th className="th">Code</th><th className="th">Address</th><th className="th">Status</th><th className="th" /></tr></thead>
            <tbody>
              {(warehouses.data || []).map((w) => (
                <tr key={w.id} className="table-row">
                  <td className="td font-medium">{w.name}</td>
                  <td className="td"><Badge color="blue">{w.code}</Badge></td>
                  <td className="td text-slate-500">{[w.address, w.city, w.country].filter(Boolean).join(', ') || '—'}</td>
                  <td className="td"><Badge color={w.isActive ? 'green' : 'gray'}>{w.isActive ? 'Active' : 'Inactive'}</Badge></td>
                  <td className="td text-right">
                    {canManage && (
                      <button className="btn-ghost p-1.5" onClick={() => setModal(w)} aria-label={`Edit ${w.name}`}>
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

      {modal && (
        <WarehouseModal
          value={modal}
          onClose={() => setModal(null)}
          onSaved={() => {
            setModal(null)
            queryClient.invalidateQueries({ queryKey: ['warehouses'] })
          }}
        />
      )}
    </div>
  )
}

function WarehouseModal({ value, onClose, onSaved }) {
  const isEdit = !!value.id
  const [form, setForm] = useState({
    name: value.name || '', code: value.code || '', address: value.address || '',
    city: value.city || '', country: value.country || '', isActive: value.isActive !== false
  })
  const [busy, setBusy] = useState(false)

  const submit = async (e) => {
    e.preventDefault()
    setBusy(true)
    try {
      if (isEdit) await api.put(`/api/v1/warehouses/${value.id}`, form)
      else await api.post('/api/v1/warehouses', form)
      toast.success(isEdit ? 'Warehouse updated.' : 'Warehouse created.')
      onSaved()
    } catch (err) {
      toast.error(err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open onClose={onClose} title={isEdit ? 'Edit warehouse' : 'New warehouse'}
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn-primary" onClick={submit} disabled={busy}>{isEdit ? 'Save' : 'Create'}</button>
        </>
      }>
      <form onSubmit={submit} className="grid grid-cols-1 gap-4 sm:grid-cols-2" noValidate>
        <Field label="Name" required><input className="input" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} autoFocus /></Field>
        <Field label="Code" required hint="Short unique code, e.g. MAIN"><input className="input" value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} /></Field>
        <div className="sm:col-span-2"><Field label="Address"><input className="input" value={form.address} onChange={(e) => setForm({ ...form, address: e.target.value })} /></Field></div>
        <Field label="City"><input className="input" value={form.city} onChange={(e) => setForm({ ...form, city: e.target.value })} /></Field>
        <Field label="Country"><input className="input" value={form.country} onChange={(e) => setForm({ ...form, country: e.target.value })} /></Field>
        {isEdit && (
          <label className="flex items-center gap-2 text-sm sm:col-span-2">
            <input type="checkbox" className="h-4 w-4" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} />
            Active
          </label>
        )}
      </form>
    </Modal>
  )
}
