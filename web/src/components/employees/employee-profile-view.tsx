import type { ReactNode } from "react";
import { Link } from "react-router";
import { useTranslation } from "react-i18next";
import { Check, ChevronRight, Copy, EyeOff, Mail, Phone } from "lucide-react";
import {
  ActionIcon,
  Anchor,
  Card,
  CopyButton,
  Divider,
  Group,
  Progress,
  Stack,
  Text,
  Title,
} from "@mantine/core";
import { usePermission } from "@/components/permission-guard";
import { useAuthStore } from "@/store/auth.store";
import { formatDateOnly, parseDateOnly, toDateOnly } from "@/lib/dates";
import type { EmployeeDetail } from "@/services/employees.service";
import { AccountStatusBadge, EmployeeStatusBadge } from "./employee-badges";
import { EmployeeAvatar } from "./employee-avatar";
import { EmployeePhotoEditor } from "./employee-photo-editor";
import { useOrgUnits } from "./use-org-units";

const MONTHS_PER_YEAR = 12;
const HIGH_COMPLETION = 80;
const MEDIUM_COMPLETION = 50;

export interface Tenure {
  years: number;
  months: number;
  days: number;
}

const MS_PER_DAY = 86_400_000;

/**
 * Calendar difference between two date-only strings ("12 yıl 10 ay 2 gün"); null when the end is before the start.
 * Whole months are counted first, then the remaining days are measured from the start date moved forward by those
 * months (clamped to the month's last day, so 31 Jan + 1 month = 28 Feb) - never a negative day count.
 */
export function computeTenure(startDate: string, endDate: string): Tenure | null {
  const start = parseDateOnly(startDate);
  const end = parseDateOnly(endDate);
  if (end < start) return null;

  let totalMonths =
    (end.getFullYear() - start.getFullYear()) * MONTHS_PER_YEAR +
    (end.getMonth() - start.getMonth());
  if (end.getDate() < start.getDate()) totalMonths -= 1;

  const anchorMonthStart = new Date(start.getFullYear(), start.getMonth() + totalMonths, 1);
  const lastDayOfAnchorMonth = new Date(
    anchorMonthStart.getFullYear(),
    anchorMonthStart.getMonth() + 1,
    0
  ).getDate();
  const anchor = new Date(
    anchorMonthStart.getFullYear(),
    anchorMonthStart.getMonth(),
    Math.min(start.getDate(), lastDayOfAnchorMonth)
  );

  const days = Math.round(
    (Date.UTC(end.getFullYear(), end.getMonth(), end.getDate()) -
      Date.UTC(anchor.getFullYear(), anchor.getMonth(), anchor.getDate())) /
      MS_PER_DAY
  );
  return {
    years: Math.floor(totalMonths / MONTHS_PER_YEAR),
    months: totalMonths % MONTHS_PER_YEAR,
    days,
  };
}

export interface Completion {
  completed: number;
  total: number;
  percent: number;
}

const isSet = (value: unknown) =>
  value !== null && value !== undefined && value !== "" && value !== "unspecified";

/** "Özlük doluluk oranı": how many of the tracked personnel-file fields are filled in. */
export function profileCompletion(employee: EmployeeDetail): Completion {
  const tracked: unknown[] = [
    employee.nationalId,
    employee.birthDate,
    employee.gender,
    employee.maritalStatus,
    employee.educationStatus,
    employee.highestEducationLevel,
    employee.workEmail,
    employee.workPhone,
    employee.personalEmail,
    employee.mobilePhone,
    employee.addressLine,
    employee.city,
    employee.postalCode,
    employee.hireDate,
    employee.contractType,
    employee.employmentType,
    employee.managerEmployeeId,
    employee.departmentName,
    employee.titleName,
    employee.orgUnitId,
  ];
  const completed = tracked.filter(isSet).length;
  return {
    completed,
    total: tracked.length,
    percent: Math.round((completed / tracked.length) * 100),
  };
}

/** Grey-headed card ("Yöneticisi", "İletişim", …) as in the reference layout; `action` sits at the right of the header. */
function InfoCard({
  title,
  action,
  children,
}: {
  title: string;
  action?: ReactNode;
  children: ReactNode;
}) {
  return (
    <Card withBorder padding={0} radius="md">
      <Group
        justify="space-between"
        wrap="nowrap"
        px="lg"
        py="sm"
        style={{
          background: "light-dark(var(--mantine-color-gray-0), var(--mantine-color-dark-6))",
          borderBottom: "1px solid var(--mantine-color-default-border)",
        }}
      >
        <Title order={4}>{title}</Title>
        {action}
      </Group>
      <div className="p-4 md:p-5">{children}</div>
    </Card>
  );
}

function Info({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="min-w-0">
      <Text component="dt" size="xs" c="dimmed">
        {label}
      </Text>
      <Text component="dd" size="sm" fw={500} mt={2} className="break-words">
        {children}
      </Text>
    </div>
  );
}

function InfoGrid({ children }: { children: ReactNode }) {
  return <dl className="grid gap-x-8 gap-y-4 sm:grid-cols-2">{children}</dl>;
}

function CopyValue({ value }: { value: string }) {
  const { t } = useTranslation(["employees"]);
  const label = t("employees:card.copy");
  return (
    <CopyButton value={value}>
      {({ copied, copy }) => (
        <ActionIcon
          variant="subtle"
          color={copied ? "teal" : "gray"}
          size="sm"
          onClick={copy}
          aria-label={label}
        >
          {copied ? <Check size={15} /> : <Copy size={15} />}
        </ActionIcon>
      )}
    </CopyButton>
  );
}

/** Icon + label + value row with a copy button (contact card). */
function ContactRow({
  icon,
  label,
  value,
}: {
  icon: ReactNode;
  label: string;
  value: string | null;
}) {
  return (
    <Group gap="md" wrap="nowrap" py="xs">
      <Text component="span" c="brand" className="inline-flex shrink-0">
        {icon}
      </Text>
      <div className="min-w-0 flex-1">
        <Text size="xs" c="dimmed">
          {label}
        </Text>
        <Text size="sm" fw={500} truncate>
          {value ?? "—"}
        </Text>
      </div>
      {value && <CopyValue value={value} />}
    </Group>
  );
}

function ContactList({
  rows,
}: {
  rows: Array<{ key: string; icon: ReactNode; label: string; value: string | null }>;
}) {
  return (
    <Stack gap={0}>
      {rows.map((row, index) => (
        <div key={row.key}>
          {index > 0 && <Divider />}
          <ContactRow icon={row.icon} label={row.label} value={row.value} />
        </div>
      ))}
    </Stack>
  );
}

const PRIVATE_ICON = <EyeOff size={18} />;

function TenureText({ employee }: { employee: EmployeeDetail }) {
  const { t } = useTranslation(["employees"]);
  if (!employee.hireDate) return <>—</>;
  const tenure = computeTenure(
    employee.hireDate,
    employee.terminationDate ?? toDateOnly(new Date())
  );
  if (!tenure) return <>—</>;

  const parts = [
    tenure.years > 0 && t("employees:card.tenure.years", { count: tenure.years }),
    tenure.months > 0 && t("employees:card.tenure.months", { count: tenure.months }),
    (tenure.days > 0 || (tenure.years === 0 && tenure.months === 0)) &&
      t("employees:card.tenure.days", { count: tenure.days }),
  ].filter(Boolean);
  return <>{parts.join(" ")}</>;
}

/** Read-only "Profil": identity header with photo, employment dates and organisation, plus manager / contact / access cards. */
export function ProfileView({
  employee,
  editAction,
}: {
  employee: EmployeeDetail;
  editAction?: ReactNode;
}) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const tenantName = useAuthStore((state) => state.user?.tenant?.name);
  const canReadEmployees = usePermission("employee.employees.read");
  const { nameById } = useOrgUnits();
  const unitName = employee.orgUnitId ? nameById.get(employee.orgUnitId) : undefined;
  const date = (value: string | null | undefined) =>
    value ? formatDateOnly(value, i18n.language) : "—";
  const enumLabel = (group: string, value: string | null) =>
    value ? t(`employees:enum.${group}.${value}`) : "—";

  return (
    <div className="grid items-start gap-5 lg:grid-cols-3">
      <div className="lg:col-span-2">
        <InfoCard title={t("employees:card.sections.general")} action={editAction}>
          <Group justify="space-between" align="flex-start" wrap="nowrap" mb="lg" gap="md">
            <div className="min-w-0 pt-2">
              <Title order={2} size="h3">
                {employee.displayName}
              </Title>
              <Group gap="xs" mt="xs">
                <EmployeeStatusBadge
                  status={employee.status}
                  label={t("employees:card.statusLabel")}
                />
                {employee.terminationDate && (
                  <Text size="xs" c="dimmed">
                    {t("employees:card.profile.terminationDate")}: {date(employee.terminationDate)}
                  </Text>
                )}
              </Group>
            </div>
            <EmployeePhotoEditor employee={employee} size={112} radius="md" fz={32} />
          </Group>
          <Divider mb="lg" />
          <InfoGrid>
            <Info label={t("employees:fields.hireDate")}>{date(employee.hireDate)}</Info>
            <Info label={t("employees:fields.contractType")}>
              {enumLabel("contractType", employee.contractType)}
            </Info>
            <Info label={t("employees:card.profile.tenure")}>
              <TenureText employee={employee} />
            </Info>
            <Info label={t("employees:fields.leaveAccrualStartDate")}>
              {date(employee.leaveAccrualStartDate)}
            </Info>
          </InfoGrid>
          <Divider my="lg" />
          <InfoGrid>
            <Info label={t("employees:fields.positionStartDate")}>
              {date(employee.positionStartDate)}
            </Info>
            <Info label={t("employees:fields.employmentType")}>
              {enumLabel("employmentType", employee.employmentType)}
            </Info>
            <Info label={t("employees:card.profile.company")}>{tenantName ?? "—"}</Info>
            <Info label={t("employees:fields.orgUnit")}>{unitName ?? "—"}</Info>
            <Info label={t("employees:card.profile.department")}>
              {employee.departmentName ?? "—"}
            </Info>
            <Info label={t("employees:card.profile.title")}>{employee.titleName ?? "—"}</Info>
          </InfoGrid>
          <Divider my="lg" />
          <Group justify="flex-end">
            <Anchor component={Link} to={`/app/employees/${employee.id}/career`} size="sm" fw={500}>
              <Group component="span" gap={4} wrap="nowrap">
                {t("employees:card.tabs.career")} <ChevronRight size={16} />
              </Group>
            </Anchor>
          </Group>
        </InfoCard>
      </div>

      <Stack gap="lg">
        <InfoCard title={t("employees:card.groups.manager")}>
          {employee.managerEmployeeId && employee.managerDisplayName ? (
            <Group gap="sm" wrap="nowrap">
              <EmployeeAvatar
                employeeId={employee.managerEmployeeId}
                firstName={employee.managerDisplayName.split(" ")[0] ?? ""}
                lastName={employee.managerDisplayName.split(" ").slice(1).join(" ")}
                size={36}
                radius="md"
              />
              {canReadEmployees ? (
                <Anchor
                  component={Link}
                  to={`/app/employees/${employee.managerEmployeeId}/general`}
                  size="sm"
                  fw={500}
                  truncate
                >
                  {employee.managerDisplayName}
                </Anchor>
              ) : (
                <Text size="sm" fw={500} truncate>
                  {employee.managerDisplayName}
                </Text>
              )}
            </Group>
          ) : (
            <Text size="sm" c="dimmed">
              —
            </Text>
          )}
        </InfoCard>

        <InfoCard title={t("employees:card.groups.contact")}>
          <ContactList
            rows={[
              {
                key: "workEmail",
                icon: <Mail size={18} />,
                label: t("employees:fields.workEmail"),
                value: employee.workEmail,
              },
              {
                key: "workPhone",
                icon: <Phone size={18} />,
                label: t("employees:fields.workPhone"),
                value: employee.workPhone,
              },
              {
                key: "personalEmail",
                icon: PRIVATE_ICON,
                label: t("employees:fields.personalEmail"),
                value: employee.personalEmail,
              },
              {
                key: "mobilePhone",
                icon: PRIVATE_ICON,
                label: t("employees:fields.mobilePhone"),
                value: employee.mobilePhone,
              },
            ]}
          />
        </InfoCard>

        <InfoCard title={t("employees:card.groups.access")}>
          <InfoGrid>
            <Info label={t("employees:fields.accessType")}>
              {employee.userId && employee.accessType
                ? t(`employees:accessType.${employee.accessType}`)
                : "—"}
            </Info>
            <Info label={t("employees:fields.accountStatus")}>
              <AccountStatusBadge status={employee.accountStatus} />
            </Info>
          </InfoGrid>
        </InfoCard>
      </Stack>
    </div>
  );
}

/** Read-only "Kişisel bilgiler": citizenship, education, address on the left; completion and private contact on the right. */
export function PersonalView({
  employee,
  editAction,
}: {
  employee: EmployeeDetail;
  editAction?: ReactNode;
}) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const canViewSensitive = usePermission("employee.sensitive.view");
  const completion = profileCompletion(employee);
  const remaining = completion.total - completion.completed;
  const completionColor =
    completion.percent >= HIGH_COMPLETION
      ? "green"
      : completion.percent >= MEDIUM_COMPLETION
        ? "orange"
        : "red";
  const date = (value: string | null | undefined) =>
    value ? formatDateOnly(value, i18n.language) : "—";
  const enumLabel = (group: string, value: string | null) =>
    value ? t(`employees:enum.${group}.${value}`) : "—";

  return (
    <div className="grid items-start gap-5 lg:grid-cols-3">
      <Stack gap="lg" className="lg:col-span-2">
        <InfoCard title={t("employees:card.groups.citizenship")} action={editAction}>
          <InfoGrid>
            <Info label={t("employees:fields.birthDate")}>{date(employee.birthDate)}</Info>
            <Info label={t("employees:fields.gender")}>{enumLabel("gender", employee.gender)}</Info>
            <Info label={t("employees:fields.maritalStatus")}>
              {enumLabel("maritalStatus", employee.maritalStatus)}
            </Info>
            <Info label={t("employees:fields.childCount")}>{employee.childCount ?? "—"}</Info>
            <Info label={t("employees:fields.disabilityDegree")}>
              {enumLabel("disabilityDegree", employee.disabilityDegree)}
            </Info>
            <Info label={t("employees:fields.militaryStatus")}>
              {enumLabel("militaryStatus", employee.militaryStatus)}
            </Info>
            <Info label={t("employees:fields.bloodType")}>
              {enumLabel("bloodType", employee.bloodType)}
            </Info>
          </InfoGrid>
          <Divider my="lg" />
          <Group justify="space-between" wrap="nowrap" align="flex-start">
            <Info label={t("employees:fields.nationalId")}>{employee.nationalId ?? "—"}</Info>
            {employee.nationalId && <CopyValue value={employee.nationalId} />}
          </Group>
          {!canViewSensitive && (
            <Text size="xs" c="dimmed" mt="md">
              {t("employees:card.sensitiveHidden")}
            </Text>
          )}
        </InfoCard>

        <InfoCard title={t("employees:card.groups.education")}>
          <InfoGrid>
            <Info label={t("employees:fields.educationStatus")}>
              {enumLabel("educationStatus", employee.educationStatus)}
            </Info>
            <Info label={t("employees:fields.highestEducationLevel")}>
              {enumLabel("highestEducationLevel", employee.highestEducationLevel)}
            </Info>
          </InfoGrid>
        </InfoCard>

        <InfoCard title={t("employees:card.groups.address")}>
          <Group justify="space-between" wrap="nowrap" align="flex-start" mb="md">
            <Info label={t("employees:fields.addressLine")}>{employee.addressLine ?? "—"}</Info>
            {employee.addressLine && <CopyValue value={employee.addressLine} />}
          </Group>
          <InfoGrid>
            <Info label={t("employees:fields.city")}>{employee.city ?? "—"}</Info>
            <Info label={t("employees:fields.postalCode")}>{employee.postalCode ?? "—"}</Info>
          </InfoGrid>
        </InfoCard>
      </Stack>

      <Stack gap="lg">
        <Card withBorder padding="lg" radius="md">
          <Group gap="xs" mb="xs">
            <Title order={4}>{t("employees:card.completion.title")}</Title>
          </Group>
          <Text fz={32} fw={600} c={completionColor} lh={1.1}>
            %{completion.percent}
          </Text>
          <Progress
            value={completion.percent}
            color={completionColor}
            size="sm"
            my="sm"
            aria-label={t("employees:card.completion.title")}
          />
          <Group justify="space-between">
            <Text size="sm">
              {t("employees:card.completion.completed")}{" "}
              <Text component="span" fw={600}>
                {completion.completed}
              </Text>
            </Text>
            <Text size="sm">
              {t("employees:card.completion.remaining")}{" "}
              <Text component="span" fw={600}>
                {remaining}
              </Text>
            </Text>
          </Group>
        </Card>

        <InfoCard title={t("employees:card.groups.personalContact")}>
          <ContactList
            rows={[
              {
                key: "personalEmail",
                icon: PRIVATE_ICON,
                label: t("employees:fields.personalEmail"),
                value: employee.personalEmail,
              },
              {
                key: "mobilePhone",
                icon: PRIVATE_ICON,
                label: t("employees:fields.mobilePhone"),
                value: employee.mobilePhone,
              },
              {
                key: "homePhone",
                icon: <Phone size={18} />,
                label: t("employees:fields.homePhone"),
                value: employee.homePhone,
              },
            ]}
          />
        </InfoCard>
      </Stack>
    </div>
  );
}
