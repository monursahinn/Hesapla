import { lazy, useCallback, useMemo, useState, type ReactNode } from "react";
import { MantineProvider } from "@mantine/core";
import { DatesProvider } from "@mantine/dates";
import { ModalsProvider } from "@mantine/modals";
import { Notifications } from "@mantine/notifications";
import { createBrowserRouter, Navigate, Outlet, RouterProvider, useLocation } from "react-router";
import { AuthProvider, useAuth, type RoleCode } from "@/auth/AuthProvider";
import { AppLayout } from "@/layout/AppLayout";
import { AppSplash } from "@/components/AppSplash";
import { LoginPage } from "@/pages/auth/LoginPage";
import { RegisterPage } from "@/pages/auth/RegisterPage";
import { ForgotPasswordPage, ResetPasswordPage } from "@/pages/auth/PasswordPages";
import { NotFoundPage } from "@/pages/NotFoundPage";
import { buildTheme, cssResolver, themeDef } from "@/theme";
import { ThemePrefContext, type ThemePref } from "@/themePreference";

// Sayfalar ihtiyaç anında yüklenir (ilk açılış hızlı kalır).
const DashboardPage = lazy(() => import("@/pages/dashboard/DashboardPage"));
const CustomersPage = lazy(() => import("@/pages/customers/CustomersPage"));
const CustomerDetailPage = lazy(() => import("@/pages/customers/CustomerDetailPage"));
const ProductsPage = lazy(() => import("@/pages/products/ProductsPage"));
const InvoicesPage = lazy(() => import("@/pages/invoices/InvoicesPage"));
const InvoiceEditorPage = lazy(() => import("@/pages/invoices/InvoiceEditorPage"));
const InvoiceDetailPage = lazy(() => import("@/pages/invoices/InvoiceDetailPage"));
const CashBankPage = lazy(() => import("@/pages/cash/CashBankPage"));
const ReportsPage = lazy(() => import("@/pages/reports/ReportsPage"));
const SettingsPage = lazy(() => import("@/pages/settings/SettingsPage"));
const AuditPage = lazy(() => import("@/pages/audit/AuditPage"));
const PlatformPage = lazy(() => import("@/pages/platform/PlatformPage"));

/** Oturum yoksa girişe yönlendirir. */
function RequireAuth() {
  const { me, loading } = useAuth();
  const location = useLocation();

  if (loading) return <AppSplash />;
  if (!me) return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  return <AppLayout />;
}

/** Rolün göremediği adrese doğrudan gidilirse kendi anasayfasına döner. */
function Only({ roles, children }: { roles: RoleCode[]; children: ReactNode }) {
  const { hasRole } = useAuth();
  return hasRole(...roles) ? <>{children}</> : <Navigate to="/" replace />;
}

/** Platform yöneticisinin anasayfası firma yönetimidir; firma kullanıcılarınınki pano. */
function Home() {
  const { hasRole } = useAuth();
  return hasRole("SU") ? <PlatformPage /> : <DashboardPage />;
}

const FIRM: RoleCode[] = ["AD", "AC", "VW"];
const WRITER: RoleCode[] = ["AD", "AC"];

const router = createBrowserRouter([
  {
    element: (
      <AuthProvider>
        <Outlet />
      </AuthProvider>
    ),
    children: [
      { path: "/login", element: <LoginPage /> },
      { path: "/register", element: <RegisterPage /> },
      { path: "/forgot-password", element: <ForgotPasswordPage /> },
      { path: "/reset-password", element: <ResetPasswordPage /> },
      {
        element: <RequireAuth />,
        children: [
          { index: true, element: <Home /> },
          { path: "customers", element: <Only roles={FIRM}><CustomersPage /></Only> },
          { path: "customers/:id", element: <Only roles={FIRM}><CustomerDetailPage /></Only> },
          { path: "products", element: <Only roles={FIRM}><ProductsPage /></Only> },
          { path: "invoices", element: <Only roles={FIRM}><InvoicesPage /></Only> },
          { path: "invoices/new", element: <Only roles={WRITER}><InvoiceEditorPage /></Only> },
          { path: "invoices/:id", element: <Only roles={FIRM}><InvoiceDetailPage /></Only> },
          { path: "invoices/:id/edit", element: <Only roles={WRITER}><InvoiceEditorPage /></Only> },
          { path: "cash", element: <Only roles={FIRM}><CashBankPage /></Only> },
          { path: "reports", element: <Only roles={FIRM}><ReportsPage /></Only> },
          { path: "settings", element: <SettingsPage /> },
          { path: "audit", element: <Only roles={["AD"]}><AuditPage /></Only> },
          { path: "*", element: <NotFoundPage /> },
        ],
      },
    ],
  },
]);

function readStoredTheme() {
  try {
    return localStorage.getItem("hesap.theme") ?? "Light";
  } catch {
    return "Light";
  }
}

export function App() {
  const [pref, setPref] = useState(() => themeDef(readStoredTheme()).value);
  const def = themeDef(pref);
  const theme = useMemo(() => buildTheme(def), [def]);
  const resolver = useMemo(() => cssResolver(def), [def]);

  const apply = useCallback((p: string) => {
    try {
      localStorage.setItem("hesap.theme", p);
    } catch {
      /* yok say */
    }
    setPref(p);
  }, []);
  const ctx = useMemo<ThemePref>(() => ({ pref, apply }), [pref, apply]);

  return (
    <ThemePrefContext.Provider value={ctx}>
      <MantineProvider theme={theme} forceColorScheme={def.scheme} cssVariablesResolver={resolver}>
        <DatesProvider settings={{ locale: "tr", firstDayOfWeek: 1 }}>
          <ModalsProvider labels={{ confirm: "Onayla", cancel: "Vazgeç" }}>
            <Notifications position="top-right" limit={4} containerWidth={380} />
            <RouterProvider router={router} />
          </ModalsProvider>
        </DatesProvider>
      </MantineProvider>
    </ThemePrefContext.Provider>
  );
}
