import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { Boxes, Loader2 } from 'lucide-react'
import { useAuth } from '../../app/providers.jsx'
import { Field, toast } from '../../components/ui/index.jsx'

const timezones = [
  'UTC', 'Europe/London', 'Europe/Berlin', 'Europe/Paris', 'Europe/Istanbul', 'Europe/Moscow',
  'America/New_York', 'America/Chicago', 'America/Denver', 'America/Los_Angeles', 'America/Sao_Paulo',
  'Africa/Cairo', 'Africa/Lagos', 'Africa/Johannesburg', 'Asia/Dubai', 'Asia/Karachi', 'Asia/Kolkata',
  'Asia/Dhaka', 'Asia/Bangkok', 'Asia/Shanghai', 'Asia/Tokyo', 'Asia/Singapore', 'Australia/Sydney',
  'Pacific/Auckland'
]
const currencies = ['USD', 'EUR', 'GBP', 'TRY', 'AED', 'SAR', 'JPY', 'CNY', 'INR', 'CAD', 'AUD', 'BRL', 'ZAR']

export default function RegisterPage() {
  const auth = useAuth()
  const navigate = useNavigate()
  const [serverError, setServerError] = useState(null)
  const {
    register,
    handleSubmit,
    watch,
    formState: { errors, isSubmitting }
  } = useForm({
    defaultValues: {
      timeZoneId: 'UTC',
      currencyCode: 'USD',
      dateFormat: 'yyyy-MM-dd'
    }
  })
  const password = watch('password')

  const onSubmit = async (data) => {
    setServerError(null)
    try {
      await auth.register(data)
      toast.success('Welcome to StockWise! Your organization is ready.')
      navigate('/', { replace: true })
    } catch (e) {
      setServerError(e.message)
      if (e.errors) {
        // surface first field error
        const first = Object.values(e.errors).flat()[0]
        if (first) setServerError(first)
      }
    }
  }

  return (
    <div className="min-h-screen bg-slate-50 px-4 py-10 dark:bg-slate-950">
      <div className="mx-auto max-w-2xl">
        <div className="mb-8 flex flex-col items-center">
          <Boxes className="h-12 w-12 text-accent-600" strokeWidth={1.75} />
          <h1 className="mt-3 text-2xl font-bold text-slate-800 dark:text-slate-100">Create your StockWise account</h1>
          <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">Set up your organization in one step.</p>
        </div>

        <div className="card p-6">
          {serverError && (
            <div className="mb-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700 dark:bg-red-900/30 dark:text-red-300" role="alert">
              {serverError}
            </div>
          )}
          <form onSubmit={handleSubmit(onSubmit)} className="grid grid-cols-1 gap-4 sm:grid-cols-2" noValidate>
            <Field label="Your full name" error={errors.fullName?.message} required>
              <input className="input" autoComplete="name" {...register('fullName', { required: 'Name is required.', maxLength: 200 })} />
            </Field>
            <Field label="Email" error={errors.email?.message} required>
              <input type="email" className="input" autoComplete="email" {...register('email', {
                required: 'Email is required.',
                pattern: { value: /^[^\s@]+@[^\s@]+\.[^\s@]+$/, message: 'Enter a valid email address.' }
              })} />
            </Field>
            <Field label="Password" error={errors.password?.message} required hint="Minimum 10 characters with at least one letter and one digit.">
              <input type="password" className="input" autoComplete="new-password" {...register('password', {
                required: 'Password is required.',
                minLength: { value: 10, message: 'At least 10 characters.' },
                pattern: { value: /^(?=.*[A-Za-z])(?=.*\d).+$/, message: 'Must contain a letter and a digit.' }
              })} />
            </Field>
            <Field label="Confirm password" error={errors.confirm?.message} required>
              <input type="password" className="input" autoComplete="new-password" {...register('confirm', {
                required: 'Confirm the password.',
                validate: (v) => v === password || 'Passwords do not match.'
              })} />
            </Field>

            <div className="sm:col-span-2">
              <h3 className="mb-3 border-t border-slate-200 pt-4 text-sm font-semibold text-slate-600 dark:border-slate-700 dark:text-slate-300">
                Organization
              </h3>
            </div>
            <Field label="Organization name" error={errors.organizationName?.message} required>
              <input className="input" {...register('organizationName', { required: 'Organization name is required.', maxLength: 200 })} />
            </Field>
            <Field label="Industry" error={errors.industry?.message}>
              <select className="input" {...register('industry')}>
                <option value="">Select…</option>
                <option>Retail</option>
                <option>Pharmacy</option>
                <option>Distribution</option>
                <option>Manufacturing</option>
                <option>Wholesale</option>
                <option>Other</option>
              </select>
            </Field>
            <Field label="Country" error={errors.country?.message} required>
              <input className="input" defaultValue="" {...register('country', { required: 'Country is required.', maxLength: 100 })} />
            </Field>
            <Field label="Time zone" error={errors.timeZoneId?.message} required>
              <select className="input" {...register('timeZoneId', { required: true })}>
                {timezones.map((tz) => <option key={tz} value={tz}>{tz}</option>)}
              </select>
            </Field>
            <Field label="Default currency" error={errors.currencyCode?.message} required>
              <select className="input" {...register('currencyCode', { required: true })}>
                {currencies.map((c) => <option key={c} value={c}>{c}</option>)}
              </select>
            </Field>
            <Field label="Date format" error={errors.dateFormat?.message} required>
              <select className="input" {...register('dateFormat', { required: true })}>
                <option value="yyyy-MM-dd">2026-10-28</option>
                <option value="dd/MM/yyyy">28/10/2026</option>
                <option value="dd.MM.yyyy">28.10.2026</option>
                <option value="MM/dd/yyyy">10/28/2026</option>
              </select>
            </Field>

            <div className="sm:col-span-2">
              <button className="btn-primary w-full" disabled={isSubmitting}>
                {isSubmitting && <Loader2 className="h-4 w-4 animate-spin" />}
                Create account and organization
              </button>
            </div>
          </form>
        </div>

        <p className="mt-4 text-center text-sm text-slate-500">
          Already have an account?{' '}
          <Link to="/login" className="font-medium text-accent-600 hover:underline">Sign in</Link>
        </p>
      </div>
    </div>
  )
}
