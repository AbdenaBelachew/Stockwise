import { Link } from 'react-router-dom'
import { Compass } from 'lucide-react'

export default function NotFoundPage() {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center px-4 text-center">
      <Compass className="h-12 w-12 text-slate-300 dark:text-slate-600" strokeWidth={1.5} />
      <h1 className="mt-4 text-2xl font-bold text-slate-800 dark:text-slate-100">Page not found</h1>
      <p className="mt-2 max-w-md text-sm text-slate-500 dark:text-slate-400">
        The page you are looking for does not exist or has been moved.
      </p>
      <Link to="/" className="btn-primary mt-6">Back to overview</Link>
    </div>
  )
}

export { }
