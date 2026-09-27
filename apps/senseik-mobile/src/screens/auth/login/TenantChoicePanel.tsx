import React from "react";
import { Text, TouchableOpacity } from "react-native";
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
  return (
    <Panel>
      <Text style={authStyles.panelTitle}>Şirket seçin</Text>
      <Text style={authStyles.panelSubtitle}>
        Bu e-posta adresi birden fazla şirkete kayıtlı. Devam etmek için bir şirket seçin.
      </Text>
      <TenantChoiceList tenants={tenants} onSelect={onSelect} isLoading={isLoading} />
      <TouchableOpacity
        onPress={onCancel}
        disabled={isLoading}
        style={authStyles.secondaryLink}
        accessibilityRole="button"
        accessibilityLabel="Farklı hesapla giriş yap"
      >
        <Text style={authStyles.secondaryLinkText}>Farklı hesapla giriş yap</Text>
      </TouchableOpacity>
    </Panel>
  );
}
