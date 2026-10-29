import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Bell, CheckCheck, AlertTriangle, XCircle, Info } from 'lucide-react'
import { api } from '../lib/api-client.js'
import { useAuth } from '../app/providers.jsx'
import { Link } from 'react-router-dom'
import { formatDateTime } from '../lib/formatters.js'
import { Badge, EmptyState, ErrorState, PageHeader, TableSkeleton, toast } from '../components/ui/index.jsx'

export default function NotificationsPage() {
  const auth = useAuth()
  const queryClient = useQueryClient()

  const query = useQuery({
    queryKey: ['notifications'],
    queryFn: () => api.get('/api/v1/notifications?limit=100'),
    refetchInterval: 60000
  })

  const markRead = useMutation({
    mutationFn: (id) => api.post(`/api/v1/notifications/${id}/read`, {}),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notifications'] })
  })
  const markAll = useMutation({
    mutationFn: () => api.post('/api/v1/notifications/read-all', {}),
    onSuccess: () => {
      toast.success('All notifications marked as read.')
      queryClient.invalidateQueries({ queryKey: ['notifications'] })
    }
  })

  const icons = { Critical: XCircle, Warning: AlertTriangle, Info: Info }
  const colors = { Critical: 'red', Warning: 'amber', Info: 'blue' }
  const rows = query.data || []
  const unread = rows.filter((n) => !n.isRead)

  return (
    <div>
      <PageHeader
        title="Notifications"
        description="Low stock, expiry and procurement alerts. Alerts deduplicate automatically and clear when resolved."
        actions={unread.length > 0 && (
          <button className="btn-secondary" onClick={() => markAll.mutate()} disabled={markAll.isPending}>
            <CheckCheck className="h-4 w-4" /> Mark all read ({unread.length})
          </button>
        )}
      />
      <div className="card">
        {query.isLoading ? <TableSkeleton rows={6} cols={3} /> : query.isError ? (
          <ErrorState error={query.error} onRetry={query.refetch} />
        ) : rows.length === 0 ? (
          <EmptyState icon={Bell} title="No notifications" description="You're all caught up." />
        ) : (
          <div className="divide-y divide-slate-100 dark:divide-slate-800">
            {rows.map((n) => {
              const Icon = icons[n.severity] || Info
              const body = (
                <div className={`flex items-start gap-3 px-4 py-3.5 ${n.isRead ? 'opacity-60' : ''} hover:bg-slate-50 dark:hover:bg-slate-800/50`}>
                  <Icon className={`mt-0.5 h-5 w-5 ${n.severity === 'Critical' ? 'text-red-500' : n.severity === 'Warning' ? 'text-amber-500' : 'text-sky-500'}`} />
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center justify-between gap-2">
                      <p className="text-sm font-medium text-slate-800 dark:text-slate-100">{n.title}</p>
                      <Badge color={colors[n.severity]}>{n.severity}</Badge>
                    </div>
                    <p className="mt-0.5 text-sm text-slate-500 dark:text-slate-400">{n.message}</p>
                    <p className="mt-1 text-xs text-slate-400">{formatDateTime(n.createdAtUtc, auth.dateFormat)}</p>
                  </div>
                  {!n.isRead && (
                    <button
                      className="text-xs font-medium text-accent-600 hover:underline"
                      onClick={(e) => { e.preventDefault(); markRead.mutate(n.id) }}
                    >
                      Mark read
                    </button>
                  )}
                </div>
              )
              return n.link ? (
                <Link key={n.id} to={n.link} className="block">{body}</Link>
              ) : (
                <div key={n.id}>{body}</div>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}
