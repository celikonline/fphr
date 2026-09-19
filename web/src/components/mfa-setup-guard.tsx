import { Navigate, Outlet, useLocation } from "react-router";
import { useAuthStore } from "@/store/auth.store";

/** Kendi hesabım: MFA kurulum kartı burada (bkz. src/pages/account.tsx), tek istisna yolu. */
const MFA_SETUP_PATH = "/app/account";

/**
 * senseik-051: tenant güvenlik politikası (Ayarlar > Sistem ayarları > Güvenlik politikası) çağıranın rolü için
 * 2FA'yı zorunlu kılıyor ama kullanıcı henüz açmamışsa (`features.mfaSetupRequired`, GetMeHandler'da hesaplanır),
 * uygulama kabuğu (sidebar/header) yerinde kalır ama içerik alanı her rotada hesap sayfasına yönlendirilir — orada
 * MFA kurulum akışı (Etkinleştir → QR/secret → kod doğrulama) zaten var, ayrı bir ekran gerekmez.
 */
export default function MfaSetupGuard() {
  const mfaSetupRequired = useAuthStore((state) => state.features?.mfaSetupRequired ?? false);
  const location = useLocation();

  if (mfaSetupRequired && location.pathname !== MFA_SETUP_PATH) {
    return <Navigate to={MFA_SETUP_PATH} replace />;
  }

  return <Outlet />;
}
