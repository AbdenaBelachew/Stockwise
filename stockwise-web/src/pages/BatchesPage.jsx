import { useState } from 'react'
import { useQuery, useQueryClient, useMutation } from '@tanstack/react-query'
import { Layers, ShieldAlert, ShieldCheck } from 'lucide-react'
import { api, hasPermission, qs } from '../lib/api-client.js'
import { P } from '../lib/permissions.js'
import { useAuth } from '../app/providers.jsx'
import { formatMoney, formatDate } from '../lib/formatters.js'
import { Badge, ConfirmDialog, EmptyState, PageHeader, StatCard, toast } from '../components/ui/index.jsx'
import { DataTable, TableToolbar } from '../components/data-display/DataTable.jsx'

export default function BatchesPage() {
  const auth = useAuth()
  const queryClient = useQueryClient()
  const [warehouseId, setWarehouseId] = useState('')
  const [bucket, setBucket] = useState('all')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(5)
  const [quarantineBatch, setQuarantineBatch] = useState(null)
  const [releaseBatch, setReleaseBatch] = useState(null)

  // Reset to the first page whenever the filters change.
  useEffect(() => setPage(1), [warehouseId, bucket])

  const { data: warehouses } = useQuery({ queryKey: ['warehouses'], queryFn: () => api.get('/api/v1/warehouses') })
  const query = useQuery({
    queryKey: ['expiry', { warehouseId, bucket }],
    queryFn: () => api.get(`/api/v1/inventory/expiry${qs({ warehouseId, includeExpired: true })}`),
    enabled: hasPermission(P.InventoryRead)
  })

  const quarantine = useMutation({
    mutationFn: (id) => api.post(`/api/v1/inventory/batches/${id}/quarantine`, {}),
    onSuccess: () => {
      toast.success('Batch quarantined.')
      setQuarantineBatch(null)
      queryClient.invalidateQueries({ queryKey: ['expiry'] })
    },
    onError: (e) => toast.error(e.message)
  })
  const release = useMutation({
    mutationFn: (id) => api.post(`/api/v1/inventory/batches/${id}/release`, {}),
    onSuccess: () => {
      toast.success('Batch released.')
      setReleaseBatch(null)
      queryClient.invalidateQueries({ queryKey: ['expiry'] })
    },
    onError: (e) => toast.error(e.message)
  })

  const rows = (query.data || []).filter((b) => bucket === 'all' || b.bucket === bucket)
  const pagedRows = rows.slice((page - 1) * pageSize, page * pageSize)
  const counts = { expired: 0, critical: 0, warning: 0, ok: 0 }
  for (const b of query.data || []) counts[b.bucket] = (counts[b.bucket] || 0) + 1

  const bucketColor = { expired: 'red', critical: 'red', warning: 'amber', ok: 'green' }
  const statusColor = { Active: 'green', Quarantined: 'purple', Expired: 'red', Depleted: 'gray' }

  return (
    <div>
      <PageHeader
        title="Batches & expiry"
        description="Expiry policy: a batch is valid through its expiry date and counts as expired from the following day, in your organization's time zone."
      />

      <div className="mb-6 grid grid-cols-2 gap-4 lg:grid-cols-4">
        <StatCard title="Expired" value={counts.expired} tone="danger" icon={ShieldAlert} onClick={() => setBucket('expired')} />
        <StatCard title="≤ 7 days" value={counts.critical} tone="danger" onClick={() => setBucket('critical')} />
        <StatCard title={`Warning window`} value={counts.warning} tone="warning" onClick={() => setBucket('warning')} />
        <StatCard title="Healthy" value={counts.ok} tone="success" onClick={() => setBucket('ok')} />
      </div>

      <TableToolbar>
        <select className="input sm:w-52" value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} aria-label="Filter by warehouse">
          <option value="">All warehouses</option>
          {(warehouses || []).map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
        </select>
        <select className="input sm:w-44" value={bucket} onChange={(e) => setBucket(e.target.value)} aria-label="Filter by expiry bucket">
          <option value="all">All batches</option>
          <option value="expired">Expired</option>
          <option value="critical">≤ 7 days</option>
          <option value="warning">Warning</option>
          <option value="ok">OK</option>
        </select>
      </TableToolbar>

      <div className="mt-4" />
      <DataTable
        loading={query.isLoading}
        error={query.isError ? query.error : null}
        onRetry={query.refetch}
        rows={pagedRows}
        keyField="batchId"
        empty={{ icon: Layers, title: 'No batches to show', description: 'Batch data appears when you receive batch-tracked products.' }}
        minWidth="min-w-[900px]"
        page={page}
        pageSize={pageSize}
        totalCount={rows.length}
        onPage={setPage}
        onPageSize={(n) => { setPageSize(n); setPage(1) }}
        columns={[
          {
            key: 'productName', header: 'Product',
            render: (b) => (
              <div>
                <p className="max-w-[200px] truncate font-medium">{b.productName}</p>
                <p className="text-xs text-slate-400">{b.sku}</p>
              </div>
            )
          },
          { key: 'batchNumber', header: 'Batch', render: (b) => <span className="font-mono text-xs">{b.batchNumber}</span> },
          { key: 'warehouseName', header: 'Warehouse', render: (b) => <span className="text-slate-500">{b.warehouseName}</span> },
          {
            key: 'quantityRemaining', header: 'Qty remaining', align: 'right',
            render: (b) => <span className="tabular-nums">{Number(b.quantityRemaining)}</span>
          },
          {
            key: 'expiryDate', header: 'Expiry date',
            render: (b) => (
              <div className="whitespace-nowrap">
                <span className={b.bucket === 'expired' ? 'font-semibold text-red-600' : ''}>{formatDate(b.expiryDate, auth.dateFormat)}</span>
                <p className="text-xs text-slate-400">{b.daysToExpiry < 0 ? `${-b.daysToExpiry}d ago` : `${b.daysToExpiry}d left`}</p>
              </div>
            )
          },
          {
            key: 'totalValue', header: 'Value', align: 'right',
            render: (b) => <span className="tabular-nums">{formatMoney(b.totalValue, auth.currency)}</span>
          },
          {
            key: 'status', header: 'Status',
            render: (b) => (
              <div className="flex flex-col items-start gap-1">
                <Badge color={bucketColor[b.bucket]}>{b.bucket === 'critical' ? '≤7 days' : b.bucket}</Badge>
                {b.status !== 'Active' && <Badge color={statusColor[b.status]}>{b.status}</Badge>}
              </div>
            )
          },
          {
            key: 'actions', header: '', align: 'right',
            render: (b) => hasPermission(P.BatchesManage) && (
              b.status === 'Active' ? (
                <button className="btn-ghost p-1.5 text-violet-600" title="Quarantine batch" onClick={() => setQuarantineBatch(b)}>
                  <ShieldAlert className="h-4 w-4" />
                </button>
              ) : b.status === 'Quarantined' ? (
                <button className="btn-ghost p-1.5 text-emerald-600" title="Release batch" onClick={() => setReleaseBatch(b)}>
                  <ShieldCheck className="h-4 w-4" />
                </button>
              ) : null
            )
          }
        ]}
      />

            <ConfirmDialog
        open={!!quarantineBatch} onClose={() => setQuarantineBatch(null)}
        title="Quarantine batch?"
        message={`Batch '${quarantineBatch?.batchNumber}' will be quarantined and excluded from FEFO allocation until released.`}
        confirmLabel="Quarantine" busy={quarantine.isPending}
        onConfirm={() => quarantine.mutate(quarantineBatch.batchId)}
      />
      <ConfirmDialog
        open={!!releaseBatch} onClose={() => setReleaseBatch(null)}
        title="Release batch?"
        message={`Batch '${releaseBatch?.batchNumber}' will be marked active again.`}
        confirmLabel="Release" busy={release.isPending}
        onConfirm={() => release.mutate(releaseBatch.batchId)}
      />
    </div>
  )
}
