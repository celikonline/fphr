import {
  APP_LAUNCHER_ITEMS,
  NAV_FOOTER_ITEMS,
  NAV_ITEMS,
  SETTINGS_SECTIONS,
  type NavItem,
  type SettingsLink,
} from "./navigation";
import type { Gated } from "@/hooks/use-nav-visibility";

/**
 * senseik-052: App.tsx'teki <Route> tanımları path -> element eşliyor, yetki bilgisi taşımıyor. Menü
 * görünürlüğü için zaten var olan permission/accessSetting listeleri (NAV_ITEMS, NAV_FOOTER_ITEMS,
 * APP_LAUNCHER_ITEMS, SETTINGS_SECTIONS - `navigation.ts`, hot file, buradan sadece OKUNUYOR) burada
 * path -> Gated eşlemesine dönüştürülüyor, RouteGuard aynısını (useVisibleItems) URL'den direkt gelen
 * istek için de uygular. Yeni bir yetki listesi TANIMLANMIYOR - navigation.ts değişince bu harita da
 * otomatik güncel kalır.
 */
function toGate(item: Gated): Gated {
  return { permissions: item.permissions, accessSetting: item.accessSetting };
}

function isGated(item: Gated): boolean {
  return (item.permissions?.length ?? 0) > 0 || item.accessSetting !== undefined;
}

function collect(items: readonly NavItem[]): Array<[string, Gated]> {
  return items.filter(isGated).map((item) => [item.path, toGate(item)]);
}

function collectSettingsLinks(links: readonly SettingsLink[]): Array<[string, Gated]> {
  return links.filter(isGated).map((link) => [link.path, toGate(link)]);
}

const SETTINGS_LINKS = SETTINGS_SECTIONS.flatMap((section) => section.links);

/** path (App.tsx route'una eşit, örn. "/app/settings/system/users-roles") -> gerekli yetki/erişim ayarı. */
export const ROUTE_ACCESS: ReadonlyMap<string, Gated> = new Map([
  ...collect(NAV_ITEMS),
  ...collect(NAV_FOOTER_ITEMS),
  ...collect(APP_LAUNCHER_ITEMS),
  ...collectSettingsLinks(SETTINGS_LINKS),
]);

/** Rota için gerekli yetki/erişim ayarı - harita boşsa (kayıt yoksa) rota herkese açık kabul edilir. */
export function getRouteAccess(pathname: string): Gated | undefined {
  return ROUTE_ACCESS.get(pathname);
}
