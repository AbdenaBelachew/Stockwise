import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Settings as SettingsIcon, Save } from 'lucide-react'
import { api, hasPermission } from '../lib/api-client.js'
import { P } from '../lib/permissions.js'
import { PageHeader, Field, TableSkeleton, ErrorState, toast } from '../components/ui/index.jsx'

export default function SettingsPage() {
  const queryClient = useQueryClient()
  const settings = useQuery({ queryKey: ['settings'], queryFn: () => api.get('/api/v1/settings') })
  const [form, setForm] = useState(null)
  const [busy, setBusy] = useState(false)
  const canManage = hasPermission(P.OrgSettingsManage)

  const value = form || settings.data
  const set = (key, v) => setForm({ ...(value || {}), [key]: v })

  const save = async () => {
    setBusy(true)
    try {
      await api.patch('/api/v1/settings', {
        timeZoneId: form?.timeZoneId,
        currencyCode: form?.currencyCode,
        dateFormat: form?.dateFormat,
        expiryAlertDays: form?.expiryAlertDays ? Number(form.expiryAlertDays) : undefined,
        allowsNegativeStock: form?.allowsNegativeStock,
        requireSeparationOfDuties: form?.requireSeparationOfDuties,
        approvalThresholdAmount: form?.approvalThresholdAmount != null && form.approvalThresholdAmount !== '' ? Number(form.approvalThresholdAmount) : 0,
        allowOverReceipt: form?.allowOverReceipt,
        overReceiptTolerancePercent: form?.overReceiptTolerancePercent != null ? Number(form.overReceiptTolerancePercent) : undefined,
        defaultTrackBatches: form?.defaultTrackBatches,
        defaultTrackExpiry: form?.defaultTrackExpiry,
        minShelfLifeDays: form?.minShelfLifeDays != null ? Number(form.minShelfLifeDays) : undefined
      })
      toast.success('Settings saved.')
      setForm(null)
      queryClient.invalidateQueries({ queryKey: ['settings'] })
    } catch (e) {
      toast.error(e.message)
    } finally {
      setBusy(false)
    }
  }

  if (settings.isLoading) return <TableSkeleton rows={8} cols={2} />
  if (settings.isError) return <ErrorState error={settings.error} onRetry={settings.refetch} />

  return (
    <div>
      <PageHeader
        title="Organization settings"
        description="Preferences, alert thresholds and inventory policies. Changes apply tenant-wide."
        actions={canManage && (
          <button className="btn-primary" onClick={save} disabled={busy || !form}>
            <Save className="h-4 w-4" /> Save changes
          </button>
        )}
      />

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <div className="card p-5">
          <h2 className="mb-4 flex items-center gap-2 text-sm font-semibold"><SettingsIcon className="h-4 w-4" /> Profile & preferences</h2>
          <div className="space-y-4">
            <Field label="Organization name"><input className="input" value={value?.name || ''} disabled /></Field>
            <Field label="Country"><input className="input" value={value?.country || ''} disabled /></Field>
            <Field label="Time zone" hint="Used for expiry date calculations and daily reporting buckets.">
              <input className="input" value={value?.timeZoneId || ''} disabled={!canManage} onChange={(e) => set('timeZoneId', e.target.value)} />
            </Field>
            <Field label="Currency (ISO 4217)"><input className="input" maxLength={3} value={value?.currencyCode || ''} disabled={!canManage} onChange={(e) => set('currencyCode', e.target.value)} /></Field>
            <Field label="Date format">
              <select className="input" value={value?.dateFormat || 'yyyy-MM-dd'} disabled={!canManage} onChange={(e) => set('dateFormat', e.target.value)}>
                <option value="yyyy-MM-dd">2026-10-28</option>
                <option value="dd/MM/yyyy">28/10/2026</option>
                <option value="dd.MM.yyyy">28.10.2026</option>
                <option value="MM/dd/yyyy">10/28/2026</option>
              </select>
            </Field>
          </div>
        </div>

        <div className="card p-5">
          <h2 className="mb-4 text-sm font-semibold">Inventory policies</h2>
          <div className="space-y-4">
            <Field label="Expiry alert window (days)" hint="Batches within this many days of expiry raise warnings.">
              <input type="number" min="1" max="365" className="input" value={value?.expiryAlertDays ?? 30} disabled={!canManage} onChange={(e) => set('expiryAlertDays', e.target.value)} />
            </Field>
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" className="h-4 w-4" checked={!!value?.allowsNegativeStock} disabled={!canManage}
                onChange={(e) => set('allowsNegativeStock', e.target.checked)} />
              Allow negative stock (audited overselling)
            </label>
            <Field label="Min. shelf life default (days)" hint="Reject receiving batches with less remaining life.">
              <input type="number" min="0" className="input" value={value?.minShelfLifeDays ?? 0} disabled={!canManage} onChange={(e) => set('minShelfLifeDays', e.target.value)} />
            </Field>
            <div className="flex gap-6">
              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" className="h-4 w-4" checked={!!value?.defaultTrackBatches} disabled={!canManage}
                  onChange={(e) => set('defaultTrackBatches', e.target.checked)} />
                Track batches by default
              </label>
              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" className="h-4 w-4" checked={!!value?.defaultTrackExpiry} disabled={!canManage}
                  onChange={(e) => set('defaultTrackExpiry', e.target.checked)} />
                Track expiry by default
              </label>
            </div>
          </div>
        </div>

        <div className="card p-5 lg:col-span-2">
          <h2 className="mb-4 text-sm font-semibold">Procurement policies</h2>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Field label="Approval threshold" hint="Orders at or below this total auto-approve on submit. 0 = always require approval.">
              <input type="number" min="0" step="0.01" className="input" value={value?.approvalThresholdAmount ?? ''} disabled={!canManage} onChange={(e) => set('approvalThresholdAmount', e.target.value)} />
            </Field>
            <div className="flex flex-col justify-center gap-2">
              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" className="h-4 w-4" checked={!!value?.requireSeparationOfDuties} disabled={!canManage}
                  onChange={(e) => set('requireSeparationOfDuties', e.target.checked)} />
                Require separation of duties (creators cannot approve)
              </label>
              <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" className="h-4 w-4" checked={!!value?.allowOverReceipt} disabled={!canManage}
                  onChange={(e) => set('allowOverReceipt', e.target.checked)} />
                Allow over-receipt within tolerance
              </label>
            </div>
            <Field label="Over-receipt tolerance (%)">
              <input type="number" min="0" max="100" className="input" value={value?.overReceiptTolerancePercent ?? 10} disabled={!canManage} onChange={(e) => set('overReceiptTolerancePercent', e.target.value)} />
            </Field>
          </div>
          <p className="mt-4 text-xs text-slate-400">
            Inventory is a controlled ledger: stock changes only through audited movements, receipts are idempotent,
            and posted movements are immutable at the database level.
          </p>
        </div>
      </div>
    </div>
  )
}
