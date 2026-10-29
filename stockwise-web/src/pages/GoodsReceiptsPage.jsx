import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { ArrowLeft, PackageCheck, Printer } from 'lucide-react'
import { api, hasPermission, qs } from '../lib/api-client.js'
import { P } from '../lib/permissions.js'
import { useAuth } from '../app/providers.jsx'
import { formatDateTime, formatDate } from '../lib/formatters.js'
import { PageHeader, TableSkeleton, ErrorState } from '../components/ui/index.jsx'
import { DataTable } from '../components/data-display/DataTable.jsx'

export default function GoodsReceiptsPage() {
  const auth = useAuth()
  const navigate = useNavigate()
  const [params] = useState(() => new URLSearchParams(window.location.search))
  const page = parseInt(params.get('page') || '1', 10)
  const pageSize = parseInt(params.get('pageSize') || '5', 10)
  const setParams = (next) => navigate({ search: next.toString() }, { replace: true })
  const setPage = (p) => { const n = new URLSearchParams(params); n.set('page', String(p)); setParams(n) }
  const setPageSize = (n) => { const next = new URLSearchParams(params); next.set('pageSize', String(n)); next.set('page', '1'); setParams(next) }

  const query = useQuery({
    queryKey: ['goods-receipts', page, pageSize],
    queryFn: () => api.get(`/api/v1/goods-receipts${qs({ page, pageSize })}`),
    enabled: hasPermission(P.PurchaseOrdersRead)
  })

  return (
    <div>
      <PageHeader
        title="Goods receiving"
        description="Every posted receipt with its accepted and rejected quantities."
      />
      <div className="mt-4" />
      <DataTable
        loading={query.isLoading}
        error={query.isError ? query.error : null}
        onRetry={query.refetch}
        rows={query.data?.items}
        empty={{ icon: PackageCheck, title: 'No receipts yet', description: 'Receive goods against an approved purchase order.' }}
        minWidth="min-w-[760px]"
        page={query.data?.page}
        pageSize={query.data?.pageSize ?? pageSize}
        totalCount={query.data?.totalCount}
        onPage={setPage}
        onPageSize={setPageSize}
        columns={[
          {
            key: 'number', header: 'Number',
            render: (g) => <Link className="font-mono text-sm font-medium text-accent-600 hover:underline" to={`/goods-receipts/${g.id}`}>{g.number}</Link>
          },
          { key: 'purchaseOrderNumber', header: 'Purchase order', render: (g) => <span className="text-slate-500">{g.purchaseOrderNumber}</span> },
          { key: 'supplierName', header: 'Supplier', render: (g) => <span className="text-slate-500">{g.supplierName}</span> },
          { key: 'warehouseName', header: 'Warehouse', render: (g) => <span className="text-slate-500">{g.warehouseName}</span> },
          {
            key: 'createdAt', header: 'Received at',
            render: (g) => <span className="text-slate-500">{formatDateTime(g.createdAt, auth.dateFormat)}</span>
          },
          {
            key: 'actions', header: '', align: 'right',
            render: (g) => <Link className="text-sm font-medium text-accent-600 hover:underline" to={`/goods-receipts/${g.id}`}>Open</Link>
          }
        ]}
      />
    </div>
  )
}

export function GoodsReceiptDetailPage() {
  const { id } = useParams()
  const auth = useAuth()

  const query = useQuery({
    queryKey: ['goods-receipts', id],
    queryFn: () => api.get(`/api/v1/goods-receipts/${id}`)
  })

  if (query.isLoading) return <TableSkeleton rows={6} cols={5} />
  if (query.isError) return <ErrorState error={query.error} onRetry={query.refetch} />
  const g = query.data

  return (
    <div>
      <div className="mb-4">
        <Link to="/goods-receipts" className="flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700 dark:text-slate-400">
          <ArrowLeft className="h-4 w-4" /> Goods receiving
        </Link>
      </div>
      <PageHeader
        title={`Goods receipt ${g.number}`}
        description={`Against purchase order ${g.purchaseOrderNumber} · ${g.supplierName}`}
        actions={<button className="btn-secondary" onClick={() => window.print()}><Printer className="h-4 w-4" /> Print note</button>}
      />

      <div className="card p-5">
        <dl className="grid grid-cols-2 gap-4 text-sm sm:grid-cols-4">
          <div><dt className="text-xs text-slate-400">Warehouse</dt><dd className="font-medium">{g.warehouseName}</dd></div>
          <div><dt className="text-xs text-slate-400">Received by</dt><dd className="font-medium">{g.createdByName}</dd></div>
          <div><dt className="text-xs text-slate-400">Received at</dt><dd className="font-medium">{formatDateTime(g.createdAt, auth.dateFormat)}</dd></div>
          <div><dt className="text-xs text-slate-400">Notes</dt><dd className="font-medium">{g.notes || '—'}</dd></div>
        </dl>
      </div>

      <div className="card mt-6">
        <div className="border-b border-slate-100 px-4 py-3 dark:border-slate-800">
          <h2 className="text-sm font-semibold">Received lines</h2>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full min-w-[700px]">
            <thead>
              <tr><th className="th">Product</th><th className="th text-right">Accepted</th><th className="th text-right">Rejected</th>
                <th className="th">Reject reason</th><th className="th">Batch / lot</th><th className="th">Expiry</th></tr>
            </thead>
            <tbody>
              {g.lines.map((l) => (
                <tr key={l.id} className="table-row">
                  <td className="td">{l.productName}<p className="text-xs text-slate-400">{l.sku}</p></td>
                  <td className="td text-right font-semibold tabular-nums text-emerald-600">{Number(l.quantityAccepted)}</td>
                  <td className="td text-right tabular-nums text-red-600">{Number(l.quantityRejected) || '—'}</td>
                  <td className="td text-slate-500">{l.rejectReason || '—'}</td>
                  <td className="td font-mono text-xs">{l.batchNumber || '—'}</td>
                  <td className="td text-slate-500">{l.expiryDate ? formatDate(l.expiryDate, auth.dateFormat) : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
