import { TableSkeleton, EmptyState, ErrorState, Pagination } from '../ui/index.jsx'

/**
 * Standard data table used by all list pages.
 *
 * columns: [{ key, header, align: 'left'|'right'|'center', width, className, headerClassName, render(row) }]
 * rows:    array of objects (must have a stable key field)
 * Standardized: card shell, sticky header, hover rows, tabular numbers for
 * right-aligned cells, built-in loading / error / empty / pagination states.
 */
export function DataTable({
  columns,
  rows,
  keyField = 'id',
  loading = false,
  error = null,
  onRetry,
  empty = {},
  page,
  pageSize,
  totalCount,
  onPage,
  onPageSize,
  minWidth = 'min-w-[640px]',
  onRowClick,
  footer
}) {
  return (
    <div className="card overflow-hidden">
      {loading ? (
        <TableSkeleton rows={8} cols={Math.min(columns.length, 6)} />
      ) : error ? (
        <ErrorState error={error} onRetry={onRetry} />
      ) : !rows || rows.length === 0 ? (
        <EmptyState {...empty} />
      ) : (
        <>
          <div className="overflow-x-auto">
            <table className={`w-full ${minWidth}`}>
              <thead className="sticky top-0 z-10 bg-slate-50/95 backdrop-blur dark:bg-slate-800/95">
                <tr className="border-b border-slate-200 dark:border-slate-700">
                  {columns.map((c) => (
                    <th
                      key={c.key}
                      scope="col"
                      className={`th whitespace-nowrap ${alignClass(c.align)} ${c.headerClassName || ''}`}
                      style={c.width ? { width: c.width } : undefined}
                    >
                      {c.header}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map((row, i) => (
                  <tr
                    key={row[keyField] ?? i}
                    className={`table-row ${onRowClick ? 'cursor-pointer' : ''}`}
                    onClick={onRowClick ? () => onRowClick(row) : undefined}
                  >
                    {columns.map((c) => (
                      <td
                        key={c.key}
                        className={`td align-middle ${alignClass(c.align)} ${c.className || ''}`}
                      >
                        {c.render ? c.render(row) : formatDefault(row[c.key])}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {footer}
          {page !== undefined && totalCount !== undefined && (
            <Pagination
              page={page}
              pageSize={pageSize}
              totalCount={Number(totalCount)}
              onPage={onPage}
              onPageSize={onPageSize}
            />
          )}
        </>
      )}
    </div>
  )
}

function alignClass(align) {
  if (align === 'right') return 'text-right'
  if (align === 'center') return 'text-center'
  return 'text-left'
}

function formatDefault(value) {
  if (value === null || value === undefined) return '—'
  if (typeof value === 'boolean') return value ? 'Yes' : 'No'
  if (typeof value === 'number') return value.toLocaleString(undefined, { maximumFractionDigits: 4 })
  return String(value)
}

/** Toolbar row rendered above a DataTable (search + filters + primary actions). */
export function TableToolbar({ children }) {
  return (
    <div className="flex flex-col gap-3 border-b border-slate-100 p-4 dark:border-slate-800 lg:flex-row lg:items-center">
      {children}
    </div>
  )
}
