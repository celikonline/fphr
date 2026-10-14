import { useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Badge, Button, Card, Group, Modal, NumberInput, Skeleton, Stack, Table, Text, Title } from "@mantine/core";
import { DateInput } from "@mantine/dates";
import { usePermission } from "@/components/permission-guard";
import { formatDateOnly, parseDateOnly, toDateOnly } from "@/lib/dates";
import { formatMoney } from "@/lib/money";
import {
  getEmployeePayrollProfile,
  listGarnishmentOrders,
  updateEmployeeRegularBenefits,
  type EmployeePayrollProfile,
} from "@/services/payroll.service";
import type { EmployeeDetail } from "@/services/employees.service";
import { EmployeePayslipsCard } from "./employee-payslips-card";
import { EmployeePayItemsCard } from "./employee-pay-items-card";
import { EmployeeIncentivesCard, hasEffectiveSgkAssignment, useEmployeeIncentiveAssignments } from "./employee-incentives-card";
import { ExitSettlementSection } from "./exit-settlement-section";
import { getApiErrorMessage } from "@/lib/api-error";

/** "Bordro" tab: visible with either read gate; each card then checks its own permission. */
export const PAYROLL_CARD_TAB_PERMISSIONS = ["payroll.trace.view", "payroll.profile.manage"];

/** Employee card > Bordro: payroll profile, payslips, garnishments and (for terminated employees) the exit settlement. */
export function PayrollSection({ employee }: { employee: EmployeeDetail }) {
  const canViewPayslips = usePermission("payroll.trace.view");
  const canViewProfile = usePermission("payroll.profile.manage");

  return (
    <Stack gap="lg">
      {canViewProfile && <PayrollProfileCard employeeId={employee.id} />}
      {canViewPayslips && <EmployeePayslipsCard employeeId={employee.id} />}
      {canViewPayslips && <GarnishmentsCard employeeId={employee.id} />}
      {canViewPayslips && <EmployeePayItemsCard employee={employee} />}
      {canViewPayslips && <EmployeeIncentivesCard employee={employee} />}
      <ExitSettlementSection employee={employee} />
    </Stack>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <Text size="xs" c="dimmed">
        {label}
      </Text>
      <Text size="sm" fw={500}>
        {children}
      </Text>
    </div>
  );
}

function PayrollProfileCard({ employeeId }: { employeeId: string }) {
  const { t, i18n } = useTranslation(["employees"]);
  const [editingBenefits, setEditingBenefits] = useState(false);
  const [profileDate, setProfileDate] = useState(() => toDateOnly(new Date()));
  const canViewAssignments = usePermission("payroll.trace.view");
  const { data, isLoading } = useQuery({
    queryKey: ["employee-payroll-profile", employeeId, profileDate],
    queryFn: () => getEmployeePayrollProfile(employeeId, profileDate),
    retry: false,
  });
  const assignments = useEmployeeIncentiveAssignments(employeeId, canViewAssignments);

  return (
    <Card withBorder padding="lg">
      <Group justify="space-between" mb="md">
        <Title order={4}>{t("employees:payrollTab.profile.title")}</Title>
        {data && <Button variant="light" size="xs" onClick={() => setEditingBenefits(true)}>
          {t("employees:payrollTab.profile.editBenefits")}
        </Button>}
      </Group>
      <DateInput label={t("employees:payrollTab.profile.asOfDate")} value={profileDate}
        onChange={(value) => setProfileDate(value ?? toDateOnly(new Date()))} mb="md" maw={260} />
      {isLoading ? (
        <Skeleton h={96} />
      ) : !data ? (
        <Text size="sm" c="dimmed">
          {t("employees:payrollTab.profile.empty")}
        </Text>
      ) : (
        <ProfileFields profile={data} language={i18n.language} incentiveFieldUnused={hasEffectiveSgkAssignment(assignments.data, parseDateOnly(profileDate))} />
      )}
      {editingBenefits && data && <RegularBenefitsDialog employeeId={employeeId} profile={data} onClose={() => setEditingBenefits(false)} />}
    </Card>
  );
}

function RegularBenefitsDialog({ employeeId, profile, onClose }: { employeeId: string; profile: EmployeePayrollProfile; onClose: () => void }) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const queryClient = useQueryClient();
  const [amount, setAmount] = useState(profile.regularBenefitsMonthlyGross ?? 0);
  const save = useMutation({
    mutationFn: () => updateEmployeeRegularBenefits(employeeId, profile.id, amount),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["employee-payroll-profile", employeeId] }),
        queryClient.invalidateQueries({ queryKey: ["severance-estimate", employeeId] }),
      ]);
      onClose();
    },
  });
  return (
    <Modal opened onClose={onClose} title={t("employees:payrollTab.profile.regularBenefits")}>
      <Stack>
        <Text size="sm" c="dimmed">{t("employees:payrollTab.profile.regularBenefitsHint")}</Text>
        <Text size="sm">{t("employees:payrollTab.profile.validFrom")}: {formatDateOnly(profile.validFrom, i18n.language)}</Text>
        <NumberInput label={t("employees:payrollTab.profile.regularBenefits")} value={amount} min={0} max={999_999_999_999.99} decimalScale={2}
          suffix=" TRY" onChange={(value) => setAmount(typeof value === "number" ? value : 0)} />
        {save.isError && <Alert color="red">{getApiErrorMessage(save.error, t("common:error"))}</Alert>}
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose}>{t("common:cancel")}</Button>
          <Button onClick={() => save.mutate()} loading={save.isPending} disabled={amount < 0 || !Number.isFinite(amount)}>{t("common:save")}</Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function ProfileFields({
  profile,
  language,
  incentiveFieldUnused,
}: {
  profile: EmployeePayrollProfile;
  language: string;
  /** An SGK incentive assignment covers this month: the profile's law code is ignored by the calculation. */
  incentiveFieldUnused: boolean;
}) {
  const { t } = useTranslation(["employees", "payroll"]);
  const yesNo = (value: boolean) =>
    t(value ? "employees:payrollTab.profile.yes" : "employees:payrollTab.profile.no");
  const meal =
    profile.mealBenefitVariant === "none"
      ? t("employees:payrollTab.enum.meal.none")
      : `${t(`employees:payrollTab.enum.meal.${profile.mealBenefitVariant}`)} (${t(
          "employees:payrollTab.profile.mealDaily",
          {
            amount: formatMoney(profile.mealDailyAmount, "TRY", language),
          }
        )})`;

  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <Field label={t("employees:payrollTab.profile.validFrom")}>
        {formatDateOnly(profile.validFrom, language)}
      </Field>
      <Field label={t("employees:payrollTab.profile.wageBasis")}>
        {t(`employees:payrollTab.enum.wageBasis.${profile.wageBasis}`)}
      </Field>
      <Field label={t("employees:payrollTab.profile.sgkStatus")}>
        {t(`employees:payrollTab.enum.sgkStatus.${profile.sgkStatus}`)}
      </Field>
      <Field label={t("employees:payrollTab.profile.incentive")}>
        {profile.incentiveLawCode || "—"}
        {profile.incentiveLawCode && incentiveFieldUnused && (
          <Text span size="xs" c="orange" ml={6} data-testid="incentive-legacy-field-hint">
            {t("payroll:incentiveAssignments.legacyProfileHint")}
          </Text>
        )}
      </Field>
      <Field label={t("employees:payrollTab.profile.meal")}>{meal}</Field>
      <Field label={t("employees:payrollTab.profile.transport")}>
        {t(`employees:payrollTab.enum.transport.${profile.transportVariant}`)}
      </Field>
      <Field label={t("employees:payrollTab.profile.paymentMethod")}>
        {t(`employees:payrollTab.enum.paymentMethod.${profile.paymentMethod}`)}
      </Field>
      <Field label={t("employees:payrollTab.profile.sickDays")}>
        {yesNo(profile.sickDaysReducePay)}
      </Field>
      <Field label={t("employees:payrollTab.profile.externalCode")}>
        {profile.externalPayrollCode || "—"}
      </Field>
      <Field label={t("employees:payrollTab.profile.regularBenefits")}>
        {formatMoney(profile.regularBenefitsMonthlyGross ?? 0, "TRY", language)}
      </Field>
      {profile.notes && (
        <div className="sm:col-span-2 lg:col-span-3">
          <Field label={t("employees:payrollTab.profile.notes")}>{profile.notes}</Field>
        </div>
      )}
    </div>
  );
}

function GarnishmentsCard({ employeeId }: { employeeId: string }) {
  const { t, i18n } = useTranslation(["employees"]);
  const { data, isLoading } = useQuery({
    queryKey: ["employee-garnishments", employeeId],
    queryFn: () => listGarnishmentOrders(employeeId),
    retry: false,
  });
  const money = (amount: number) => formatMoney(amount, "TRY", i18n.language);

  return (
    <Card withBorder padding="lg">
      <Title order={4} mb="md">
        {t("employees:payrollTab.garnishments.title")}
      </Title>
      {isLoading ? (
        <Skeleton h={64} />
      ) : !data || data.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("employees:payrollTab.garnishments.empty")}
        </Text>
      ) : (
        <Table.ScrollContainer minWidth={560}>
          <Table verticalSpacing="xs" horizontalSpacing="sm">
            <Table.Thead>
              <Table.Tr>
                <Table.Th>{t("employees:payrollTab.garnishments.columns.type")}</Table.Th>
                <Table.Th>{t("employees:payrollTab.garnishments.columns.case")}</Table.Th>
                <Table.Th ta="right">
                  {t("employees:payrollTab.garnishments.columns.installment")}
                </Table.Th>
                <Table.Th ta="right">
                  {t("employees:payrollTab.garnishments.columns.remaining")}
                </Table.Th>
                <Table.Th>{t("employees:payrollTab.garnishments.columns.start")}</Table.Th>
                <Table.Th>{t("employees:payrollTab.garnishments.columns.status")}</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.map((order) => (
                <Table.Tr key={order.id}>
                  <Table.Td>
                    {t(`employees:payrollTab.enum.garnishmentType.${order.type}`)}
                  </Table.Td>
                  <Table.Td>{order.caseReference}</Table.Td>
                  <Table.Td ta="right">{money(order.monthlyInstallmentAmount)}</Table.Td>
                  <Table.Td ta="right">{money(order.remainingBalance)}</Table.Td>
                  <Table.Td>{formatDateOnly(order.startDate, i18n.language)}</Table.Td>
                  <Table.Td>
                    <Badge variant="light" color={order.isClosed ? "gray" : "green"}>
                      {t(
                        order.isClosed
                          ? "employees:payrollTab.garnishments.closed"
                          : "employees:payrollTab.garnishments.open"
                      )}
                    </Badge>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
    </Card>
  );
}
