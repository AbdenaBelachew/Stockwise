import { useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { Loader2 } from 'lucide-react'
import { api } from '../../lib/api-client.js'
import { Field, toast } from '../../components/ui/index.jsx'

export default function ResetPasswordPage() {
  const [params] = useSearchParams()
  const token = params.get('token') || ''
  const navigate = useNavigate()
  const [serverError, setServerError] = useState(null)
  const {
    register,
    handleSubmit,
    watch,
    formState: { errors, isSubmitting }
  } = useForm()

  const onSubmit = async (data) => {
    setServerError(null)
    try {
      await api.post('/api/v1/auth/reset-password', { token, password: data.password })
      toast.success('Password updated. You can sign in now.')
      navigate('/login')
    } catch (e) {
      setServerError(e.message)
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center px-4">
      <div className="card w-full max-w-md p-6">
        <h1 className="text-lg font-semibold">Set a new password</h1>
        <p className="mb-4 mt-1 text-sm text-slate-500">Choose a strong password with at least 10 characters.</p>
        {serverError && (
          <div className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700" role="alert">{serverError}</div>
        )}
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
          <Field label="New password" error={errors.password?.message} required>
            <input type="password" className="input" autoComplete="new-password" {...register('password', {
              required: 'Password is required.',
              minLength: { value: 10, message: 'At least 10 characters.' },
              pattern: { value: /^(?=.*[A-Za-z])(?=.*\d).+$/, message: 'Must contain a letter and a digit.' }
            })} />
          </Field>
          <Field label="Confirm password" error={errors.confirm?.message} required>
            <input type="password" className="input" autoComplete="new-password" {...register('confirm', {
              required: 'Confirm the password.',
              validate: (v) => v === watch('password') || 'Passwords do not match.'
            })} />
          </Field>
          <button className="btn-primary w-full" disabled={isSubmitting || !token}>
            {isSubmitting && <Loader2 className="h-4 w-4 animate-spin" />}
            Reset password
          </button>
          {!token && <p className="text-xs text-red-600">Missing reset token in the link.</p>}
        </form>
        <p className="mt-4 text-center text-sm">
          <Link to="/login" className="text-accent-600 hover:underline">Back to sign in</Link>
        </p>
      </div>
    </div>
  )
}
