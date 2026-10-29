import { useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { ClipboardList } from 'lucide-react'
import { api, hasPermission, qs } from '../lib/api-client.js'
import { P } from '../lib/permissions.js'
import { useAuth } from '../app/providers.jsx'
import { formatDateTime } from '../lib/formatters.js'
import { Badge, PageHeader } from '../components/ui/index.jsx'
import { DataTable, TableToolbar } from '../components/data-display/DataTable.jsx'

export default function MovementsPage() {
  const auth = useAuth()
  const [params, setParams] = useSearchParams()
  const productId = params.get('productId') || ''
  const warehouseId = params.get('warehouseId') || ''
  const movementType = params.get('movementType') || ''
  const search = params.get('search') || ''
  const page = parseInt(params.get('page') || '1', 10)
  const pageSize = parseInt(params.get('pageSize') || '5', 10)

  const { data: warehouses } = useQuery({ queryKey: ['warehouses'], queryFn: () => api.get('/api/v1/warehouses') })
  const product = useQuery({
    queryKey: ['products', productId],
    queryFn: () => api.get(`/api/v1/products/${productId}`),
    enabled: !!productId
  })
  const query = useQuery({
    queryKey: ['movements', { productId, warehouseId, movementType, search, page }],
    queryFn: () => api.get(`/api/v1/inventory/movements${qs({ productId, warehouseId, movementType, search, page, pageSize })}`),
    enabled: hasPermission(P.InventoryRead)
  })

  const setParam = (key, value) => {
    const next = new URLSearchParams(params)
    if (value) next.set(key, value)
    else next.delete(key)
    if (key !== 'page') next.set('page', '1')
    setParams(next)
  }

  const typeColor = (t) =>
    t === 'GoodsReceipt' || t === 'OpeningBalance' || t === 'TransferIn' || t === 'CustomerReturn' ? 'green'
      : t === 'Adjustment' || t === 'StockCountVariance' ? 'amber'
      : t === 'Reversal' ? 'purple' : 'red'

  return (
    <div>
      <PageHeader
        title="Stock movements"
        description="The append-only ledger of every inventory change. Posted movements are immutable; corrections are compensating entries."
      />
      {productId && product.data && (
        <div className="mb-4 flex items-center gap-2 rounded-md bg-slate-100 px-4 py-2.5 text-sm dark:bg-slate-800">
          Filtered to <strong>{product.data.name}</strong>
          <button className="text-accent-600 hover:underline" onClick={() => setParam('productId', '')}>Clear</button>
        </div>
      )}
      <TableToolbar>
        <input className="input flex-1" placeholder="Search reference, reason or SKU…" value={search}
          onChange={(e) => setParam('search', e.target.value)} aria-label="Search movements" />
        <select className="input sm:w-52" value={warehouseId} onChange={(e) => setParam('warehouseId', e.target.value)} aria-label="Filter by warehouse">
          <option value="">All warehouses</option>
          {(warehouses || []).map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
        </select>
        <select className="input sm:w-56" value={movementType} onChange={(e) => setParam('movementType', e.target.value)} aria-label="Filter by movement type">
          <option value="">All types</option>
          {['OpeningBalance', 'GoodsReceipt', 'StockIssue', 'Sale', 'TransferOut', 'TransferIn', 'Adjustment',
            'CustomerReturn', 'SupplierReturn', 'Reversal', 'StockCountVariance'].map((t) => (
            <option key={t} value={t}>{t.replace(/([A-Z])/g, ' $1').trim()}</option>
          ))}
        </select>
      </TableToolbar>

      <div className="mt-4" />
      <DataTable
        loading={query.isLoading}
        error={query.isError ? query.error : null}
        onRetry={query.refetch}
        rows={query.data?.items}
        empty={{ icon: ClipboardList, title: 'No movements', description: 'Stock movements appear here as soon as inventory changes.' }}
        minWidth="min-w-[980px]"
        page={query.data?.page}
        pageSize={query.data?.pageSize}
        totalCount={query.data?.totalCount}
        onPage={(p) => setParam('page', String(p))}
        onPageSize={(n) => setParam('pageSize', String(n))}
        columns={[
          {
            key: 'createdAtUtc', header: 'When',
            render: (m) => <span className="whitespace-nowrap text-slate-500">{formatDateTime(m.createdAtUtc, auth.dateFormat)}</span>
          },
          {
            key: 'productName', header: 'Product',
            render: (m) => (
              <div>
                <p className="max-w-[180px] truncate font-medium">{m.productName}</p>
                <p className="text-xs text-slate-400">{m.sku}</p>
              </div>
            )
          },
          {
            key: 'movementType', header: 'Type',
            render: (m) => <Badge color={typeColor(m.movementType)}>{m.movementType.replace(/([A-Z])/g, ' $1').trim()}</Badge>
          },
          {
            key: 'quantityDelta', header: 'Qty Δ', align: 'right',
            render: (m) => (
              <span className={`font-semibold tabular-nums ${m.quantityDelta > 0 ? 'text-emerald-600' : 'text-red-600'}`}>
                {m.quantityDelta > 0 ? '+' : ''}{Number(m.quantityDelta)}
              </span>
            )
          },
          { key: 'warehouseName', header: 'Warehouse', render: (m) => <span className="text-slate-500">{m.warehouseName}</span> },
          { key: 'batchNumber', header: 'Batch', render: (m) => <span className="text-slate-500">{m.batchNumber || '—'}</span> },
          {
            key: 'referenceNumber', header: 'Reference',
            render: (m) => <span className="text-slate-500">{m.referenceNumber || m.referenceType || '—'}</span>
          },
          {
            key: 'reason', header: 'Reason',
            render: (m) => <span className="block max-w-[200px] truncate text-slate-500" title={m.reason || ''}>{m.reason || '—'}</span>
          },
          { key: 'userName', header: 'By', render: (m) => <span className="text-slate-500">{m.userName}</span> }
        ]}
      />
    </div>
  )
}
