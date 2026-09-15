import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { AppWindow } from "lucide-react";
import { APP_LAUNCHER_ITEMS } from "@/config/navigation";
import { useVisibleItems } from "@/hooks/use-nav-visibility";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";

const ACTIVE_APPS = APP_LAUNCHER_ITEMS.filter((item) => item.phase === undefined);
const PLANNED_APPS = APP_LAUNCHER_ITEMS.filter((item) => item.phase !== undefined);

/** "Uygulamalar" launcher: available apps link out; later-phase apps are listed disabled with their phase. */
export default function AppsPage() {
  const { t } = useTranslation(["navigation"]);
  const activeApps = useVisibleItems(ACTIVE_APPS);

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-6 md:p-10">
      <div>
        <h1 className="flex items-center gap-2 text-2xl font-bold">
          <AppWindow className="h-6 w-6" /> {t("navigation:nav.apps")}
        </h1>
        <p className="text-muted-foreground">{t("navigation:apps.subtitle")}</p>
      </div>

      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
        {activeApps.map((item) => {
          const Icon = item.icon;
          return (
            <Link key={item.key} to={item.path} className="group">
              <Card className="h-full transition-colors group-hover:border-primary">
                <CardContent className="flex flex-col items-center gap-3 p-6 text-center">
                  <Icon className="h-8 w-8 text-primary" />
                  <span className="text-sm font-medium">{t(item.labelKey)}</span>
                </CardContent>
              </Card>
            </Link>
          );
        })}
        {PLANNED_APPS.map((item) => {
          const Icon = item.icon;
          return (
            <Card key={item.key} aria-disabled className="h-full opacity-60">
              <CardContent className="flex flex-col items-center gap-3 p-6 text-center">
                <Icon className="h-8 w-8 text-muted-foreground" />
                <span className="text-sm font-medium">{t(item.labelKey)}</span>
                <Badge variant="outline">{t("navigation:phase", { phase: item.phase })}</Badge>
              </CardContent>
            </Card>
          );
        })}
      </div>
    </div>
  );
}
