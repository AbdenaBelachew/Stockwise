import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Truck, Plus, Search, Archive, Pencil } from 'lucide-react'
import { api, hasPermission, qs } from '../lib/api-client.js'
import { P } from '../lib/permissions.js'
import { useAuth } from '../app/providers.jsx'
import { Badge, ConfirmDialog, Field, Modal, PageHeader, toast } from '../components/ui/index.jsx'
import { DataTable, TableToolbar } from '../components/data-display/DataTable.jsx'

export default function SuppliersPage() {
  const auth = useAuth()
  const queryClient = useQueryClient()
  const [params, setParams] = useSearchParamsState()
  const search = params.get('search') || ''
  const active = params.get('active') ?? ''
  const page = parseInt(params.get('page') || '1', 10)
  const pageSize = parseInt(params.get('pageSize') || '5', 10)
  const [modal, setModal] = useState(null)
  const [archiving, setArchiving] = useState(null)

  const query = useQuery({
    queryKey: ['suppliers', { search, active, page }],
    queryFn: () => api.get(`/api/v1/suppliers${qs({ search, active: active === '' ? undefined : active === 'true', page, pageSize })}`)
  })

  const setParam = (key, value) => {
    const next = new URLSearchParams(params)
    if (value !== '') next.set(key, value)
    else next.delete(key)
    if (key !== 'page') next.set('page', '1')
    setParams(next)
  }

  const archive = async () => {
    try {
      await api.post(`/api/v1/suppliers/${archiving.id}/archive`, {})
      toast.success('Supplier archived.')
      setArchiving(null)
      queryClient.invalidateQueries({ queryKey: ['suppliers'] })
    } catch (e) {
      toast.error(e.message)
    }
  }

  return (
    <div>
      <PageHeader
        title="Suppliers"
        description="Vendor records with payment terms and purchase history links."
        actions={hasPermission(P.SuppliersManage) && (
          <button className="btn-primary" onClick={() => setModal({ code: '', name: '', contactPerson: '', email: '', phone: '', address: '', country: '', taxId: '', paymentTermsDays: 30, currencyCode: auth.currency, notes: '' })}>
            <Plus className="h-4 w-4" /> New supplier
          </button>
        )}
      />
      <TableToolbar>
        <div className="relative flex-1">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input className="input pl-9" placeholder="Search suppliers…" value={search} onChange={(e) => setParam('search', e.target.value)} aria-label="Search suppliers" />
        </div>
        <select className="input sm:w-40" value={active} onChange={(e) => setParam('active', e.target.value)} aria-label="Filter by status">
          <option value="">All</option>
          <option value="true">Active</option>
          <option value="false">Archived</option>
        </select>
      </TableToolbar>
      <div className="mt-4" />
      <DataTable
        loading={query.isLoading}
        error={query.isError ? query.error : null}
        onRetry={query.refetch}
        rows={query.data?.items}
        empty={{ icon: Truck, title: 'No suppliers', description: 'Add a supplier to start purchasing.' }}
        minWidth="min-w-[760px]"
        page={query.data?.page}
        pageSize={query.data?.pageSize}
        totalCount={query.data?.totalCount}
        onPage={(p) => setParam('page', String(p))}
        onPageSize={(n) => setParam('pageSize', String(n))}
        columns={[
          {
            key: 'name', header: 'Supplier',
            render: (s) => (
              <div>
                <p className="font-medium">{s.name}</p>
                <p className="text-xs text-slate-400">{s.code}{s.country ? ` · ${s.country}` : ''}</p>
              </div>
            )
          },
          {
            key: 'contactPerson', header: 'Contact',
            render: (s) => (
              <div className="text-slate-500">
                {s.contactPerson || '—'}
                {s.email && <p className="text-xs text-slate-400">{s.email}</p>}
              </div>
            )
          },
          {
            key: 'paymentTermsDays', header: 'Terms',
            render: (s) => <span className="text-slate-500">{s.paymentTermsDays} days{s.currencyCode ? ` · ${s.currencyCode}` : ''}</span>
          },
          {
            key: 'isActive', header: 'Status',
            render: (s) => <Badge color={s.isActive ? 'green' : 'gray'}>{s.isActive ? 'Active' : 'Archived'}</Badge>
          },
          {
            key: 'actions', header: '', align: 'right',
            render: (s) => (
              <div className="flex justify-end gap-1">
                {hasPermission(P.SuppliersManage) && (
                  <>
                    <button className="btn-ghost p-1.5" onClick={() => setModal(s)} aria-label={`Edit ${s.name}`}><Pencil className="h-4 w-4" /></button>
                    {s.isActive && <button className="btn-ghost p-1.5 text-red-500" onClick={() => setArchiving(s)} aria-label={`Archive ${s.name}`}><Archive className="h-4 w-4" /></button>}
                  </>
                )}
              </div>
            )
          }
        ]}
      />

      {modal && (
        <SupplierModal value={modal} onClose={() => setModal(null)} onSaved={() => { setModal(null); queryClient.invalidateQueries({ queryKey: ['suppliers'] }) }} />
      )}
      <ConfirmDialog open={!!archiving} onClose={() => setArchiving(null)} title="Archive supplier?"
        message={`'${archiving?.name}' will be archived. Suppliers with purchase history are never deleted.`}
        confirmLabel="Archive" danger onConfirm={archive} />
    </div>
  )
}

function useSearchParamsState() {
  const navigate = useNavigate()
  const [params] = useState(() => new URLSearchParams(window.location.search))
  const setParams = (next) => navigate({ search: next.toString() }, { replace: true })
  return [params, setParams]
}

function SupplierModal({ value, onClose, onSaved }) {
  const isEdit = !!value.id
  const [form, setForm] = useState({ ...value })
  const [busy, setBusy] = useState(false)
  const set = (key) => (e) => setForm((f) => ({ ...f, [key]: e.target.value }))

  const submit = async (e) => {
    e.preventDefault()
    setBusy(true)
    try {
      if (isEdit) await api.put(`/api/v1/suppliers/${value.id}`, form)
      else await api.post('/api/v1/suppliers', form)
      toast.success(isEdit ? 'Supplier updated.' : 'Supplier created.')
      onSaved()
    } catch (err) {
      toast.error(err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal open onClose={onClose} title={isEdit ? `Edit ${value.name}` : 'New supplier'} size="lg"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn-primary" onClick={submit} disabled={busy}>{isEdit ? 'Save' : 'Create'}</button>
        </>
      }>
      <form onSubmit={submit} className="grid grid-cols-1 gap-4 sm:grid-cols-2" noValidate>
        <Field label="Code" required><input className="input" value={form.code} onChange={set('code')} autoFocus /></Field>
        <Field label="Name" required><input className="input" value={form.name} onChange={set('name')} /></Field>
        <Field label="Contact person"><input className="input" value={form.contactPerson} onChange={set('contactPerson')} /></Field>
        <Field label="Email"><input type="email" className="input" value={form.email} onChange={set('email')} /></Field>
        <Field label="Phone"><input className="input" value={form.phone} onChange={set('phone')} /></Field>
        <Field label="Tax / business ID"><input className="input" value={form.taxId} onChange={set('taxId')} /></Field>
        <Field label="Country"><input className="input" value={form.country} onChange={set('country')} /></Field>
        <Field label="Payment terms (days)"><input type="number" min="0" max="365" className="input" value={form.paymentTermsDays} onChange={set('paymentTermsDays')} /></Field>
        <Field label="Currency preference"><input className="input" maxLength={3} value={form.currencyCode} onChange={set('currencyCode')} /></Field>
        <div className="sm:col-span-2"><Field label="Address"><input className="input" value={form.address} onChange={set('address')} /></Field></div>
        <div className="sm:col-span-2"><Field label="Notes"><textarea className="input h-20" value={form.notes} onChange={set('notes')} /></Field></div>
      </form>
    </Modal>
  )
}
