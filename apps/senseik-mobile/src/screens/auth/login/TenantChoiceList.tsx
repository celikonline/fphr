import React from "react";
import { ActivityIndicator, StyleSheet, Text, TouchableOpacity, View } from "react-native";
import { TenantChoice } from "@/types";
import { fontFor, palette } from "@/theme/theme";

type Props = {
  tenants: TenantChoice[];
  onSelect: (tenant: TenantChoice) => void;
  isLoading: boolean;
};

/** Login's company-select step rows: initial badge, company name, chevron; spinner while loading. */
export function TenantChoiceList({ tenants, onSelect, isLoading }: Props) {
  return (
    <>
      {tenants.map((tenant) => (
        <TouchableOpacity
          key={tenant.tenantId}
          style={styles.tenantRow}
          onPress={() => onSelect(tenant)}
          disabled={isLoading}
          accessibilityRole="button"
          accessibilityLabel={tenant.name}
        >
          <View style={styles.tenantBadge}>
            <Text style={styles.tenantBadgeText}>
              {tenant.name.charAt(0).toLocaleUpperCase("tr")}
            </Text>
          </View>
          <Text style={styles.tenantName}>{tenant.name}</Text>
          <Text style={styles.tenantChevron}>›</Text>
        </TouchableOpacity>
      ))}
      {isLoading && <ActivityIndicator color={palette.brand} style={styles.tenantLoading} />}
    </>
  );
}

const styles = StyleSheet.create({
  tenantRow: { marginTop: 16, minHeight: 44, flexDirection: "row", alignItems: "center", gap: 12 },
  tenantBadge: {
    width: 40,
    height: 40,
    borderRadius: 10,
    backgroundColor: palette.navy,
    alignItems: "center",
    justifyContent: "center",
  },
  tenantBadgeText: { fontSize: 16, color: palette.white, ...fontFor(700) },
  tenantName: { flex: 1, fontSize: 16, color: palette.white, ...fontFor(600) },
  tenantChevron: { fontSize: 18, color: palette.ice },
  tenantLoading: { marginTop: 16 },
});
