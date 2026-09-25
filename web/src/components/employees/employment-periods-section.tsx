import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Badge, Button, Card, Group, Skeleton, Stack, Text, Title } from "@mantine/core";
import { Download } from "lucide-react";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import {
  downloadWorkCertificate,
  getEmploymentPeriods,
  type EmployeeDetail,
} from "@/services/employees.service";
import { saveDownloadedPayrollFile } from "@/services/payroll.service";

/**
 * "Çalışma dönemleri ve kıdem" (E5a): every employment period of the employee plus the merged seniority they add up
 * to. An employee hired before periods were recorded shows a single implicit period built from the hire date.
 */
export function EmploymentPeriodsSection({ employee }: { employee: EmployeeDetail }) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const canDownloadCertificate = usePermission("employee.employees.update");
  const { data, isLoading } = useQuery({
    queryKey: ["employment-periods", employee.id],
    queryFn: () => getEmploymentPeriods(employee.id),
  });
  // Çalışma belgesi (İş K. m.28): the same PDF the leaver received by e-mail.
  const certificate = useMutation({
    mutationFn: () => downloadWorkCertificate(employee.id),
    onSuccess: saveDownloadedPayrollFile,
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("employees:workCertificate.failed")),
      }),
  });

  if (isLoading) {
    return <Skeleton h={160} />;
  }

  const date = (value: string | null) => (value ? formatDateOnly(value, i18n.language) : "—");
  const seniority = data?.seniority;

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" align="flex-start" mb="md" wrap="wrap">
        <Title order={4}>{t("employees:employmentPeriods.title")}</Title>
        {employee.status === "Terminated" && canDownloadCertificate && (
          <Button
            size="compact-sm"
            variant="default"
            leftSection={<Download size={14} />}
            loading={certificate.isPending}
            onClick={() => certificate.mutate()}
          >
            {t("employees:workCertificate.download")}
          </Button>
        )}
      </Group>

      {!data || data.periods.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("employees:employmentPeriods.empty")}
        </Text>
      ) : (
        <Stack gap="sm">
          {data.periods.map((period) => (
            <Group key={period.id} justify="space-between" wrap="wrap" gap="xs">
              <Stack gap={4} className="min-w-0">
                <Group gap="xs" wrap="wrap">
                  <Text size="sm" fw={500}>
                    {t("employees:career.period", {
                      from: date(period.startDate),
                      to: period.endDate
                        ? date(period.endDate)
                        : t("employees:employmentPeriods.current"),
                    })}
                  </Text>
                  <Badge variant="light">
                    {t(`employees:enum.employmentPeriodSource.${period.source}`)}
                  </Badge>
                  {period.severancePaid && (
                    <Badge variant="outline">
                      {t("employees:employmentPeriods.severancePaid")}
                    </Badge>
                  )}
                  {period.leaveSettledAtExit && (
                    <Badge variant="outline">{t("employees:employmentPeriods.leaveSettled")}</Badge>
                  )}
                </Group>
                {period.terminationReasonName && (
                  <Text size="xs" c="dimmed">
                    {t("employees:employmentPeriods.columns.reason")}:{" "}
                    {period.terminationReasonName}
                  </Text>
                )}
              </Stack>
              <Text size="sm" c="dimmed">
                {t("employees:employmentPeriods.seniority.days", { count: period.countedDays })}
              </Text>
            </Group>
          ))}
        </Stack>
      )}

      {seniority && (
        <div className="mt-4 grid gap-4 sm:grid-cols-3">
          <SeniorityField
            label={t("employees:employmentPeriods.seniority.leave")}
            value={t("employees:employmentPeriods.seniority.days", {
              count: seniority.leaveSeniorityDays,
            })}
            hint={
              seniority.leaveSeniorityStartDate
                ? t("employees:employmentPeriods.seniority.since", {
                    date: date(seniority.leaveSeniorityStartDate),
                  })
                : null
            }
          />
          <SeniorityField
            label={t("employees:employmentPeriods.seniority.severance")}
            value={t("employees:employmentPeriods.seniority.days", {
              count: seniority.severanceSeniorityDays,
            })}
            hint={
              seniority.severanceSeniorityStartDate
                ? t("employees:employmentPeriods.seniority.since", {
                    date: date(seniority.severanceSeniorityStartDate),
                  })
                : null
            }
          />
          <SeniorityField
            label={t("employees:employmentPeriods.seniority.jobSecurity")}
            value={t("employees:employmentPeriods.seniority.months", {
              count: seniority.jobSecuritySeniorityMonths,
            })}
            hint={null}
          />
        </div>
      )}
    </Card>
  );
}

function SeniorityField({
  label,
  value,
  hint,
}: {
  label: string;
  value: string;
  hint: string | null;
}) {
  return (
    <div>
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text size="sm" fw={500}>
        {value}
      </Text>
      {hint && (
        <Text size="xs" c="dimmed">
          {hint}
        </Text>
      )}
    </div>
  );
}
