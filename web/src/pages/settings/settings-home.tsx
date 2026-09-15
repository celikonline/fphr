import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { ChevronRight, Settings } from "lucide-react";
import { SETTINGS_SECTIONS, type SettingsLink, type SettingsSection } from "@/config/navigation";
import { useVisibleItems } from "@/hooks/use-nav-visibility";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

function SettingsLinkRow({ link }: { link: SettingsLink }) {
  const { t } = useTranslation(["navigation"]);
  const Icon = link.icon;

  return (
    <Link
      to={link.path}
      className="flex items-center gap-3 rounded-md px-3 py-3 transition-colors hover:bg-accent hover:text-accent-foreground"
    >
      <Icon className="h-5 w-5 shrink-0 text-primary" />
      <div className="min-w-0 flex-1">
        <p className="flex items-center gap-2 text-sm font-medium">
          {t(link.labelKey)}
          {link.phase !== undefined && (
            <Badge variant="outline" className="px-1.5 py-0 text-[10px]">
              {t("navigation:phase", { phase: link.phase })}
            </Badge>
          )}
        </p>
        <p className="truncate text-xs text-muted-foreground">{t(link.descriptionKey)}</p>
      </div>
      <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" />
    </Link>
  );
}

function SettingsSectionCard({ section }: { section: SettingsSection }) {
  const { t } = useTranslation(["navigation"]);
  const links = useVisibleItems(section.links);
  if (links.length === 0) return null;

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="text-base">{t(section.labelKey)}</CardTitle>
      </CardHeader>
      <CardContent className="space-y-1 px-3 pb-3">
        {links.map((link) => (
          <SettingsLinkRow key={link.key} link={link} />
        ))}
      </CardContent>
    </Card>
  );
}

/** Ayarlar hub: Şirket bilgileri / Kurallar / Sistem ayarları / Veri aktar, each link permission-gated. */
export default function SettingsHomePage() {
  const { t } = useTranslation(["settings"]);

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-6 md:p-10">
      <div>
        <h1 className="flex items-center gap-2 text-2xl font-bold">
          <Settings className="h-6 w-6" /> {t("settings:hub.title")}
        </h1>
        <p className="text-muted-foreground">{t("settings:hub.subtitle")}</p>
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {SETTINGS_SECTIONS.map((section) => (
          <SettingsSectionCard key={section.key} section={section} />
        ))}
      </div>
    </div>
  );
}
