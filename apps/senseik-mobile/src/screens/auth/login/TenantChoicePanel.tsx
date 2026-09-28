import React from "react";
import { Text, TouchableOpacity } from "react-native";
import { useTranslation } from "react-i18next";
import { TenantChoice } from "@/types";
import { AuthPanel as Panel } from "@/components/auth/AuthPanel";
import { authStyles } from "@/components/auth/authStyles";
import { TenantChoiceList } from "./TenantChoiceList";

type Props = {
  tenants: TenantChoice[];
  onSelect: (tenant: TenantChoice) => void;
  isLoading: boolean;
  onCancel: () => void;
};

/** Login's company-select step panel: title/subtitle, tenant rows, and the cancel link back to the
 * credentials form. Presentation only - LoginScreen owns the logic. */
export function TenantChoicePanel({ tenants, onSelect, isLoading, onCancel }: Props) {
  const { t } = useTranslation("auth");
  return (
    <Panel>
      <Text style={authStyles.panelTitle}>{t("tenant.title")}</Text>
      <Text style={authStyles.panelSubtitle}>{t("tenant.subtitle")}</Text>
      <TenantChoiceList tenants={tenants} onSelect={onSelect} isLoading={isLoading} />
      <TouchableOpacity
        onPress={onCancel}
        disabled={isLoading}
        style={authStyles.secondaryLink}
        accessibilityRole="button"
        accessibilityLabel={t("login.switchAccount")}
      >
        <Text style={authStyles.secondaryLinkText}>{t("login.switchAccount")}</Text>
      </TouchableOpacity>
    </Panel>
  );
}
