import { useState } from 'react'
import { useAuth } from '../app/providers.jsx'
import { api } from '../lib/api-client.js'
import { Field, PageHeader, toast } from '../components/ui/index.jsx'

export default function ProfilePage() {
  const auth = useAuth()
  const [fullName, setFullName] = useState(auth.user?.fullName || '')
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [busy, setBusy] = useState(false)

  const saveProfile = async (e) => {
    e.preventDefault()
    setBusy(true)
    try {
      await api.put('/api/v1/auth/profile', { fullName })
      toast.success('Profile updated.')
      auth.refreshMe()
    } catch (err) {
      toast.error(err.message)
    } finally {
      setBusy(false)
    }
  }

  const changePassword = async (e) => {
    e.preventDefault()
    if (newPassword !== confirmPassword) return toast.error('New passwords do not match.')
    setBusy(true)
    try {
      await api.post('/api/v1/auth/change-password', { currentPassword, newPassword })
      toast.success('Password changed.')
      setCurrentPassword(''); setNewPassword(''); setConfirmPassword('')
    } catch (err) {
      toast.error(err.message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <PageHeader title="Your profile" description="Account details and password." />
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <form onSubmit={saveProfile} className="card space-y-4 p-5">
          <h2 className="text-sm font-semibold">Profile</h2>
          <Field label="Email"><input className="input" value={auth.user?.email || ''} disabled /></Field>
          <Field label="Full name" required>
            <input className="input" value={fullName} onChange={(e) => setFullName(e.target.value)} />
          </Field>
          <Field label="Email verified">
            <p className={`text-sm font-medium ${auth.user?.emailVerified ? 'text-emerald-600' : 'text-amber-600'}`}>
              {auth.user?.emailVerified ? 'Verified' : 'Not verified — check the mail outbox in development mode.'}
            </p>
          </Field>
          <button className="btn-primary" disabled={busy || !fullName.trim()}>Save profile</button>
        </form>

        <form onSubmit={changePassword} className="card space-y-4 p-5">
          <h2 className="text-sm font-semibold">Change password</h2>
          <Field label="Current password" required>
            <input type="password" className="input" autoComplete="current-password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} />
          </Field>
          <Field label="New password" required hint="Minimum 10 characters with a letter and a digit.">
            <input type="password" className="input" autoComplete="new-password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)} />
          </Field>
          <Field label="Confirm new password" required>
            <input type="password" className="input" autoComplete="new-password" value={confirmPassword} onChange={(e) => setConfirmPassword(e.target.value)} />
          </Field>
          <button className="btn-primary" disabled={busy || !currentPassword || !newPassword}>Change password</button>
        </form>
      </div>
    </div>
  )
}
