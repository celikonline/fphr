import { useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, Landmark, Pencil, Plus, Star, Trash2, Wallet } from "lucide-react";
import {
  ActionIcon,
  Badge,
  Button,
  Card,
  Checkbox,
  Divider,
  Group,
  Input,
  Modal,
  NumberInput,
  Select,
  Skeleton,
  Stack,
  Table,
  Text,
  TextInput,
  Textarea,
  Title,
  Tooltip,
} from "@mantine/core";
import { DatePicker } from "@/components/ui/date-picker";
import ConfirmDialog from "@/components/confirm-dialog";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import { formatIban, normalizeIban, validateIban } from "@/lib/iban";
import { COMMON_CURRENCIES, formatMoney, hasAtMostTwoDecimals } from "@/lib/money";
import {
  addBankAccount,
  addSalaryRecord,
  deleteBankAccount,
  deleteSalaryRecord,
  getEmployeeBankAccounts,
  getEmployeeSalaries,
  getMyBankAccounts,
  getMySalaries,
  setPrimaryBankAccount,
  updateBankAccount,
  updateSalaryRecord,
  type BankAccount,
  type BankAccountInput,
  type BankAccountType,
  type PayFrequency,
  type SalaryRecord,
  type SalaryRecordInput,
  type SalaryType,
} from "@/services/employee-payments.service";
import type { EmployeeDetail } from "@/services/employees.service";
import { useAuthStore } from "@/store/auth.store";
import { BANK_ACCOUNT_TYPE_OPTIONS, PAY_FREQUENCY_OPTIONS, SALARY_TYPE_OPTIONS } from "./employee-enum-options";

const READ_PERMISSION = "employee.employees.read";
const SALARY_VIEW_PERMISSION = "employee.salary.view";
const SALARY_EDIT_PERMISSION = "employee.salary.edit";
const BANK_EDIT_PERMISSION = "employee.bank_accounts.edit";
const DEFAULT_CURRENCY = "TRY";
const NONE = "__none__";

/** Today as a backend DateOnly ("yyyy-MM-dd") in the user's local calendar. */
function todayDateOnly(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

function currencyOptions(current: string | null | undefined): string[] {
  return current && !COMMON_CURRENCIES.includes(current) ? [...COMMON_CURRENCIES, current] : COMMON_CURRENCIES;
}

function amountText(value: number | null | undefined): string {
  return value === null || value === undefined ? "" : String(value);
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <Text component="dt" size="xs" c="dimmed">
        {label}
      </Text>
      <Text component="dd" size="sm" mt={2}>
        {children}
      </Text>
    </div>
  );
}

function SectionHeader({ icon, title, action }: { icon?: ReactNode; title: string; action?: ReactNode }) {
  return (
    <Group justify="space-between" wrap="nowrap" mb="md">
      <Group gap="xs" wrap="nowrap">
        {icon}
        <Title order={4}>{title}</Title>
      </Group>
      {action}
    </Group>
  );
}

function RowIconButton({ label, icon, onClick }: { label: string; icon: ReactNode; onClick: () => void }) {
  return (
    <Tooltip label={label}>
      <ActionIcon type="button" variant="subtle" color="gray" aria-label={label} onClick={onClick}>
        {icon}
      </ActionIcon>
    </Tooltip>
  );
}

function DialogFooter({ busy, onCancel }: { busy: boolean; onCancel: () => void }) {
  const { t } = useTranslation(["common"]);
  return (
    <Group justify="flex-end" gap="sm">
      <Button type="button" variant="default" onClick={onCancel} disabled={busy}>
        {t("common:cancel")}
      </Button>
      <Button type="submit" disabled={busy}>
        {busy ? t("common:saving") : t("common:save")}
      </Button>
    </Group>
  );
}

/** Whether the caller reads this card through the "/me" endpoints (Employee-role users opening their own card). */
function useOwnCard(employee: EmployeeDetail) {
  const canRead = usePermission(READ_PERMISSION);
  const ownEmployeeId = useAuthStore((state) => state.user?.employeeId);
  return { canRead, isOwnCard: !canRead && ownEmployeeId === employee.id };
}

/** "Ödemeler": salary history (employee.salary.view) and bank accounts (employee.employees.read); the own card shows both read-only. */
export function PaymentsSection({ employee }: { employee: EmployeeDetail }) {
  const { t } = useTranslation(["employees"]);
  const { canRead, isOwnCard } = useOwnCard(employee);
  const canViewSalary = usePermission(SALARY_VIEW_PERMISSION);
  const showSalary = isOwnCard || canViewSalary;
  const showBank = isOwnCard || canRead;

  if (!showSalary && !showBank) {
    return (
      <Text size="sm" c="dimmed">
        {t("employees:payments.noAccess")}
      </Text>
    );
  }

  return (
    <Stack gap="lg">
      {showSalary && <SalarySection employee={employee} isOwnCard={isOwnCard} />}
      {showBank && <BankAccountsSection employee={employee} isOwnCard={isOwnCard} />}
    </Stack>
  );
}

// ---------------------------------------------------------------- salary

function SalarySection({ employee, isOwnCard }: { employee: EmployeeDetail; isOwnCard: boolean }) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  const queryClient = useQueryClient();
  const editable = usePermission(SALARY_EDIT_PERMISSION) && !isOwnCard;
  const [dialog, setDialog] = useState<{ mode: "add" } | { mode: "edit"; record: SalaryRecord } | null>(null);
  const [deleting, setDeleting] = useState<SalaryRecord | null>(null);

  const queryKey = ["employees", "salaries", employee.id];
  const { data: records = [], isLoading } = useQuery({
    queryKey,
    queryFn: () => (isOwnCard ? getMySalaries() : getEmployeeSalaries(employee.id)),
  });

  const onChanged = (updated: SalaryRecord[], message: string) => {
    queryClient.setQueryData(queryKey, updated);
    toast({ variant: "success", title: t("common:success"), description: message });
  };

  const deleteMutation = useMutation({
    mutationFn: (record: SalaryRecord) => deleteSalaryRecord(employee.id, record.id),
    onSuccess: (updated) => {
      setDeleting(null);
      onChanged(updated, t("employees:payments.salary.deleted"));
    },
    onError: (error) => {
      setDeleting(null);
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("employees:genericError")) });
    },
  });

  const date = (value: string) => formatDateOnly(value, i18n.language);
  const money = (value: number | null | undefined, currency: string) =>
    value === null || value === undefined ? "—" : formatMoney(value, currency, i18n.language);
  const current = records.find((r) => r.isCurrent) ?? null;
  const today = todayDateOnly();

  if (isLoading) {
    return <Skeleton h={192} />;
  }

  return (
    <>
      <Card withBorder padding="lg">
        <SectionHeader
          icon={<Wallet size={16} style={{ color: "var(--mantine-color-dimmed)" }} />}
          title={t("employees:payments.salary.currentTitle")}
          action={
            editable && (
              <Button type="button" size="sm" leftSection={<Plus size={16} />} onClick={() => setDialog({ mode: "add" })}>
                {t("employees:payments.salary.add")}
              </Button>
            )
          }
        />
        {!current ? (
          <Text size="sm" c="dimmed">
            {records.length === 0 ? t("employees:payments.salary.empty") : t("employees:payments.salary.noCurrent")}
          </Text>
        ) : (
          <dl className="grid gap-4 sm:grid-cols-3">
            <Field label={t("employees:payments.salary.amount")}>
              <Text component="span" size="md" fw={600}>
                {money(current.amount, current.currency)}
              </Text>
            </Field>
            <Field label={t("employees:payments.salary.salaryType")}>{t(`employees:enum.salaryType.${current.salaryType}`)}</Field>
            <Field label={t("employees:payments.salary.payFrequency")}>{t(`employees:enum.payFrequency.${current.payFrequency}`)}</Field>
            <Field label={t("employees:payments.salary.effectiveFrom")}>{date(current.effectiveFrom)}</Field>
            <Field label={t("employees:payments.salary.transportAllowance")}>{money(current.transportAllowance, current.currency)}</Field>
            <Field label={t("employees:payments.salary.mealAllowance")}>{money(current.mealAllowance, current.currency)}</Field>
          </dl>
        )}
      </Card>

      <Card withBorder padding="lg">
        <SectionHeader title={t("employees:payments.salary.historyTitle")} />
        {records.length === 0 ? (
          <Text size="sm" c="dimmed">
            {t("employees:payments.salary.empty")}
          </Text>
        ) : (
          <Table.ScrollContainer minWidth={640}>
            <Table verticalSpacing="sm">
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>{t("employees:payments.salary.effectiveFrom")}</Table.Th>
                  <Table.Th ta="right">{t("employees:payments.salary.amount")}</Table.Th>
                  <Table.Th>{t("employees:payments.salary.salaryType")}</Table.Th>
                  <Table.Th>{t("employees:payments.salary.payFrequency")}</Table.Th>
                  <Table.Th ta="right">{t("employees:payments.salary.transportAllowance")}</Table.Th>
                  <Table.Th ta="right">{t("employees:payments.salary.mealAllowance")}</Table.Th>
                  {editable && <Table.Th w={0} />}
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {records.map((record) => (
                  <Table.Tr key={record.id}>
                    <Table.Td className="whitespace-nowrap">
                      <Group gap="xs" wrap="wrap">
                        {date(record.effectiveFrom)}
                        {record.isCurrent && <Badge variant="filled">{t("employees:payments.salary.current")}</Badge>}
                        {record.effectiveFrom > today && <Badge variant="light">{t("employees:payments.salary.upcoming")}</Badge>}
                      </Group>
                      {record.note && (
                        <Text size="xs" c="dimmed" mt={4}>
                          {record.note}
                        </Text>
                      )}
                    </Table.Td>
                    <Table.Td ta="right" fw={500} className="whitespace-nowrap">
                      {money(record.amount, record.currency)}
                    </Table.Td>
                    <Table.Td>{t(`employees:enum.salaryType.${record.salaryType}`)}</Table.Td>
                    <Table.Td>{t(`employees:enum.payFrequency.${record.payFrequency}`)}</Table.Td>
                    <Table.Td ta="right" className="whitespace-nowrap">
                      {money(record.transportAllowance, record.currency)}
                    </Table.Td>
                    <Table.Td ta="right" className="whitespace-nowrap">
                      {money(record.mealAllowance, record.currency)}
                    </Table.Td>
                    {editable && (
                      <Table.Td>
                        <Group gap={4} justify="flex-end" wrap="nowrap">
                          <RowIconButton label={t("common:edit")} icon={<Pencil size={16} />} onClick={() => setDialog({ mode: "edit", record })} />
                          <RowIconButton label={t("common:delete")} icon={<Trash2 size={16} />} onClick={() => setDeleting(record)} />
                        </Group>
                      </Table.Td>
                    )}
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        )}
      </Card>

      {dialog && (
        <SalaryRecordDialog
          key={dialog.mode === "edit" ? dialog.record.id : "add"}
          employeeId={employee.id}
          records={records}
          editing={dialog.mode === "edit" ? dialog.record : null}
          onClose={() => setDialog(null)}
          onSaved={(updated) => {
            setDialog(null);
            onChanged(updated, t("employees:payments.salary.saved"));
          }}
        />
      )}

      <ConfirmDialog
        open={Boolean(deleting)}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={t("employees:payments.salary.deleteTitle")}
        description={t("employees:payments.salary.deleteDescription", {
          date: deleting ? date(deleting.effectiveFrom) : "",
          amount: deleting ? money(deleting.amount, deleting.currency) : "",
        })}
        isLoading={deleteMutation.isPending}
        onConfirm={() => deleting && deleteMutation.mutate(deleting)}
      />
    </>
  );
}

interface SalaryRecordDialogProps {
  employeeId: string;
  records: SalaryRecord[];
  editing: SalaryRecord | null;
  onClose: () => void;
  onSaved: (updated: SalaryRecord[]) => void;
}

function SalaryRecordDialog({ employeeId, records, editing, onClose, onSaved }: SalaryRecordDialogProps) {
  const { t, i18n } = useTranslation(["employees", "common"]);
  // A new record starts from the current one's terms (a raise usually changes only the date and the amount).
  const source = editing ?? records.find((r) => r.isCurrent) ?? records[0] ?? null;

  const [effectiveFrom, setEffectiveFrom] = useState(editing?.effectiveFrom ?? todayDateOnly());
  const [amount, setAmount] = useState(amountText(editing?.amount));
  const [currency, setCurrency] = useState(source?.currency ?? DEFAULT_CURRENCY);
  const [salaryType, setSalaryType] = useState<SalaryType>(source?.salaryType ?? "gross");
  const [payFrequency, setPayFrequency] = useState<PayFrequency>(source?.payFrequency ?? "monthly");
  const [transport, setTransport] = useState(amountText(source?.transportAllowance));
  const [meal, setMeal] = useState(amountText(source?.mealAllowance));
  const [note, setNote] = useState(editing?.note ?? "");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (input: SalaryRecordInput) => (editing ? updateSalaryRecord(employeeId, editing.id, input) : addSalaryRecord(employeeId, input)),
    onSuccess: onSaved,
    onError: (err) => setError(getApiErrorMessage(err, t("employees:genericError"))),
  });

  const toAmount = (value: string) => (value.trim() === "" ? null : Number(value));
  const isValidAmount = (value: number | null, allowZero: boolean) =>
    value !== null && Number.isFinite(value) && (allowZero ? value >= 0 : value > 0) && hasAtMostTwoDecimals(value);

  function submit(event: FormEvent) {
    event.preventDefault();
    const parsedAmount = toAmount(amount);
    const parsedTransport = toAmount(transport);
    const parsedMeal = toAmount(meal);

    // "Maaş bloğu bütünlüğü": date and amount first; nothing else is saved without them.
    if (!effectiveFrom) return setError(t("employees:payments.salary.validation.effectiveFromRequired"));
    if (parsedAmount === null) return setError(t("employees:payments.salary.validation.amountRequired"));
    if (!isValidAmount(parsedAmount, false)) return setError(t("employees:payments.salary.validation.amountInvalid"));
    if ((parsedTransport !== null && !isValidAmount(parsedTransport, true)) || (parsedMeal !== null && !isValidAmount(parsedMeal, true)))
      return setError(t("employees:payments.salary.validation.allowanceInvalid"));
    if (records.some((r) => r.effectiveFrom === effectiveFrom && r.id !== editing?.id))
      return setError(t("employees:payments.salary.validation.duplicateDate", { date: formatDateOnly(effectiveFrom, i18n.language) }));

    setError(null);
    mutation.mutate({
      effectiveFrom,
      amount: parsedAmount,
      currency,
      salaryType,
      payFrequency,
      transportAllowance: parsedTransport,
      mealAllowance: parsedMeal,
      note: note.trim() || null,
    });
  }

  const busy = mutation.isPending;

  return (
    <Modal opened onClose={onClose} title={editing ? t("employees:payments.salary.edit") : t("employees:payments.salary.add")} size="42rem">
      <Text size="sm" c="dimmed" mb="md">
        {t("employees:payments.salary.dialogDescription")}
      </Text>
      <form onSubmit={submit}>
        <Stack gap="md">
          <div className="grid gap-4 sm:grid-cols-2">
            <Input.Wrapper id="salary-effectiveFrom" label={t("employees:payments.salary.effectiveFrom")} withAsterisk>
              <DatePicker id="salary-effectiveFrom" value={effectiveFrom} disabled={busy} onChange={setEffectiveFrom} />
            </Input.Wrapper>
            <NumberInput
              id="salary-amount"
              label={t("employees:payments.salary.amount")}
              withAsterisk
              inputMode="decimal"
              min={0}
              step={0.01}
              hideControls
              value={amount}
              disabled={busy}
              onChange={(v) => setAmount(String(v))}
            />
            <Select
              id="salary-currency"
              label={t("employees:payments.salary.currency")}
              data={currencyOptions(currency)}
              value={currency}
              onChange={(v) => v !== null && setCurrency(v)}
              allowDeselect={false}
              disabled={busy}
            />
            <Select
              id="salary-type"
              label={t("employees:payments.salary.salaryType")}
              data={SALARY_TYPE_OPTIONS.map((option) => ({ value: option, label: t(`employees:enum.salaryType.${option}`) }))}
              value={salaryType}
              onChange={(v) => v !== null && setSalaryType(v as SalaryType)}
              allowDeselect={false}
              disabled={busy}
            />
            <Select
              id="salary-payFrequency"
              label={t("employees:payments.salary.payFrequency")}
              data={PAY_FREQUENCY_OPTIONS.map((option) => ({ value: option, label: t(`employees:enum.payFrequency.${option}`) }))}
              value={payFrequency}
              onChange={(v) => v !== null && setPayFrequency(v as PayFrequency)}
              allowDeselect={false}
              disabled={busy}
            />
            <div />
            <NumberInput
              id="salary-transport"
              label={t("employees:payments.salary.transportAllowance")}
              inputMode="decimal"
              min={0}
              step={0.01}
              hideControls
              value={transport}
              disabled={busy}
              onChange={(v) => setTransport(String(v))}
            />
            <NumberInput
              id="salary-meal"
              label={t("employees:payments.salary.mealAllowance")}
              inputMode="decimal"
              min={0}
              step={0.01}
              hideControls
              value={meal}
              disabled={busy}
              onChange={(v) => setMeal(String(v))}
            />
          </div>
          <Text size="xs" c="dimmed">
            {t("employees:payments.salary.integrityHint")}
          </Text>
          <Textarea
            id="salary-note"
            label={t("employees:payments.salary.note")}
            value={note}
            maxLength={500}
            autosize
            minRows={3}
            disabled={busy}
            onChange={(e) => setNote(e.currentTarget.value)}
          />
          {error && (
            <Text size="sm" c="red">
              {error}
            </Text>
          )}
          <DialogFooter busy={busy} onCancel={onClose} />
        </Stack>
      </form>
    </Modal>
  );
}

// ---------------------------------------------------------------- bank accounts

function BankAccountsSection({ employee, isOwnCard }: { employee: EmployeeDetail; isOwnCard: boolean }) {
  const { t } = useTranslation(["employees", "common"]);
  const queryClient = useQueryClient();
  const editable = usePermission(BANK_EDIT_PERMISSION) && !isOwnCard;
  const [dialog, setDialog] = useState<{ mode: "add" } | { mode: "edit"; account: BankAccount } | null>(null);
  const [deleting, setDeleting] = useState<BankAccount | null>(null);

  const queryKey = ["employees", "bank-accounts", employee.id];
  const { data: accounts = [], isLoading } = useQuery({
    queryKey,
    queryFn: () => (isOwnCard ? getMyBankAccounts() : getEmployeeBankAccounts(employee.id)),
  });

  const onChanged = (updated: BankAccount[], message: string) => {
    queryClient.setQueryData(queryKey, updated);
    toast({ variant: "success", title: t("common:success"), description: message });
  };
  const onError = (error: unknown) =>
    toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("employees:genericError")) });

  const deleteMutation = useMutation({
    mutationFn: (account: BankAccount) => deleteBankAccount(employee.id, account.id),
    onSuccess: (updated) => {
      setDeleting(null);
      onChanged(updated, t("employees:payments.bank.deleted"));
    },
    onError: (error) => {
      setDeleting(null);
      onError(error);
    },
  });

  const primaryMutation = useMutation({
    mutationFn: (account: BankAccount) => setPrimaryBankAccount(employee.id, account.id),
    onSuccess: (updated) => onChanged(updated, t("employees:payments.bank.primaryChanged")),
    onError,
  });

  if (isLoading) {
    return <Skeleton h={128} />;
  }

  return (
    <Card withBorder padding="lg">
      <SectionHeader
        icon={<Landmark size={16} style={{ color: "var(--mantine-color-dimmed)" }} />}
        title={t("employees:payments.bank.title")}
        action={
          editable && (
            <Button type="button" size="sm" leftSection={<Plus size={16} />} onClick={() => setDialog({ mode: "add" })}>
              {t("employees:payments.bank.add")}
            </Button>
          )
        }
      />
      {accounts.length === 0 ? (
        <Text size="sm" c="dimmed">
          {t("employees:payments.bank.empty")}
        </Text>
      ) : (
        <ul>
          {accounts.map((account, index) => (
            <li key={account.id}>
              {index > 0 && <Divider my="sm" />}
              <div className="flex flex-wrap items-start justify-between gap-2">
                <Stack gap={4} className="min-w-0">
                  <Group gap="xs" wrap="wrap">
                    <Text size="sm" ff="monospace">
                      {account.iban}
                    </Text>
                    {account.isPrimary && <Badge variant="filled">{t("employees:payments.bank.primary")}</Badge>}
                  </Group>
                  <Text size="xs" c="dimmed">
                    {[t(`employees:enum.bankAccountType.${account.accountType}`), account.bankName, account.currency].filter(Boolean).join(" · ")}
                  </Text>
                  {account.note && (
                    <Text size="xs" c="dimmed">
                      {account.note}
                    </Text>
                  )}
                </Stack>
                {editable && (
                  <Group gap={4} wrap="nowrap">
                    {!account.isPrimary && (
                      <Button
                        type="button"
                        variant="subtle"
                        size="sm"
                        leftSection={<Star size={16} />}
                        disabled={primaryMutation.isPending}
                        onClick={() => primaryMutation.mutate(account)}
                      >
                        {t("employees:payments.bank.makePrimary")}
                      </Button>
                    )}
                    <RowIconButton label={t("common:edit")} icon={<Pencil size={16} />} onClick={() => setDialog({ mode: "edit", account })} />
                    <RowIconButton label={t("common:delete")} icon={<Trash2 size={16} />} onClick={() => setDeleting(account)} />
                  </Group>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}

      {dialog && (
        <BankAccountDialog
          key={dialog.mode === "edit" ? dialog.account.id : "add"}
          employeeId={employee.id}
          hasAccounts={accounts.length > 0}
          editing={dialog.mode === "edit" ? dialog.account : null}
          onClose={() => setDialog(null)}
          onSaved={(updated) => {
            setDialog(null);
            onChanged(updated, t("employees:payments.bank.saved"));
          }}
        />
      )}

      <ConfirmDialog
        open={Boolean(deleting)}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={t("employees:payments.bank.deleteTitle")}
        description={
          deleting?.isPrimary && accounts.length > 1
            ? t("employees:payments.bank.deletePrimaryDescription")
            : t("employees:payments.bank.deleteDescription")
        }
        isLoading={deleteMutation.isPending}
        onConfirm={() => deleting && deleteMutation.mutate(deleting)}
      />
    </Card>
  );
}

interface BankAccountDialogProps {
  employeeId: string;
  hasAccounts: boolean;
  editing: BankAccount | null;
  onClose: () => void;
  onSaved: (updated: BankAccount[]) => void;
}

function BankAccountDialog({ employeeId, hasAccounts, editing, onClose, onSaved }: BankAccountDialogProps) {
  const { t } = useTranslation(["employees", "common"]);
  const maskedOriginal = editing?.isIbanMasked ?? false;
  // A masked IBAN cannot be edited in place: the field starts empty and a blank value keeps the stored IBAN.
  const [iban, setIban] = useState(editing && !maskedOriginal ? editing.iban : "");
  const [accountType, setAccountType] = useState<BankAccountType>(editing?.accountType ?? "demandDeposit");
  const [bankName, setBankName] = useState(editing?.bankName ?? "");
  const [currency, setCurrency] = useState<string | null>(editing ? (editing.currency ?? null) : DEFAULT_CURRENCY);
  const [note, setNote] = useState(editing?.note ?? "");
  const [isPrimary, setIsPrimary] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (input: BankAccountInput) => (editing ? updateBankAccount(employeeId, editing.id, input) : addBankAccount(employeeId, input)),
    onSuccess: onSaved,
    onError: (err) => setError(getApiErrorMessage(err, t("employees:genericError"))),
  });

  const normalized = normalizeIban(iban);
  const keepsStoredIban = Boolean(editing) && normalized === "" && maskedOriginal;
  const problem = keepsStoredIban ? null : validateIban(normalized);
  // Live feedback while typing: a non-TR country or a character that can never be valid shows at once, the rest at full length.
  const liveProblem =
    normalized === "" || problem === null
      ? null
      : /^[A-Z]{2}/.test(normalized) && !normalized.startsWith("TR")
        ? "countryUnsupported"
        : normalized.length >= 26 || !/^(T|TR[0-9]*)$/.test(normalized)
          ? problem
          : null;

  function submit(event: FormEvent) {
    event.preventDefault();
    if (problem) return setError(t(`employees:payments.bank.validation.${problem}`));

    setError(null);
    mutation.mutate({
      iban: keepsStoredIban ? null : normalized,
      accountType,
      bankName: bankName.trim() || null,
      currency,
      note: note.trim() || null,
      ...(editing ? {} : { isPrimary }),
    });
  }

  const busy = mutation.isPending;

  return (
    <Modal opened onClose={onClose} title={editing ? t("employees:payments.bank.edit") : t("employees:payments.bank.add")} size="36rem">
      <Text size="sm" c="dimmed" mb="md">
        {t("employees:payments.bank.dialogDescription")}
      </Text>
      <form onSubmit={submit}>
        <Stack gap="md">
          <div>
            <TextInput
              id="bank-iban"
              label={t("employees:payments.bank.iban")}
              withAsterisk={!maskedOriginal}
              description={maskedOriginal ? t("employees:payments.bank.ibanMaskedHint") : undefined}
              inputWrapperOrder={["label", "input", "description", "error"]}
              error={liveProblem ? t(`employees:payments.bank.validation.${liveProblem}`) : undefined}
              styles={{ input: { fontFamily: "var(--mantine-font-family-monospace)" } }}
              autoComplete="off"
              spellCheck={false}
              maxLength={42}
              placeholder={maskedOriginal ? editing?.iban : "TR00 0000 0000 0000 0000 0000 00"}
              value={iban}
              disabled={busy}
              onChange={(e) => setIban(formatIban(e.currentTarget.value))}
            />
            {normalized !== "" && problem === null && (
              <Group gap={4} mt={4} wrap="nowrap">
                <CheckCircle2 size={14} style={{ color: "var(--mantine-color-green-6)" }} />
                <Text size="xs" c="dimmed">
                  {t("employees:payments.bank.ibanValid")}
                </Text>
              </Group>
            )}
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <Select
              id="bank-accountType"
              label={t("employees:payments.bank.accountType")}
              data={BANK_ACCOUNT_TYPE_OPTIONS.map((option) => ({ value: option, label: t(`employees:enum.bankAccountType.${option}`) }))}
              value={accountType}
              onChange={(v) => v !== null && setAccountType(v as BankAccountType)}
              allowDeselect={false}
              disabled={busy}
            />
            <Select
              id="bank-currency"
              label={t("employees:payments.bank.currency")}
              data={[
                { value: NONE, label: t("employees:enum.notSet") },
                ...currencyOptions(currency).map((code) => ({ value: code, label: code })),
              ]}
              value={currency ?? NONE}
              onChange={(v) => setCurrency(v === null || v === NONE ? null : v)}
              allowDeselect={false}
              disabled={busy}
            />
          </div>
          <TextInput
            id="bank-name"
            label={t("employees:payments.bank.bankName")}
            value={bankName}
            maxLength={100}
            disabled={busy}
            onChange={(e) => setBankName(e.currentTarget.value)}
          />
          <Textarea
            id="bank-note"
            label={t("employees:payments.bank.note")}
            value={note}
            maxLength={500}
            autosize
            minRows={3}
            disabled={busy}
            onChange={(e) => setNote(e.currentTarget.value)}
          />
          {!editing && hasAccounts && (
            <Checkbox
              id="bank-primary"
              label={t("employees:payments.bank.setAsPrimary")}
              checked={isPrimary}
              disabled={busy}
              onChange={(e) => setIsPrimary(e.currentTarget.checked)}
            />
          )}
          {error && (
            <Text size="sm" c="red">
              {error}
            </Text>
          )}
          <DialogFooter busy={busy} onCancel={onClose} />
        </Stack>
      </form>
    </Modal>
  );
}
