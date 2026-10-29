export function formatMoney(value, currency = 'USD') {
  if (value === null || value === undefined) return '—'
  try {
    return new Intl.NumberFormat(undefined, { style: 'currency', currency, maximumFractionDigits: 2 }).format(value)
  } catch {
    return `${Number(value).toFixed(2)} ${currency}`
  }
}

export function formatQty(value, unitSymbol = '') {
  if (value === null || value === undefined) return '—'
  const n = Number(value)
  const formatted = Number.isInteger(n) ? n.toLocaleString() : n.toLocaleString(undefined, { maximumFractionDigits: 3 })
  return unitSymbol ? `${formatted} ${unitSymbol}` : formatted
}

export function formatDate(value, dateFormat = 'yyyy-MM-dd') {
  if (!value) return '—'
  const d = typeof value === 'string' ? new Date(value) : value
  if (Number.isNaN(d.getTime())) return '—'
  // Map common backend formats to Intl options (dates arrive ISO from the API).
  const map = {
    'yyyy-MM-dd': { year: 'numeric', month: 'short', day: 'numeric' },
    'dd/MM/yyyy': { day: '2-digit', month: '2-digit', year: 'numeric' },
    'dd.MM.yyyy': { day: '2-digit', month: '2-digit', year: 'numeric' },
    'MM/dd/yyyy': { month: '2-digit', day: '2-digit', year: 'numeric' }
  }
  return d.toLocaleDateString(undefined, map[dateFormat] || map['yyyy-MM-dd'])
}

export function formatDateTime(value, dateFormat = 'yyyy-MM-dd') {
  if (!value) return '—'
  const d = typeof value === 'string' ? new Date(value) : value
  if (Number.isNaN(d.getTime())) return '—'
  return `${formatDate(d, dateFormat)}, ${d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })}`
}

export function todayIso() {
  return new Date().toISOString().slice(0, 10)
}

export function toIsoDateOrNull(value) {
  if (!value) return null
  return new Date(value).toISOString()
}
