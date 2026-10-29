import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ShieldCheck } from 'lucide-react'
import { api, hasPermission, qs } from '../lib/api-client.js'
import { P } from '../lib/permissions.js'
import { useAuth } from '../app/providers.jsx'
import { formatDateTime } from '../lib/formatters.js'
import { PageHeader } from '../components/ui/index.jsx'
import { DataTable, TableToolbar } from '../components/data-display/DataTable.jsx'

export default function AuditLogsPage() {
  const auth = useAuth()
  const [params, setParams] = useState(() => new URLSearchParams(window.location.search))
  const search = params.get('search') || ''
  const page = parseInt(params.get('page') || '1', 10)
  const pageSize = parseInt(params.get('pageSize') || '5', 10)

  const query = useQuery({
    queryKey: ['audit-logs', { search, page }],
    queryFn: () => api.get(`/api/v1/audit-logs${qs({ search, page, pageSize })}`),
    enabled: hasPermission(P.AuditLogsRead)
  })

  const setParam = (key, value) => {
    const next = new URLSearchParams(params)
    if (value) next.set(key, value)
    else next.delete(key)
    if (key !== 'page') next.set('page', '1')
    setParams(next)
    window.history.replaceState(null, '', `?${next.toString()}`)
  }

  return (
    <div>
      <PageHeader
        title="Audit log"
        description="Append-only record of security-relevant actions. Contains no passwords or secrets."
      />
      <TableToolbar>
        <input className="input sm:w-80" placeholder="Filter by actor, action or entity…" value={search}
          onChange={(e) => setParam('search', e.target.value)} aria-label="Filter audit log" />
      </TableToolbar>

      <div className="mt-4" />
      <DataTable
        loading={query.isLoading}
        error={query.isError ? query.error : null}
        onRetry={query.refetch}
        rows={query.data?.items}
        empty={{ icon: ShieldCheck, title: 'No audit entries', description: 'Actions will appear here as they happen.' }}
        minWidth="min-w-[820px]"
        page={query.data?.page}
        pageSize={query.data?.pageSize}
        totalCount={query.data?.totalCount}
        onPage={(p) => setParam('page', String(p))}
        onPageSize={(n) => setParam('pageSize', String(n))}
        columns={[
          {
            key: 'createdAtUtc', header: 'When',
            render: (a) => <span className="whitespace-nowrap text-slate-500">{formatDateTime(a.createdAtUtc, auth.dateFormat)}</span>
          },
          { key: 'actorEmail', header: 'Actor', render: (a) => <span className="text-slate-600 dark:text-slate-300">{a.actorEmail}</span> },
          { key: 'action', header: 'Action', render: (a) => <span className="font-mono text-xs">{a.action}</span> },
          { key: 'entityType', header: 'Entity', render: (a) => <span className="text-slate-500">{a.entityType}</span> },
          {
            key: 'details', header: 'Details',
            render: (a) => <span className="block max-w-[280px] truncate font-mono text-xs text-slate-400" title={a.details || ''}>{a.details || '—'}</span>
          },
          { key: 'ipAddress', header: 'IP', render: (a) => <span className="font-mono text-xs text-slate-400">{a.ipAddress || '—'}</span> }
        ]}
      />
    </div>
  )
}
