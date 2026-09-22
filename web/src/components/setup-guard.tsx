import { Navigate, useLocation } from "react-router";
import { useAuthStore, type User } from "@/store/auth.store";
import { useImpersonatorId } from "@/hooks/use-access-setting";
import MfaSetupGuard from "@/components/mfa-setup-guard";
import { TENANT_MANAGE_PERMISSION } from "@/services/tenant-setup.service";

/** Kurulum sihirbazı: kabuk (sidebar/header) yerinde kalır, içerik alanı her rotada burayı gösterir. */
export const SETUP_PATH = "/app/setup";

/**
 * Yeni şirketin Owner'ı ilk girişte kurulum sihirbazını bitirmeden uygulamaya alınmaz. Yalnız kiracıyı yönetebilen
 * (identity.tenant.manage) kullanıcı için geçerlidir; destek erişimi (impersonation) oturumu ve eski backend'ler
 * (`setupCompleted` alanı yok) etkilenmez. `permissions` doğrudan okunur: `hasPermission` impersonation'da her şeye izin verir.
 */
export function isSetupPending(user: User | null, impersonatorId: string | null): boolean {
  if (!user || impersonatorId) return false;
  return (
    user.tenant?.setupCompleted === false && user.permissions.includes(TENANT_MANAGE_PERMISSION)
  );
}

/**
 * Sıra: kurulum → MFA → rota yetkisi. MFA zorunluysa önce hesap sayfasına yönlendirme kazanır (aksi halde
 * /app/account ↔ /app/setup arasında döngü olurdu); MFA kurulunca sihirbaza düşülür.
 */
export default function SetupGuard() {
  const user = useAuthStore((state) => state.user);
  const mfaSetupRequired = useAuthStore((state) => state.features?.mfaSetupRequired ?? false);
  const impersonatorId = useImpersonatorId();
  const location = useLocation();

  if (
    isSetupPending(user, impersonatorId) &&
    !mfaSetupRequired &&
    location.pathname !== SETUP_PATH
  ) {
    return <Navigate to={SETUP_PATH} replace />;
  }

  return <MfaSetupGuard />;
}
