import { useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { Boxes, Loader2 } from 'lucide-react'
import { useAuth } from '../../app/providers.jsx'
import { Field } from '../../components/ui/index.jsx'
import { toast } from '../../components/ui/index.jsx'

export default function LoginPage() {
  const auth = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [serverError, setServerError] = useState(null)
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting }
  } = useForm()

  const onSubmit = async (data) => {
    setServerError(null)
    try {
      await auth.login(data.email, data.password)
      toast.success('Signed in.')
      navigate(location.state?.from?.pathname || '/', { replace: true })
    } catch (e) {
      setServerError(e.message)
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-50 px-4 dark:bg-slate-950">
      <div className="w-full max-w-md">
        <div className="mb-8 flex flex-col items-center">
          <Boxes className="h-12 w-12 text-accent-600" strokeWidth={1.75} />
          <h1 className="mt-3 text-2xl font-bold text-slate-800 dark:text-slate-100">StockWise</h1>
          <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">
            Know what is selling, what is expiring, and what needs reordering.
          </p>
        </div>

        <div className="card p-6">
          <h2 className="mb-4 text-lg font-semibold">Sign in</h2>
          {serverError && (
            <div className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-900/30 dark:text-red-300" role="alert">
              {serverError}
            </div>
          )}
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
            <Field label="Email" error={errors.email?.message} required>
              <input
                type="email"
                className="input"
                autoComplete="email"
                autoFocus
                {...register('email', {
                  required: 'Email is required.',
                  pattern: { value: /^[^\s@]+@[^\s@]+\.[^\s@]+$/, message: 'Enter a valid email address.' }
                })}
              />
            </Field>
            <Field label="Password" error={errors.password?.message} required>
              <input
                type="password"
                className="input"
                autoComplete="current-password"
                {...register('password', { required: 'Password is required.' })}
              />
            </Field>
            <div className="flex items-center justify-between">
              <Link to="/forgot-password" className="text-sm text-accent-600 hover:underline dark:text-accent-400">
                Forgot password?
              </Link>
            </div>
            <button className="btn-primary w-full" disabled={isSubmitting}>
              {isSubmitting && <Loader2 className="h-4 w-4 animate-spin" />}
              Sign in
            </button>
          </form>
        </div>

        <p className="mt-4 text-center text-sm text-slate-500 dark:text-slate-400">
          New to StockWise?{' '}
          <Link to="/register" className="font-medium text-accent-600 hover:underline dark:text-accent-400">
            Create an organization
          </Link>
          {' '}or{' '}
          <Link to="/accept-invitation" className="font-medium text-accent-600 hover:underline dark:text-accent-400">
            accept an invitation
          </Link>
        </p>
      </div>
    </div>
  )
}
