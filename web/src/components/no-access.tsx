import { useTranslation } from "react-i18next";
import { Lock } from "lucide-react";
import { Card, CardContent } from "@/components/ui/card";

/** Shown in place of a page the current user has no permission for. */
export default function NoAccess() {
  const { t } = useTranslation(["common"]);

  return (
    <div className="mx-auto max-w-3xl p-6 md:p-10">
      <Card>
        <CardContent className="flex flex-col items-center gap-2 p-10 text-center">
          <Lock className="h-8 w-8 text-muted-foreground" />
          <p className="font-medium">{t("common:noAccessTitle")}</p>
          <p className="text-sm text-muted-foreground">{t("common:noAccessDescription")}</p>
        </CardContent>
      </Card>
    </div>
  );
}
