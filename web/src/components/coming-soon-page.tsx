import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { Hourglass } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

interface ComingSoonPageProps {
  /** i18n key in the "navigation" namespace for the page title. */
  titleKey: string;
  /** Planned roadmap phase ("Faz N"). */
  phase: number;
  /** Optional sub-features (navigation i18n keys) listed as planned. */
  itemKeys?: string[];
}

/** Placeholder for menu entries whose module ships in a later roadmap phase. */
export default function ComingSoonPage({ titleKey, phase, itemKeys }: ComingSoonPageProps) {
  const { t } = useTranslation(["navigation", "common"]);

  return (
    <div className="mx-auto max-w-3xl p-6 md:p-10">
      <Card>
        <CardHeader className="items-center text-center">
          <div className="mb-2 flex h-12 w-12 items-center justify-center rounded-full bg-muted">
            <Hourglass className="h-6 w-6 text-muted-foreground" />
          </div>
          <CardTitle className="flex items-center gap-2 text-xl">
            {t(titleKey)}
            <Badge variant="secondary">{t("navigation:phase", { phase })}</Badge>
          </CardTitle>
          <CardDescription>{t("navigation:comingSoon.description", { phase })}</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col items-center gap-4">
          {itemKeys && itemKeys.length > 0 && (
            <ul className="w-full max-w-sm space-y-2">
              {itemKeys.map((key) => (
                <li key={key} className="flex items-center justify-between rounded-md border border-border px-3 py-2 text-sm">
                  <span>{t(key)}</span>
                  <Badge variant="outline">{t("navigation:phase", { phase })}</Badge>
                </li>
              ))}
            </ul>
          )}
          <Button asChild variant="outline">
            <Link to="/app">{t("common:backHome")}</Link>
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}
