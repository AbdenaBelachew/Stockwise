import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { Loader2, MailCheck } from 'lucide-react'
import { api } from '../../lib/api-client.js'
import { Field } from '../../components/ui/index.jsx'

export default function ForgotPasswordPage() {
  const [sent, setSent] = useState(false)
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting }
  } = useForm()

  const onSubmit = async (data) => {
    await api.post('/api/v1/auth/forgot-password', data).catch(() => {})
    setSent(true) // always show success; never reveal whether the account exists
  }

  if (sent) {
    return (
      <div className="flex min-h-screen items-center justify-center px-4">
        <div className="card w-full max-w-md p-6 text-center">
          <MailCheck className="mx-auto h-10 w-10 text-emerald-500" />
          <h1 className="mt-3 text-lg font-semibold">Check your inbox</h1>
          <p className="mt-2 text-sm text-slate-500">
            If an account exists for that email, a password reset link has been sent. The link is valid for one hour.
          </p>
          <Link to="/login" className="btn-secondary mt-4 inline-flex">Back to sign in</Link>
        </div>
      </div>
    )
  }

  return (
    <div className="flex min-h-screen items-center justify-center px-4">
      <div className="card w-full max-w-md p-6">
        <h1 className="text-lg font-semibold">Forgot your password?</h1>
        <p className="mb-4 mt-1 text-sm text-slate-500">Enter your account email and we will send a reset link.</p>
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
          <Field label="Email" error={errors.email?.message} required>
            <input type="email" className="input" autoComplete="email" {...register('email', {
              required: 'Email is required.',
              pattern: { value: /^[^\s@]+@[^\s@]+\.[^\s@]+$/, message: 'Enter a valid email address.' }
            })} />
          </Field>
          <button className="btn-primary w-full" disabled={isSubmitting}>
            {isSubmitting && <Loader2 className="h-4 w-4 animate-spin" />}
            Send reset link
          </button>
        </form>
        <p className="mt-4 text-center text-sm">
          <Link to="/login" className="text-accent-600 hover:underline">Back to sign in</Link>
        </p>
      </div>
    </div>
  )
}
