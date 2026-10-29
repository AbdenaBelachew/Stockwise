import { useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Boxes } from 'lucide-react'
import { api, hasPermission, qs } from '../lib/api-client.js'
import { P } from '../lib/permissions.js'
import { useAuth } from '../app/providers.jsx'
import { formatMoney, formatQty } from '../lib/formatters.js'
import { Badge, PageHeader } from '../components/ui/index.jsx'
import { DataTable, TableToolbar } from '../components/data-display/DataTable.jsx'

export default function StockOnHandPage() {
  const auth = useAuth()
  const [params, setParams] = useSearchParams()
  const search = params.get('search') || ''
  const warehouseId = params.get('warehouseId') || ''
  const stockStatus = params.get('stockStatus') || ''
  const page = parseInt(params.get('page') || '1', 10)
  const pageSize = parseInt(params.get('pageSize') || '5', 10)

  const { data: warehouses } = useQuery({ queryKey: ['warehouses'], queryFn: () => api.get('/api/v1/warehouses') })
  const query = useQuery({
    queryKey: ['balances', { search, warehouseId, stockStatus, page }],
    queryFn: () => api.get(`/api/v1/inventory/balances${qs({ search, warehouseId, stockStatus, page, pageSize })}`),
    enabled: hasPermission(P.InventoryRead)
  })

  const setParam = (key, value) => {
    const next = new URLSearchParams(params)
    if (value) next.set(key, value)
    else next.delete(key)
    if (key !== 'page') next.set('page', '1')
    setParams(next)
  }

  const statusColor = (s) => (s === 'out' ? 'red' : s === 'low' ? 'amber' : 'green')

  return (
    <div>
      <PageHeader
        title="Stock on hand"
        description="Current balances per product and warehouse, kept in sync with the movement ledger."
      />
      <TableToolbar>
        <input
          className="input flex-1"
          placeholder="Search product or SKU…"
          value={search}
          onChange={(e) => setParam('search', e.target.value)}
          aria-label="Search stock"
        />
        <select className="input sm:w-52" value={warehouseId} onChange={(e) => setParam('warehouseId', e.target.value)} aria-label="Filter by warehouse">
          <option value="">All warehouses</option>
          {(warehouses || []).map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
        </select>
        <select className="input sm:w-44" value={stockStatus} onChange={(e) => setParam('stockStatus', e.target.value)} aria-label="Filter by stock status">
          <option value="">Any level</option>
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
        keyField="productId"
        empty={{ icon: Boxes, title: 'No stock records', description: 'Post an opening balance or receive a purchase order to create stock.' }}
        minWidth="min-w-[820px]"
        page={query.data?.page}
        pageSize={query.data?.pageSize}
        totalCount={query.data?.totalCount}
        onPage={(p) => setParam('page', String(p))}
        onPageSize={(n) => setParam('pageSize', String(n))}
        columns={[
          {
            key: 'productName', header: 'Product',
            render: (b) => (
              <div>
                <p className="font-medium">{b.productName}</p>
                <p className="text-xs text-slate-400">{b.sku}</p>
              </div>
            )
          },
          { key: 'warehouseName', header: 'Warehouse', render: (b) => <span className="text-slate-500">{b.warehouseName}</span> },
          {
            key: 'quantity', header: 'Quantity', align: 'right',
            render: (b) => <span className="font-semibold tabular-nums">{formatQty(b.quantity, b.unitSymbol)}</span>
          },
          {
            key: 'avgUnitCost', header: 'Avg. unit cost', align: 'right',
            render: (b) => <span className="tabular-nums text-slate-500">{formatMoney(b.avgUnitCost, auth.currency)}</span>
          },
          {
            key: 'totalValue', header: 'Value', align: 'right',
            render: (b) => <span className="tabular-nums">{formatMoney(b.totalValue, auth.currency)}</span>
          },
          {
            key: 'reorderPoint', header: 'Reorder point', align: 'right',
            render: (b) => <span className="tabular-nums text-slate-500">{formatQty(b.reorderPoint)}</span>
          },
          {
            key: 'stockStatus', header: 'Status', align: 'right',
            render: (b) => <Badge color={statusColor(b.stockStatus)}>{b.stockStatus === 'out' ? 'Out' : b.stockStatus === 'low' ? 'Low' : 'In stock'}</Badge>
          }
        ]}
      />
    </div>
  )
}
