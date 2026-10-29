import { Link } from 'react-router-dom'
import { ShieldX } from 'lucide-react'

export default function ForbiddenPage() {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center px-4 text-center">
      <ShieldX className="h-12 w-12 text-red-300 dark:text-red-800" strokeWidth={1.5} />
      <h1 className="mt-4 text-2xl font-bold text-slate-800 dark:text-slate-100">Access denied</h1>
      <p className="mt-2 max-w-md text-sm text-slate-500 dark:text-slate-400">
        You do not have permission to view this page. Ask an administrator if you believe this is a mistake.
      </p>
      <Link to="/" className="btn-primary mt-6">Back to overview</Link>
    </div>
  )
}
