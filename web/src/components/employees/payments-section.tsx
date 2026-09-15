import { useState, type FormEvent, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, Landmark, Pencil, Plus, Star, Trash2, Wallet } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Textarea } from "@/components/ui/textarea";
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
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 text-sm">{children}</dd>
    </div>
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
    return <p className="text-sm text-muted-foreground">{t("employees:payments.noAccess")}</p>;
  }

  return (
    <div className="space-y-6">
      {showSalary && <SalarySection employee={employee} isOwnCard={isOwnCard} />}
      {showBank && <BankAccountsSection employee={employee} isOwnCard={isOwnCard} />}
    </div>
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
    return <Skeleton className="h-48 w-full" />;
  }

  return (
    <>
      <Card>
        <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-3">
          <div className="flex items-center gap-2">
            <Wallet className="h-4 w-4 text-muted-foreground" />
            <CardTitle className="text-lg">{t("employees:payments.salary.currentTitle")}</CardTitle>
          </div>
          {editable && (
            <Button type="button" size="sm" onClick={() => setDialog({ mode: "add" })}>
              <Plus className="mr-2 h-4 w-4" /> {t("employees:payments.salary.add")}
            </Button>
          )}
        </CardHeader>
        <CardContent>
          {!current ? (
            <p className="text-sm text-muted-foreground">
              {records.length === 0 ? t("employees:payments.salary.empty") : t("employees:payments.salary.noCurrent")}
            </p>
          ) : (
            <dl className="grid gap-4 sm:grid-cols-3">
              <Field label={t("employees:payments.salary.amount")}>
                <span className="text-base font-semibold">{money(current.amount, current.currency)}</span>
              </Field>
              <Field label={t("employees:payments.salary.salaryType")}>{t(`employees:enum.salaryType.${current.salaryType}`)}</Field>
              <Field label={t("employees:payments.salary.payFrequency")}>{t(`employees:enum.payFrequency.${current.payFrequency}`)}</Field>
              <Field label={t("employees:payments.salary.effectiveFrom")}>{date(current.effectiveFrom)}</Field>
              <Field label={t("employees:payments.salary.transportAllowance")}>{money(current.transportAllowance, current.currency)}</Field>
              <Field label={t("employees:payments.salary.mealAllowance")}>{money(current.mealAllowance, current.currency)}</Field>
            </dl>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="pb-3">
          <CardTitle className="text-lg">{t("employees:payments.salary.historyTitle")}</CardTitle>
        </CardHeader>
        <CardContent>
          {records.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t("employees:payments.salary.empty")}</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("employees:payments.salary.effectiveFrom")}</TableHead>
                  <TableHead className="text-right">{t("employees:payments.salary.amount")}</TableHead>
                  <TableHead>{t("employees:payments.salary.salaryType")}</TableHead>
                  <TableHead>{t("employees:payments.salary.payFrequency")}</TableHead>
                  <TableHead className="text-right">{t("employees:payments.salary.transportAllowance")}</TableHead>
                  <TableHead className="text-right">{t("employees:payments.salary.mealAllowance")}</TableHead>
                  {editable && <TableHead className="w-0" />}
                </TableRow>
              </TableHeader>
              <TableBody>
                {records.map((record) => (
                  <TableRow key={record.id}>
                    <TableCell className="whitespace-nowrap">
                      <div className="flex flex-wrap items-center gap-2">
                        {date(record.effectiveFrom)}
                        {record.isCurrent && <Badge>{t("employees:payments.salary.current")}</Badge>}
                        {record.effectiveFrom > today && <Badge variant="secondary">{t("employees:payments.salary.upcoming")}</Badge>}
                      </div>
                      {record.note && <p className="mt-1 text-xs text-muted-foreground">{record.note}</p>}
                    </TableCell>
                    <TableCell className="whitespace-nowrap text-right font-medium">{money(record.amount, record.currency)}</TableCell>
                    <TableCell>{t(`employees:enum.salaryType.${record.salaryType}`)}</TableCell>
                    <TableCell>{t(`employees:enum.payFrequency.${record.payFrequency}`)}</TableCell>
                    <TableCell className="whitespace-nowrap text-right">{money(record.transportAllowance, record.currency)}</TableCell>
                    <TableCell className="whitespace-nowrap text-right">{money(record.mealAllowance, record.currency)}</TableCell>
                    {editable && (
                      <TableCell>
                        <div className="flex justify-end gap-1">
                          <Button type="button" variant="ghost" size="icon" title={t("common:edit")} onClick={() => setDialog({ mode: "edit", record })}>
                            <Pencil className="h-4 w-4" />
                          </Button>
                          <Button type="button" variant="ghost" size="icon" title={t("common:delete")} onClick={() => setDeleting(record)}>
                            <Trash2 className="h-4 w-4" />
                          </Button>
                        </div>
                      </TableCell>
                    )}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
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
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{editing ? t("employees:payments.salary.edit") : t("employees:payments.salary.add")}</DialogTitle>
          <DialogDescription>{t("employees:payments.salary.dialogDescription")}</DialogDescription>
        </DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <Label htmlFor="salary-effectiveFrom">
                {t("employees:payments.salary.effectiveFrom")}
                <span className="text-destructive"> *</span>
              </Label>
              <Input id="salary-effectiveFrom" type="date" value={effectiveFrom} disabled={busy} onChange={(e) => setEffectiveFrom(e.target.value)} />
            </div>
            <div>
              <Label htmlFor="salary-amount">
                {t("employees:payments.salary.amount")}
                <span className="text-destructive"> *</span>
              </Label>
              <Input
                id="salary-amount"
                type="number"
                inputMode="decimal"
                min="0"
                step="0.01"
                value={amount}
                disabled={busy}
                onChange={(e) => setAmount(e.target.value)}
              />
            </div>
            <div>
              <Label htmlFor="salary-currency">{t("employees:payments.salary.currency")}</Label>
              <Select value={currency} onValueChange={setCurrency} disabled={busy}>
                <SelectTrigger id="salary-currency">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {currencyOptions(currency).map((code) => (
                    <SelectItem key={code} value={code}>
                      {code}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div>
              <Label htmlFor="salary-type">{t("employees:payments.salary.salaryType")}</Label>
              <Select value={salaryType} onValueChange={(v) => setSalaryType(v as SalaryType)} disabled={busy}>
                <SelectTrigger id="salary-type">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {SALARY_TYPE_OPTIONS.map((option) => (
                    <SelectItem key={option} value={option}>
                      {t(`employees:enum.salaryType.${option}`)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div>
              <Label htmlFor="salary-payFrequency">{t("employees:payments.salary.payFrequency")}</Label>
              <Select value={payFrequency} onValueChange={(v) => setPayFrequency(v as PayFrequency)} disabled={busy}>
                <SelectTrigger id="salary-payFrequency">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {PAY_FREQUENCY_OPTIONS.map((option) => (
                    <SelectItem key={option} value={option}>
                      {t(`employees:enum.payFrequency.${option}`)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div />
            <div>
              <Label htmlFor="salary-transport">{t("employees:payments.salary.transportAllowance")}</Label>
              <Input
                id="salary-transport"
                type="number"
                inputMode="decimal"
                min="0"
                step="0.01"
                value={transport}
                disabled={busy}
                onChange={(e) => setTransport(e.target.value)}
              />
            </div>
            <div>
              <Label htmlFor="salary-meal">{t("employees:payments.salary.mealAllowance")}</Label>
              <Input
                id="salary-meal"
                type="number"
                inputMode="decimal"
                min="0"
                step="0.01"
                value={meal}
                disabled={busy}
                onChange={(e) => setMeal(e.target.value)}
              />
            </div>
          </div>
          <p className="text-xs text-muted-foreground">{t("employees:payments.salary.integrityHint")}</p>
          <div>
            <Label htmlFor="salary-note">{t("employees:payments.salary.note")}</Label>
            <Textarea id="salary-note" value={note} maxLength={500} disabled={busy} onChange={(e) => setNote(e.target.value)} />
          </div>
          {error && <p className="text-sm text-destructive">{error}</p>}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={busy}>
              {busy ? t("common:saving") : t("common:save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
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
    return <Skeleton className="h-32 w-full" />;
  }

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-3">
        <div className="flex items-center gap-2">
          <Landmark className="h-4 w-4 text-muted-foreground" />
          <CardTitle className="text-lg">{t("employees:payments.bank.title")}</CardTitle>
        </div>
        {editable && (
          <Button type="button" size="sm" onClick={() => setDialog({ mode: "add" })}>
            <Plus className="mr-2 h-4 w-4" /> {t("employees:payments.bank.add")}
          </Button>
        )}
      </CardHeader>
      <CardContent>
        {accounts.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t("employees:payments.bank.empty")}</p>
        ) : (
          <ul className="divide-y divide-border">
            {accounts.map((account) => (
              <li key={account.id} className="flex flex-wrap items-start justify-between gap-2 py-3 first:pt-0 last:pb-0">
                <div className="min-w-0 space-y-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-mono text-sm">{account.iban}</span>
                    {account.isPrimary && <Badge>{t("employees:payments.bank.primary")}</Badge>}
                  </div>
                  <p className="text-xs text-muted-foreground">
                    {[t(`employees:enum.bankAccountType.${account.accountType}`), account.bankName, account.currency].filter(Boolean).join(" · ")}
                  </p>
                  {account.note && <p className="text-xs text-muted-foreground">{account.note}</p>}
                </div>
                {editable && (
                  <div className="flex gap-1">
                    {!account.isPrimary && (
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        disabled={primaryMutation.isPending}
                        onClick={() => primaryMutation.mutate(account)}
                      >
                        <Star className="mr-2 h-4 w-4" /> {t("employees:payments.bank.makePrimary")}
                      </Button>
                    )}
                    <Button type="button" variant="ghost" size="icon" title={t("common:edit")} onClick={() => setDialog({ mode: "edit", account })}>
                      <Pencil className="h-4 w-4" />
                    </Button>
                    <Button type="button" variant="ghost" size="icon" title={t("common:delete")} onClick={() => setDeleting(account)}>
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </CardContent>

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
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent className="max-h-[90vh] overflow-y-auto sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>{editing ? t("employees:payments.bank.edit") : t("employees:payments.bank.add")}</DialogTitle>
          <DialogDescription>{t("employees:payments.bank.dialogDescription")}</DialogDescription>
        </DialogHeader>
        <form onSubmit={submit} className="space-y-4">
          <div>
            <Label htmlFor="bank-iban">
              {t("employees:payments.bank.iban")}
              {!maskedOriginal && <span className="text-destructive"> *</span>}
            </Label>
            <Input
              id="bank-iban"
              className="font-mono"
              autoComplete="off"
              spellCheck={false}
              maxLength={42}
              placeholder={maskedOriginal ? editing?.iban : "TR00 0000 0000 0000 0000 0000 00"}
              value={iban}
              disabled={busy}
              onChange={(e) => setIban(formatIban(e.target.value))}
            />
            {maskedOriginal && <p className="mt-1 text-xs text-muted-foreground">{t("employees:payments.bank.ibanMaskedHint")}</p>}
            {liveProblem && <p className="mt-1 text-xs text-destructive">{t(`employees:payments.bank.validation.${liveProblem}`)}</p>}
            {normalized !== "" && problem === null && (
              <p className="mt-1 flex items-center gap-1 text-xs text-muted-foreground">
                <CheckCircle2 className="h-3.5 w-3.5 text-green-600" /> {t("employees:payments.bank.ibanValid")}
              </p>
            )}
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <Label htmlFor="bank-accountType">{t("employees:payments.bank.accountType")}</Label>
              <Select value={accountType} onValueChange={(v) => setAccountType(v as BankAccountType)} disabled={busy}>
                <SelectTrigger id="bank-accountType">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {BANK_ACCOUNT_TYPE_OPTIONS.map((option) => (
                    <SelectItem key={option} value={option}>
                      {t(`employees:enum.bankAccountType.${option}`)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div>
              <Label htmlFor="bank-currency">{t("employees:payments.bank.currency")}</Label>
              <Select value={currency ?? NONE} onValueChange={(v) => setCurrency(v === NONE ? null : v)} disabled={busy}>
                <SelectTrigger id="bank-currency">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={NONE}>{t("employees:enum.notSet")}</SelectItem>
                  {currencyOptions(currency).map((code) => (
                    <SelectItem key={code} value={code}>
                      {code}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>
          <div>
            <Label htmlFor="bank-name">{t("employees:payments.bank.bankName")}</Label>
            <Input id="bank-name" value={bankName} maxLength={100} disabled={busy} onChange={(e) => setBankName(e.target.value)} />
          </div>
          <div>
            <Label htmlFor="bank-note">{t("employees:payments.bank.note")}</Label>
            <Textarea id="bank-note" value={note} maxLength={500} disabled={busy} onChange={(e) => setNote(e.target.value)} />
          </div>
          {!editing && hasAccounts && (
            <div className="flex items-center gap-2">
              <Checkbox id="bank-primary" checked={isPrimary} disabled={busy} onCheckedChange={(checked) => setIsPrimary(checked === true)} />
              <Label htmlFor="bank-primary" className="font-normal">
                {t("employees:payments.bank.setAsPrimary")}
              </Label>
            </div>
          )}
          {error && <p className="text-sm text-destructive">{error}</p>}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={busy}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" disabled={busy}>
              {busy ? t("common:saving") : t("common:save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
