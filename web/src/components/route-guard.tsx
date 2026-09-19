import { useMemo } from "react";
import { Outlet, useLocation } from "react-router";
import { useVisibleItems } from "@/hooks/use-nav-visibility";
import { getRouteAccess } from "@/config/route-permissions";
import NoAccess from "@/components/no-access";

/**
 * senseik-052: sol menü zaten kullanıcının yetkisine göre filtreleniyor (`useVisibleItems`,
 * `NAV_ITEMS`/`SETTINGS_SECTIONS`) ama sidebar'da görünmeyen bir rotaya adres çubuğundan direkt
 * gidilince sayfa kendi API çağrısıyla boş/bozuk bir ekran gösteriyordu. Çoğu sayfa zaten kendi
 * içinde `usePermission(...) + <NoAccess/>` deseniyle bunu ele alıyor (bkz. audit-log.tsx,
 * employee-list.tsx, work-schedules.tsx, ...) ama bu satır tek tek ~50 sayfaya dağılmış - bir kaçında
 * unutulmuş (`users-roles.tsx`, `locations.tsx`, `company-profile.tsx`, `employees/org-chart.tsx`),
 * o sayfalarda veri API tarafında korunsa da ekran boş/bozuk görünüyordu (1. Tur Test Bulgu
 * Dokümanı § E-04). RouteGuard AYNI görünürlük mekanizmasını (`useVisibleItems` + `NAV_ITEMS`/
 * `SETTINGS_SECTIONS`'tan türeyen `route-permissions.ts`) ve AYNI "erişim yok" ekranını (`NoAccess`)
 * rota seviyesinde uygulayarak bu tek tek unutulma riskini ortadan kaldırıyor - yeni bir yetki
 * kontrolü ya da farklı bir 403 ekranı icat etmiyor.
 *
 * `MfaSetupGuard`'ın Outlet'i yerine render edilir (bkz. mfa-setup-guard.tsx), böylece MFA
 * zorunluluğu kontrolünden SONRA, gerçek sayfa render edilmeden ÖNCE devreye girer.
 */
export default function RouteGuard() {
  const location = useLocation();
  // useVisibleItems tek elemanlı bir dizi bekliyor; kayıt yoksa boş bir Gated (`{}`) her zaman görünür sayılır -
  // NavItem'daki "empty = always visible" kuralıyla birebir aynı.
  const gate = useMemo(() => [getRouteAccess(location.pathname) ?? {}], [location.pathname]);
  const [visible] = useVisibleItems(gate);

  if (!visible) {
    return <NoAccess />;
  }

  return <Outlet />;
}
