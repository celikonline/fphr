import { useTranslation } from "react-i18next";
import { useQuery } from "@tanstack/react-query";
import { Alert } from "@mantine/core";
import { AlertTriangle } from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import { getEmployeeAssets } from "@/services/assets.service";

/**
 * Offboarding <-> zimmet (roadmap): advisory-only open-asset count for an employee, meant to be dropped into an
 * exit/termination dialog with a single line. Never blocks the flow - HR sees the count up front and records the
 * return separately from the employee's "Zimmet" tab.
 */
export function OpenAssetsWarning({ employeeId }: { employeeId: string }) {
  const { t } = useTranslation(["employees"]);
  const canViewAssets = usePermission("assets.assets.view");
  const { data: assets = [] } = useQuery({
    queryKey: ["assets", "employee-assets", employeeId],
    queryFn: () => getEmployeeAssets(employeeId),
    enabled: canViewAssets,
  });
  const openAssetCount = assets.filter((asset) => asset.status === "assigned").length;

  if (openAssetCount === 0) return null;

  return (
    <Alert color="orange" icon={<AlertTriangle size={16} />} title={t("employees:lifecycle.terminate.openAssetsTitle")}>
      {t("employees:lifecycle.terminate.openAssetsWarning", { count: openAssetCount })}
    </Alert>
  );
}
