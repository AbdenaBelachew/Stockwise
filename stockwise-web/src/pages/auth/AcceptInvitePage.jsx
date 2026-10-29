import { useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { Loader2 } from 'lucide-react'
import { api, setAuthInfo } from '../../lib/api-client.js'
import { Field, toast } from '../../components/ui/index.jsx'

export default function AcceptInvitePage() {
  const [params] = useSearchParams()
  const token = params.get('token') || ''
  const navigate = useNavigate()
  const [serverError, setServerError] = useState(null)
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting }
  } = useForm()

  const onSubmit = async (data) => {
    setServerError(null)
    try {
      const res = await api.post('/api/v1/invitations/accept', {
        token: token || data.token,
        fullName: data.fullName,
        password: data.password
      })
      setAuthInfo(res)
      toast.success(`Welcome to ${res.organizationName}!`)
      navigate('/')
    } catch (e) {
      setServerError(e.message)
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center px-4">
      <div className="card w-full max-w-md p-6">
        <h1 className="text-lg font-semibold">Join your team on StockWise</h1>
        <p className="mb-4 mt-1 text-sm text-slate-500">
          {token ? 'Set up your account to accept the invitation.' : 'Paste your invitation token below.'}
        </p>
        {serverError && (
          <div className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700" role="alert">{serverError}</div>
        )}
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
          {!token && (
            <Field label="Invitation token" error={errors.token?.message} required>
              <textarea className="input h-20" {...register('token', { required: 'Paste the invitation token from your email.' })} />
            </Field>
          )}
          <Field label="Full name" error={errors.fullName?.message} required>
            <input className="input" {...register('fullName', { required: 'Name is required.', maxLength: 200 })} />
          </Field>
          <Field label="Password" error={errors.password?.message} required hint="Minimum 10 characters with a letter and a digit.">
            <input type="password" className="input" autoComplete="new-password" {...register('password', {
              required: 'Password is required.',
              minLength: { value: 10, message: 'At least 10 characters.' },
              pattern: { value: /^(?=.*[A-Za-z])(?=.*\d).+$/, message: 'Must contain a letter and a digit.' }
            })} />
          </Field>
          <button className="btn-primary w-full" disabled={isSubmitting}>
            {isSubmitting && <Loader2 className="h-4 w-4 animate-spin" />}
            Accept invitation
          </button>
        </form>
        <p className="mt-4 text-center text-sm">
          <Link to="/login" className="text-accent-600 hover:underline">Sign in instead</Link>
        </p>
      </div>
    </div>
  )
}
