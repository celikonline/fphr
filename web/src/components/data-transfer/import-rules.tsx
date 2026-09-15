import { useTranslation } from "react-i18next";
import { Info } from "lucide-react";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";

const RULE_KEYS = ["headers", "deleteColumns", "dates", "lookups", "matching"] as const;

/** Item 8 template rules shown next to the template cards. */
export default function ImportRules() {
  const { t } = useTranslation(["dataTransfer"]);

  return (
    <Alert>
      <Info className="h-4 w-4" />
      <AlertTitle>{t("dataTransfer:rules.title")}</AlertTitle>
      <AlertDescription>
        <ul className="mt-2 list-disc space-y-1 pl-4">
          {RULE_KEYS.map((key) => (
            <li key={key}>{t(`dataTransfer:rules.${key}`)}</li>
          ))}
        </ul>
      </AlertDescription>
    </Alert>
  );
}
