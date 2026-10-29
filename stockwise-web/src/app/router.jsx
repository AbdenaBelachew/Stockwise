import { Navigate, Outlet, Route, Routes, useLocation } from 'react-router-dom'
import { AuthProvider, useAuth } from './providers.jsx'
import AppShell from '../components/layout/AppShell.jsx'

import LoginPage from '../pages/auth/LoginPage.jsx'
import RegisterPage from '../pages/auth/RegisterPage.jsx'
import ForgotPasswordPage from '../pages/auth/ForgotPasswordPage.jsx'
import ResetPasswordPage from '../pages/auth/ResetPasswordPage.jsx'
import AcceptInvitePage from '../pages/auth/AcceptInvitePage.jsx'

import DashboardPage from '../pages/DashboardPage.jsx'
import ProductsPage from '../pages/ProductsPage.jsx'
import ProductDetailPage from '../pages/ProductDetailPage.jsx'
import CategoriesPage from '../pages/CategoriesPage.jsx'
import WarehousesPage from '../pages/WarehousesPage.jsx'
import StockOnHandPage from '../pages/StockOnHandPage.jsx'
import MovementsPage from '../pages/MovementsPage.jsx'
import BatchesPage from '../pages/BatchesPage.jsx'
import AdjustmentsPage from '../pages/AdjustmentsPage.jsx'
import TransfersPage, { TransferDetailPage } from '../pages/TransfersPage.jsx'

import StockCountsPage, { StockCountDetailPage } from '../pages/StockCountsPage.jsx'

import SuppliersPage from '../pages/SuppliersPage.jsx'
import PurchaseOrdersPage from '../pages/PurchaseOrdersPage.jsx'
import PurchaseOrderDetailPage from '../pages/PurchaseOrderDetailPage.jsx'
import GoodsReceiptsPage, { GoodsReceiptDetailPage } from '../pages/GoodsReceiptsPage.jsx'

import ReportsPage from '../pages/ReportsPage.jsx'
import NotificationsPage from '../pages/NotificationsPage.jsx'
import TeamPage from '../pages/TeamPage.jsx'
import SettingsPage from '../pages/SettingsPage.jsx'
import ProfilePage from '../pages/ProfilePage.jsx'
import AuditLogsPage from '../pages/AuditLogsPage.jsx'
import NotFoundPage from '../pages/NotFoundPage.jsx'
import ForbiddenPage from '../pages/ForbiddenPage.jsx'

function Protected() {
  const { status } = useAuth()
  const location = useLocation()
  if (status === 'loading') {
    return (
      <div className="flex h-screen items-center justify-center bg-slate-50 dark:bg-slate-950">
        <div className="h-10 w-10 animate-spin rounded-full border-4 border-slate-200 border-t-accent-600" />
      </div>
    )
  }
  if (status === 'unauthenticated') {
    return <Navigate to="/login" replace state={{ from: location }} />
  }
  return <Outlet />
}

export default function Router() {
  return (
    <AuthProvider>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/register" element={<RegisterPage />} />
        <Route path="/forgot-password" element={<ForgotPasswordPage />} />
        <Route path="/reset-password" element={<ResetPasswordPage />} />
        <Route path="/accept-invitation" element={<AcceptInvitePage />} />

        <Route element={<Protected />}>
          <Route element={<AppShell />}>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/products" element={<ProductsPage />} />
            <Route path="/products/:id" element={<ProductDetailPage />} />
            <Route path="/categories" element={<CategoriesPage />} />
            <Route path="/warehouses" element={<WarehousesPage />} />
            <Route path="/stock-on-hand" element={<StockOnHandPage />} />
            <Route path="/movements" element={<MovementsPage />} />
            <Route path="/batches" element={<BatchesPage />} />
            <Route path="/adjustments" element={<AdjustmentsPage />} />
            <Route path="/transfers" element={<TransfersPage />} />
            <Route path="/transfers/:id" element={<TransferDetailPage />} />
            <Route path="/stock-counts" element={<StockCountsPage />} />
            <Route path="/stock-counts/:id" element={<StockCountDetailPage />} />
            <Route path="/suppliers" element={<SuppliersPage />} />
            <Route path="/purchase-orders" element={<PurchaseOrdersPage />} />
            <Route path="/purchase-orders/:id" element={<PurchaseOrderDetailPage />} />
            <Route path="/goods-receipts" element={<GoodsReceiptsPage />} />
            <Route path="/goods-receipts/:id" element={<GoodsReceiptDetailPage />} />
            <Route path="/reports" element={<ReportsPage />} />
            <Route path="/notifications" element={<NotificationsPage />} />
            <Route path="/team" element={<TeamPage />} />
            <Route path="/settings" element={<SettingsPage />} />
            <Route path="/settings/profile" element={<ProfilePage />} />
            <Route path="/audit-logs" element={<AuditLogsPage />} />
          </Route>
        </Route>

        <Route path="/403" element={<ForbiddenPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </AuthProvider>
  )
}
