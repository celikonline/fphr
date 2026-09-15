import { useTranslation } from "react-i18next";
import { Alert, List } from "@mantine/core";
import { Info } from "lucide-react";

const RULE_KEYS = ["headers", "deleteColumns", "dates", "lookups", "matching"] as const;

/** Item 8 template rules shown next to the template cards. */
export default function ImportRules() {
  const { t } = useTranslation(["dataTransfer"]);

  return (
    <Alert variant="light" color="gray" icon={<Info size={16} />} title={t("dataTransfer:rules.title")}>
      <List size="sm" spacing={4} mt={4}>
        {RULE_KEYS.map((key) => (
          <List.Item key={key}>{t(`dataTransfer:rules.${key}`)}</List.Item>
        ))}
      </List>
    </Alert>
  );
}
