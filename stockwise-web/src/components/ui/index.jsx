import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { X, CheckCircle2, AlertTriangle, Info, XCircle, Loader2 } from 'lucide-react'

// ---------- Toasts ----------

let toastPush = null

const ToastContext = (() => {
  return {
    Provider: ({ children }) => {
      const [toasts, setToasts] = useState([])
      toastPush = (toast) => {
        const id = Math.random().toString(36).slice(2)
        setToasts((t) => [...t, { id, ...toast }])
        setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), toast.duration || 4500)
      }
      const icons = {
        success: <CheckCircle2 className="h-5 w-5 text-emerald-600" />,
        error: <XCircle className="h-5 w-5 text-red-600" />,
        warning: <AlertTriangle className="h-5 w-5 text-amber-500" />,
        info: <Info className="h-5 w-5 text-sky-600" />
      }
      return (
        <>
          {children}
          {createPortal(
            <div className="pointer-events-none fixed bottom-4 right-4 z-[100] flex w-96 max-w-[calc(100vw-2rem)] flex-col gap-2">
              {toasts.map((t) => (
                <div
                  key={t.id}
                  className="pointer-events-auto flex items-start gap-3 rounded-lg border border-slate-200 bg-white p-3.5 shadow-lg dark:border-slate-700 dark:bg-slate-900"
                  role="status"
                >
                  {icons[t.type] || icons.info}
                  <div className="flex-1 text-sm text-slate-700 dark:text-slate-200">{t.message}</div>
                  <button
                    className="text-slate-400 hover:text-slate-600"
                    onClick={() => setToasts((list) => list.filter((x) => x.id !== t.id))}
                    aria-label="Dismiss notification"
                  >
                    <X className="h-4 w-4" />
                  </button>
                </div>
              ))}
            </div>,
            document.body
          )}
        </>
      )
    }
  }
})()

export function toast(type, message, duration) {
  toastPush?.({ type, message, duration })
}
toast.success = (m) => toast('success', m)
toast.error = (m) => toast('error', m, 7000)
toast.warning = (m) => toast('warning', m)
toast.info = (m) => toast('info', m)

export function ToastProvider({ children }) {
  return <ToastContext.Provider>{children}</ToastContext.Provider>
}

// ---------- Modal ----------

export function Modal({ open, onClose, title, children, footer, size = 'md' }) {
  useEffect(() => {
    if (!open) return
    const onKey = (e) => e.key === 'Escape' && onClose?.()
    window.addEventListener('keydown', onKey)
    document.body.style.overflow = 'hidden'
    return () => {
      window.removeEventListener('keydown', onKey)
      document.body.style.overflow = ''
    }
  }, [open, onClose])

  if (!open) return null
  const widths = { sm: 'max-w-md', md: 'max-w-xl', lg: 'max-w-3xl', xl: 'max-w-5xl' }
  return createPortal(
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-slate-900/50 p-4 sm:items-center" role="dialog" aria-modal="true" aria-label={title}>
      <div className={`w-full ${widths[size]} rounded-xl bg-white shadow-xl dark:bg-slate-900`}>
        <div className="flex items-center justify-between border-b border-slate-200 px-5 py-3.5 dark:border-slate-800">
          <h2 className="text-base font-semibold text-slate-800 dark:text-slate-100">{title}</h2>
          <button onClick={onClose} className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600 dark:hover:bg-slate-800" aria-label="Close dialog">
            <X className="h-5 w-5" />
          </button>
        </div>
        <div className="px-5 py-4">{children}</div>
        {footer && <div className="flex justify-end gap-2 border-t border-slate-200 px-5 py-3.5 dark:border-slate-800">{footer}</div>}
      </div>
    </div>,
    document.body
  )
}

// ---------- Confirm dialog ----------

export function ConfirmDialog({ open, onClose, onConfirm, title, message, confirmLabel = 'Confirm', danger = false, busy = false }) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      footer={
        <>
          <button className="btn-secondary" onClick={onClose} disabled={busy}>Cancel</button>
          <button className={danger ? 'btn-danger' : 'btn-primary'} onClick={onConfirm} disabled={busy}>
            {busy && <Loader2 className="h-4 w-4 animate-spin" />}
            {confirmLabel}
          </button>
        </>
      }
      size="sm"
    >
      <p className="text-sm text-slate-600 dark:text-slate-300">{message}</p>
    </Modal>
  )
}

// ---------- Badge ----------

const badgeStyles = {
  green: 'bg-emerald-50 text-emerald-700 ring-emerald-600/20 dark:bg-emerald-900/30 dark:text-emerald-300',
  red: 'bg-red-50 text-red-700 ring-red-600/20 dark:bg-red-900/30 dark:text-red-300',
  amber: 'bg-amber-50 text-amber-700 ring-amber-600/20 dark:bg-amber-900/30 dark:text-amber-300',
  blue: 'bg-sky-50 text-sky-700 ring-sky-600/20 dark:bg-sky-900/30 dark:text-sky-300',
  gray: 'bg-slate-100 text-slate-600 ring-slate-500/20 dark:bg-slate-800 dark:text-slate-300',
  purple: 'bg-violet-50 text-violet-700 ring-violet-600/20 dark:bg-violet-900/30 dark:text-violet-300'
}

export function Badge({ color = 'gray', children }) {
  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset ${badgeStyles[color] || badgeStyles.gray}`}>
      {children}
    </span>
  )
}

// ---------- Skeleton ----------

export function Skeleton({ className = 'h-4 w-full' }) {
  return <div className={`animate-pulse rounded bg-slate-200 dark:bg-slate-800 ${className}`} />
}

export function TableSkeleton({ rows = 6, cols = 5 }) {
  return (
    <div className="space-y-2 p-4">
      {Array.from({ length: rows }).map((_, i) => (
        <div key={i} className="flex gap-4">
          {Array.from({ length: cols }).map((_, j) => (
            <Skeleton key={j} className="h-5 flex-1" />
          ))}
        </div>
      ))}
    </div>
  )
}

// ---------- Empty state ----------

export function EmptyState({ icon: Icon, title, description, action }) {
  return (
    <div className="flex flex-col items-center justify-center px-6 py-14 text-center">
      {Icon && <Icon className="mb-3 h-10 w-10 text-slate-300 dark:text-slate-600" strokeWidth={1.5} />}
      <h3 className="text-sm font-semibold text-slate-700 dark:text-slate-200">{title}</h3>
      {description && <p className="mt-1 max-w-sm text-sm text-slate-500 dark:text-slate-400">{description}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  )
}

// ---------- Error state ----------

export function ErrorState({ error, onRetry }) {
  return (
    <div className="flex flex-col items-center justify-center px-6 py-14 text-center">
      <XCircle className="mb-3 h-10 w-10 text-red-400" strokeWidth={1.5} />
      <h3 className="text-sm font-semibold text-slate-700 dark:text-slate-200">Something went wrong</h3>
      <p className="mt-1 max-w-md text-sm text-slate-500 dark:text-slate-400">
        {error?.message || 'An unexpected error occurred.'}
      </p>
      {onRetry && (
        <button className="btn-secondary mt-4" onClick={onRetry}>
          Try again
        </button>
      )}
    </div>
  )
}

// ---------- Pagination ----------

export const PAGE_SIZE_OPTIONS = [5, 10, 25, 50]
export const MIN_PAGE_SIZE = 5

export function Pagination({ page, pageSize, totalCount, onPage, onPageSize }) {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  if (totalCount === 0) return null
  const from = (page - 1) * pageSize + 1
  const to = Math.min(page * pageSize, totalCount)
  // Keep the selector usable even when the caller doesn't support changing page size.
  const sizeOptions = PAGE_SIZE_OPTIONS.includes(pageSize) ? PAGE_SIZE_OPTIONS : [...PAGE_SIZE_OPTIONS, pageSize].sort((a, b) => a - b)
  return (
    <div className="flex flex-col items-center justify-between gap-3 border-t border-slate-200 px-4 py-3 dark:border-slate-800 sm:flex-row">
      <div className="flex items-center gap-3">
        <p className="text-sm text-slate-500 dark:text-slate-400">
          Showing <span className="font-medium">{from}</span>–<span className="font-medium">{to}</span> of{' '}
          <span className="font-medium">{totalCount.toLocaleString()}</span>
        </p>
        {onPageSize && (
          <label className="flex items-center gap-2 text-sm text-slate-500 dark:text-slate-400">
            <span className="hidden sm:inline">Rows per page</span>
            <select
              className="rounded-md border border-slate-300 bg-white px-2 py-1 text-sm dark:border-slate-700 dark:bg-slate-900"
              value={pageSize}
              onChange={(e) => onPageSize(Number(e.target.value))}
              aria-label="Rows per page"
            >
              {sizeOptions.map((n) => (
                <option key={n} value={n}>{n}</option>
              ))}
            </select>
          </label>
        )}
      </div>
      <div className="flex gap-2">
        <button className="btn-secondary px-3 py-1.5" disabled={page <= 1} onClick={() => onPage(page - 1)}>
          Previous
        </button>
        <span className="btn-secondary pointer-events-none px-3 py-1.5" aria-live="polite">
          {page} / {totalPages}
        </span>
        <button className="btn-secondary px-3 py-1.5" disabled={page >= totalPages} onClick={() => onPage(page + 1)}>
          Next
        </button>
      </div>
    </div>
  )
}

// ---------- Page header ----------

export function PageHeader({ title, description, actions }) {
  return (
    <div className="mb-6 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
      <div>
        <h1 className="text-xl font-semibold text-slate-800 dark:text-slate-100">{title}</h1>
        {description && <p className="mt-0.5 text-sm text-slate-500 dark:text-slate-400">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  )
}

// ---------- Stat card ----------

export function StatCard({ title, value, sub, icon: Icon, tone = 'default', href, onClick }) {
  const tones = {
    default: 'text-slate-500 dark:text-slate-400',
    danger: 'text-red-500',
    warning: 'text-amber-500',
    success: 'text-emerald-500',
    info: 'text-sky-500'
  }
  const Wrapper = href ? 'a' : onClick ? 'button' : 'div'
  return (
    <Wrapper
      className="card block p-4 text-left transition-shadow hover:shadow-md"
      href={href}
      onClick={onClick}
      type={onClick ? 'button' : undefined}
    >
      <div className="flex items-center justify-between">
        <p className="text-sm font-medium text-slate-500 dark:text-slate-400">{title}</p>
        {Icon && <Icon className={`h-5 w-5 ${tones[tone]}`} strokeWidth={1.75} />}
      </div>
      <p className="mt-2 text-2xl font-semibold tabular-nums text-slate-800 dark:text-slate-100">{value}</p>
      {sub && <p className="mt-1 text-xs text-slate-400 dark:text-slate-500">{sub}</p>}
    </Wrapper>
  )
}

// ---------- Form field ----------

export function Field({ label, error, children, required, hint }) {
  return (
    <div>
      {label && (
        <label className="label">
          {label}
          {required && <span className="ml-0.5 text-red-500">*</span>}
        </label>
      )}
      {children}
      {hint && !error && <p className="mt-1 text-xs text-slate-400">{hint}</p>}
      {error && <p className="mt-1 text-xs text-red-600 dark:text-red-400">{error}</p>}
    </div>
  )
}

// ---------- Tabs ----------

export function Tabs({ tabs, active, onChange }) {
  return (
    <div className="border-b border-slate-200 dark:border-slate-800">
      <nav className="-mb-px flex gap-6 overflow-x-auto" aria-label="Tabs">
        {tabs.map((t) => (
          <button
            key={t.key}
            onClick={() => onChange(t.key)}
            className={`whitespace-nowrap border-b-2 px-1 py-2.5 text-sm font-medium transition-colors ${
              active === t.key
                ? 'border-accent-600 text-accent-600 dark:text-accent-400'
                : 'border-transparent text-slate-500 hover:border-slate-300 hover:text-slate-700 dark:text-slate-400'
            }`}
            aria-current={active === t.key ? 'page' : undefined}
          >
            {t.label}
          </button>
        ))}
      </nav>
    </div>
  )
}
