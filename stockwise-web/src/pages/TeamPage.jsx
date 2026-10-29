import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { UserPlus, Trash2, Users, Copy } from 'lucide-react'
import { api } from '../lib/api-client.js'
import { useAuth } from '../app/providers.jsx'
import { formatDateTime } from '../lib/formatters.js'
import { Badge, ConfirmDialog, EmptyState, ErrorState, Field, Modal, PageHeader, TableSkeleton, toast } from '../components/ui/index.jsx'

export default function TeamPage() {
  const auth = useAuth()
  const queryClient = useQueryClient()
  const [inviteOpen, setInviteOpen] = useState(false)
  const [revoking, setRevoking] = useState(null)

  const members = useQuery({ queryKey: ['members'], queryFn: () => api.get('/api/v1/organizations/current/members') })
  const roles = useQuery({ queryKey: ['roles'], queryFn: () => api.get('/api/v1/organizations/current/roles') })
  const invitations = useQuery({ queryKey: ['invitations'], queryFn: () => api.get('/api/v1/organizations/current/invitations') })

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['members'] })
    queryClient.invalidateQueries({ queryKey: ['invitations'] })
  }

  const updateMember = useMutation({
    mutationFn: ({ userId, body }) => api.patch(`/api/v1/organizations/current/members/${userId}`, body),
    onSuccess: () => {
      toast.success('Member updated.')
      invalidate()
    },
    onError: (e) => toast.error(e.message)
  })

  const revoke = useMutation({
    mutationFn: (id) => api.delete(`/api/v1/organizations/current/invitations/${id}`),
    onSuccess: () => {
      toast.success('Invitation revoked.')
      setRevoking(null)
      invalidate()
    },
    onError: (e) => toast.error(e.message)
  })

  return (
    <div>
      <PageHeader
        title="Team & roles"
        description="Invite teammates and manage their roles. Roles map to server-enforced permissions."
        actions={<button className="btn-primary" onClick={() => setInviteOpen(true)}><UserPlus className="h-4 w-4" /> Invite member</button>}
      />

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-3">
        <div className="card xl:col-span-2">
          <div className="border-b border-slate-100 px-4 py-3 dark:border-slate-800">
            <h2 className="flex items-center gap-2 text-sm font-semibold"><Users className="h-4 w-4" /> Members</h2>
          </div>
          {members.isLoading ? <TableSkeleton rows={4} cols={4} /> : members.isError ? (
            <ErrorState error={members.error} onRetry={members.refetch} />
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full min-w-[640px]">
                <thead><tr><th className="th">Member</th><th className="th">Role</th><th className="th">Status</th><th className="th">Joined</th><th className="th" /></tr></thead>
                <tbody>
                  {members.data.map((m) => (
                    <tr key={m.userId} className="table-row">
                      <td className="td">{m.fullName}<p className="text-xs text-slate-400">{m.email}</p></td>
                      <td className="td">
                        <select
                          className="input py-1 text-sm"
                          value={m.roleId}
                          disabled={m.userId === auth.user?.id}
                          onChange={(e) => updateMember.mutate({ userId: m.userId, body: { roleId: e.target.value, status: null } })}
                          aria-label={`Role for ${m.fullName}`}
                        >
                          {(roles.data || []).map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
                        </select>
                      </td>
                      <td className="td"><Badge color={m.status === 'Active' ? 'green' : 'gray'}>{m.status}</Badge></td>
                      <td className="td text-slate-500">{formatDateTime(m.joinedAtUtc, auth.dateFormat)}</td>
                      <td className="td text-right">
                        {m.userId !== auth.user?.id && (
                          <button
                            className="btn-ghost p-1.5 text-red-500"
                            title={m.status === 'Active' ? 'Disable' : 'Enable'}
                            onClick={() => updateMember.mutate({ userId: m.userId, body: { roleId: m.roleId, status: m.status === 'Active' ? 'Disabled' : 'Active' } })}
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>

        <div className="space-y-6">
          <div className="card">
            <div className="border-b border-slate-100 px-4 py-3 dark:border-slate-800">
              <h2 className="text-sm font-semibold">Pending invitations</h2>
            </div>
            {invitations.isLoading ? <TableSkeleton rows={2} cols={2} /> : (invitations.data || []).length === 0 ? (
              <p className="px-4 py-6 text-center text-sm text-slate-400">No pending invitations.</p>
            ) : (
              <div className="divide-y divide-slate-100 dark:divide-slate-800">
                {invitations.data.map((inv) => (
                  <div key={inv.id} className="flex items-center justify-between gap-2 px-4 py-3">
                    <div className="min-w-0">
                      <p className="truncate text-sm font-medium">{inv.email}</p>
                      <p className="text-xs text-slate-400">{inv.roleName} · expires {formatDateTime(inv.expiresAtUtc, auth.dateFormat)}</p>
                      {inv.acceptToken && (
                        <button
                          className="mt-1 flex items-center gap-1 text-xs text-accent-600 hover:underline"
                          onClick={() => {
                            navigator.clipboard.writeText(`${window.location.origin}/accept-invitation?token=${inv.acceptToken}`)
                            toast.success('Invitation link copied to clipboard.')
                          }}
                        >
                          <Copy className="h-3 w-3" /> Copy invite link
                        </button>
                      )}
                    </div>
                    <button className="btn-ghost p-1.5 text-red-500" onClick={() => setRevoking(inv)} aria-label={`Revoke invitation for ${inv.email}`}>
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>

          <div className="card p-4">
            <h2 className="mb-3 text-sm font-semibold">Roles & permissions</h2>
            <div className="space-y-3">
              {(roles.data || []).map((r) => (
                <div key={r.id}>
                  <p className="text-sm font-medium">{r.name}</p>
                  <p className="text-xs text-slate-400">{r.description}</p>
                  <p className="mt-1 text-xs text-slate-500">{r.permissions.length} permissions</p>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>

      {inviteOpen && (
        <InviteModal
          roles={roles.data || []}
          onClose={() => setInviteOpen(false)}
          onSaved={() => { setInviteOpen(false); invalidate() }}
        />
      )}
      <ConfirmDialog
        open={!!revoking}
        onClose={() => setRevoking(null)}
        title="Revoke invitation?"
        message={`The invitation for ${revoking?.email} will be revoked.`}
        confirmLabel="Revoke" danger busy={revoke.isPending}
        onConfirm={() => revoke.mutate(revoking.id)}
      />
    </div>
  )
}

function InviteModal({ roles, onClose, onSaved }) {
  const [email, setEmail] = useState('')
  const [roleId, setRoleId] = useState('')
  const [busy, setBusy] = useState(false)
  const [inviteLink, setInviteLink] = useState(null)

  const submit = async (e) => {
    e.preventDefault()
    setBusy(true)
    try {
      const inv = await api.post('/api/v1/organizations/current/invitations', { email, roleId })
      if (inv.acceptToken) {
        setInviteLink(`${window.location.origin}/accept-invitation?token=${inv.acceptToken}`)
      } else {
        toast.success('Invitation sent.')
        onSaved()
      }
    } catch (err) {
      toast.error(err.message)
    } finally {
      setBusy(false)
    }
  }

  if (inviteLink) {
    return (
      <Modal open onClose={onClose} title="Invitation created" size="md"
        footer={<button className="btn-primary" onClick={onSaved}>Done</button>}>
        <p className="mb-3 text-sm text-slate-500">
          Share this link with {email}. (Email delivery runs in development mode — the link is also written to the mail outbox.)
        </p>
        <div className="flex gap-2">
          <input className="input flex-1 font-mono text-xs" readOnly value={inviteLink} onFocus={(e) => e.target.select()} />
          <button className="btn-secondary" onClick={() => { navigator.clipboard.writeText(inviteLink); toast.success('Copied.') }}>Copy</button>
        </div>
      </Modal>
    )
  }

  return (
    <Modal open onClose={onClose} title="Invite a team member" size="sm"
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>Cancel</button>
          <button className="btn-primary" onClick={submit} disabled={busy || !email || !roleId}>Send invitation</button>
        </>
      }>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Field label="Email" required>
          <input type="email" className="input" value={email} onChange={(e) => setEmail(e.target.value)} autoFocus />
        </Field>
        <Field label="Role" required>
          <select className="input" value={roleId} onChange={(e) => setRoleId(e.target.value)}>
            <option value="">Select role…</option>
            {roles.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
          </select>
        </Field>
      </form>
    </Modal>
  )
}
