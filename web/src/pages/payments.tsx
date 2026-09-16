import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";
import { Controller, useForm, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ActionIcon,
  Badge,
  Button,
  Card,
  Group,
  Modal,
  NumberInput,
  Select,
  Skeleton,
  Stack,
  Switch,
  Table,
  Text,
  Textarea,
  TextInput,
  Title,
  Tooltip,
} from "@mantine/core";
import { CreditCard, Plus, Receipt, Trash2, Wallet } from "lucide-react";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { DatePicker } from "@/components/ui/date-picker";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly } from "@/lib/dates";
import {
  addExpenseItem,
  createAdvanceRequest,
  createExpenseClaim,
  getExpenseClaim,
  listExpenseCategories,
  listMyPayments,
  removeExpenseItem,
  submitExpenseClaim,
  type ExpenseCategory,
  type ExpenseClaim,
  type PaymentOverviewRow,
} from "@/services/expense.service";

const PAYMENTS_KEY = ["expense", "payments", "me"] as const;
const CLAIM_KEY = ["expense", "claim"] as const;
const CATEGORIES_KEY = ["expense", "categories"] as const;

const STATUS_COLOR: Record<string, string> = {
  draft: "gray",
  submitted: "blue",
  approved: "teal",
  rejected: "red",
  paid: "green",
};

const KNOWN_CATEGORY_CODES = ["travel", "food"] as const;

/** "Kendi hesabım > Ödemeler" (roadmap #64): combined expense claim / advance status list + "+ Harcama"/"+ Avans" creation. */
export default function PaymentsPage() {
  const canView = usePermission("expense.my.payments");
  if (!canView) return <NoAccess />;
  return <Payments />;
}

function Payments() {
  const { t, i18n } = useTranslation(["expense", "common", "requests"]);
  const queryClient = useQueryClient();
  const canClaims = usePermission("expense.my.claims");
  const canAdvances = usePermission("expense.my.advances");
  const [searchParams, setSearchParams] = useSearchParams();
  const [claimDialogOpen, setClaimDialogOpen] = useState(() => canClaims && searchParams.get("new") === "expense");
  const [advanceDialogOpen, setAdvanceDialogOpen] = useState(() => canAdvances && searchParams.get("new") === "advance");

  useEffect(() => {
    if (searchParams.get("new")) {
      setSearchParams(
        (params) => {
          params.delete("new");
          return params;
        },
        { replace: true },
      );
    }
  }, [searchParams, setSearchParams]);

  const paymentsQuery = useQuery({ queryKey: PAYMENTS_KEY, queryFn: listMyPayments });

  const refresh = () => queryClient.invalidateQueries({ queryKey: ["expense"] });
  const date = (value: string) => formatDateOnly(value, i18n.language);
  const amount = (value: number, currency: string) => `${value.toLocaleString(i18n.language)} ${currency}`;

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Wallet className="h-6 w-6" /> {t("expense:payments.title")}
          </Title>
          <Text c="dimmed">{t("expense:payments.subtitle")}</Text>
        </div>
        <Group gap="xs">
          {canClaims && (
            <Button type="button" leftSection={<Receipt className="h-4 w-4" />} onClick={() => setClaimDialogOpen(true)}>
              {t("expense:claim.newButton")}
            </Button>
          )}
          {canAdvances && (
            <Button type="button" variant="default" leftSection={<CreditCard className="h-4 w-4" />} onClick={() => setAdvanceDialogOpen(true)}>
              {t("expense:advance.newButton")}
            </Button>
          )}
        </Group>
      </Group>

      <Card withBorder padding="lg">
        {paymentsQuery.isLoading ? (
          <Skeleton h={128} />
        ) : paymentsQuery.isError ? (
          <Text size="sm" c="red">
            {getApiErrorMessage(paymentsQuery.error, t("expense:genericError"))}
          </Text>
        ) : (paymentsQuery.data ?? []).length === 0 ? (
          <Text size="sm" c="dimmed">
            {t("expense:payments.empty")}
          </Text>
        ) : (
          <PaymentsTable rows={paymentsQuery.data ?? []} formatDate={date} formatAmount={amount} t={t} />
        )}
      </Card>

      {claimDialogOpen && (
        <ExpenseClaimDialog
          onClose={() => setClaimDialogOpen(false)}
          onSubmitted={() => {
            setClaimDialogOpen(false);
            toast({ variant: "success", title: t("common:success"), description: t("expense:claim.submitted") });
            void refresh();
          }}
        />
      )}

      {advanceDialogOpen && (
        <AdvanceRequestDialog
          onClose={() => setAdvanceDialogOpen(false)}
          onSubmitted={() => {
            setAdvanceDialogOpen(false);
            toast({ variant: "success", title: t("common:success"), description: t("expense:advance.submitted") });
            void refresh();
          }}
        />
      )}
    </div>
  );
}

function PaymentsTable({
  rows,
  formatDate,
  formatAmount,
  t,
}: {
  rows: PaymentOverviewRow[];
  formatDate: (value: string) => string;
  formatAmount: (value: number, currency: string) => string;
  t: (key: string, options?: Record<string, unknown>) => string;
}) {
  return (
    <Table.ScrollContainer minWidth={560}>
      <Table verticalSpacing="sm">
        <Table.Thead>
          <Table.Tr>
            <Table.Th>{t("expense:payments.col.kind")}</Table.Th>
            <Table.Th>{t("expense:payments.col.date")}</Table.Th>
            <Table.Th>{t("expense:payments.col.description")}</Table.Th>
            <Table.Th>{t("expense:payments.col.amount")}</Table.Th>
            <Table.Th>{t("expense:payments.col.status")}</Table.Th>
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {rows.map((row) => (
            <Table.Tr key={`${row.kind}-${row.sourceId}`}>
              <Table.Td>{t(`expense:payments.kind.${row.kind}`)}</Table.Td>
              <Table.Td>{formatDate(row.date)}</Table.Td>
              <Table.Td>{row.description ?? "—"}</Table.Td>
              <Table.Td>{formatAmount(row.amount, row.currency)}</Table.Td>
              <Table.Td>
                <Badge color={STATUS_COLOR[row.status.toLowerCase()] ?? "gray"} variant="light">
                  {t(`expense:status.${row.status.toLowerCase()}`, { defaultValue: row.status })}
                </Badge>
              </Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Table.ScrollContainer>
  );
}

/** "Talep > Harcama": create a Draft claim, add/remove receipt items, then Submit once at least one item exists. */
function ExpenseClaimDialog({ onClose, onSubmitted }: { onClose: () => void; onSubmitted: () => void }) {
  const { t, i18n } = useTranslation(["expense", "common"]);
  const queryClient = useQueryClient();
  const [claimId, setClaimId] = useState<string | null>(null);
  const [description, setDescription] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [itemAmount, setItemAmount] = useState<number | "">("");
  const [receiptDate, setReceiptDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [taxRate, setTaxRate] = useState<number | "">("");
  const [itemDescription, setItemDescription] = useState("");

  const categoriesQuery = useQuery({ queryKey: CATEGORIES_KEY, queryFn: () => listExpenseCategories() });
  const claimQuery = useQuery({ queryKey: [...CLAIM_KEY, claimId], queryFn: () => getExpenseClaim(claimId!), enabled: Boolean(claimId) });

  const createMutation = useMutation({
    mutationFn: () => createExpenseClaim(description || null),
    onSuccess: (id) => setClaimId(id),
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("expense:genericError")) }),
  });

  const addItemMutation = useMutation({
    mutationFn: () =>
      addExpenseItem(claimId!, {
        categoryId,
        amount: Number(itemAmount),
        receiptDate,
        taxRate: taxRate === "" ? null : Number(taxRate),
        currency: "TRY",
        description: itemDescription || null,
      }),
    onSuccess: () => {
      setCategoryId("");
      setItemAmount("");
      setTaxRate("");
      setItemDescription("");
      void queryClient.invalidateQueries({ queryKey: [...CLAIM_KEY, claimId] });
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("expense:genericError")) }),
  });

  const removeItemMutation = useMutation({
    mutationFn: (itemId: string) => removeExpenseItem(claimId!, itemId),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: [...CLAIM_KEY, claimId] }),
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("expense:genericError")) }),
  });

  const submitMutation = useMutation({
    mutationFn: () => submitExpenseClaim(claimId!),
    onSuccess: onSubmitted,
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("expense:genericError")) }),
  });

  const categoryName = (category: ExpenseCategory) =>
    (KNOWN_CATEGORY_CODES as readonly string[]).includes(category.code)
      ? t(`expense:categories.${category.code}`)
      : category.code;

  const claim: ExpenseClaim | undefined = claimQuery.data;
  const canAddItem = categoryId !== "" && itemAmount !== "" && Number(itemAmount) > 0;

  return (
    <Modal opened onClose={onClose} title={t("expense:claim.dialogTitle")} size="lg">
      <Stack gap="md">
        {!claimId ? (
          <>
            <Textarea
              label={t("expense:claim.description")}
              placeholder={t("expense:claim.descriptionPlaceholder")}
              value={description}
              onChange={(event) => setDescription(event.currentTarget.value)}
              autosize
              minRows={2}
            />
            <Group justify="flex-end">
              <Button type="button" variant="default" onClick={onClose}>
                {t("common:cancel")}
              </Button>
              <Button type="button" loading={createMutation.isPending} onClick={() => createMutation.mutate()}>
                {t("expense:claim.continue")}
              </Button>
            </Group>
          </>
        ) : (
          <>
            <Card withBorder padding="md">
              <Stack gap="xs">
                <Text fw={500} size="sm">
                  {t("expense:claim.addItem")}
                </Text>
                <Group grow>
                  <Select
                    label={t("expense:item.category")}
                    placeholder={t("expense:item.categoryPlaceholder")}
                    data={(categoriesQuery.data ?? []).map((category) => ({ value: category.id, label: categoryName(category) }))}
                    value={categoryId || null}
                    onChange={(value) => setCategoryId(value ?? "")}
                    searchable
                  />
                  <NumberInput
                    label={t("expense:item.amount")}
                    value={itemAmount}
                    onChange={(value) => setItemAmount(typeof value === "number" ? value : "")}
                    min={0}
                    decimalScale={2}
                    suffix=" TRY"
                  />
                </Group>
                <Group grow>
                  <DatePicker value={receiptDate} onChange={setReceiptDate} />
                  <NumberInput
                    label={t("expense:item.taxRate")}
                    value={taxRate}
                    onChange={(value) => setTaxRate(typeof value === "number" ? value : "")}
                    min={0}
                    max={100}
                    suffix="%"
                  />
                </Group>
                <TextInput
                  label={t("expense:item.description")}
                  value={itemDescription}
                  onChange={(event) => setItemDescription(event.currentTarget.value)}
                />
                <Group justify="flex-end">
                  <Button
                    type="button"
                    size="sm"
                    leftSection={<Plus className="h-4 w-4" />}
                    disabled={!canAddItem}
                    loading={addItemMutation.isPending}
                    onClick={() => addItemMutation.mutate()}
                  >
                    {t("expense:claim.addItem")}
                  </Button>
                </Group>
              </Stack>
            </Card>

            {claimQuery.isLoading ? (
              <Skeleton h={80} />
            ) : (
              <Stack gap="xs">
                {(claim?.items ?? []).map((item) => (
                  <Group key={item.id} justify="space-between" wrap="nowrap">
                    <div>
                      <Text size="sm">{item.description || categoryName((categoriesQuery.data ?? []).find((c) => c.id === item.categoryId) ?? { id: "", code: "", nameKey: "", defaultTaxRate: null, isActive: true })}</Text>
                      <Text size="xs" c="dimmed">
                        {item.receiptDate}
                      </Text>
                    </div>
                    <Group gap="xs" wrap="nowrap">
                      <Text size="sm" fw={500}>
                        {item.amount.toLocaleString(i18n.language)} {item.currency}
                      </Text>
                      <Tooltip label={t("common:delete")}>
                        <ActionIcon
                          type="button"
                          variant="subtle"
                          color="red"
                          aria-label={t("common:delete")}
                          onClick={() => removeItemMutation.mutate(item.id)}
                        >
                          <Trash2 className="h-4 w-4" />
                        </ActionIcon>
                      </Tooltip>
                    </Group>
                  </Group>
                ))}
                {(claim?.items ?? []).length === 0 && (
                  <Text size="sm" c="dimmed">
                    {t("expense:claim.noItems")}
                  </Text>
                )}
              </Stack>
            )}

            <Group justify="space-between" mt="sm">
              <Text fw={600}>
                {t("expense:claim.total")}: {(claim?.totalAmount ?? 0).toLocaleString(i18n.language)}{" "}
                {claim?.items[0]?.currency ?? "TRY"}
              </Text>
              <Group gap="xs">
                <Button type="button" variant="default" onClick={onClose}>
                  {t("common:close")}
                </Button>
                <Button
                  type="button"
                  disabled={(claim?.items.length ?? 0) === 0}
                  loading={submitMutation.isPending}
                  onClick={() => submitMutation.mutate()}
                >
                  {t("expense:claim.submit")}
                </Button>
              </Group>
            </Group>
          </>
        )}
      </Stack>
    </Modal>
  );
}

const advanceSchema = z.object({
  amount: z.number().positive(),
  requestedDate: z.string().min(1),
  isInstallmentRequested: z.boolean(),
  installmentCount: z.number().int().min(1).max(12).optional(),
  description: z.string().optional(),
});

type AdvanceFormValues = z.infer<typeof advanceSchema>;

/** "Talep > Avans": only Amount is strictly required (part1 web walkthrough); the rest default. */
function AdvanceRequestDialog({ onClose, onSubmitted }: { onClose: () => void; onSubmitted: () => void }) {
  const { t } = useTranslation(["expense", "common"]);
  const today = useMemo(() => new Date().toISOString().slice(0, 10), []);
  const {
    handleSubmit,
    control,
    formState: { errors },
  } = useForm<AdvanceFormValues>({
    resolver: zodResolver(advanceSchema),
    defaultValues: { amount: 0, requestedDate: today, isInstallmentRequested: false, installmentCount: 1, description: "" },
  });
  const isInstallmentRequested = useWatch({ control, name: "isInstallmentRequested" });

  const createMutation = useMutation({
    mutationFn: (values: AdvanceFormValues) =>
      createAdvanceRequest({
        amount: values.amount,
        currency: "TRY",
        requestedDate: values.requestedDate,
        isInstallmentRequested: values.isInstallmentRequested,
        installmentCount: values.isInstallmentRequested ? values.installmentCount : null,
        description: values.description || null,
      }),
    onSuccess: onSubmitted,
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("expense:genericError")) }),
  });

  return (
    <Modal opened onClose={onClose} title={t("expense:advance.dialogTitle")} size="md">
      <form onSubmit={handleSubmit((values) => createMutation.mutate(values))}>
        <Stack gap="md">
          <Controller
            name="amount"
            control={control}
            render={({ field }) => (
              <NumberInput
                label={t("expense:advance.amount")}
                value={field.value}
                onChange={(value) => field.onChange(typeof value === "number" ? value : 0)}
                min={0}
                decimalScale={2}
                suffix=" TRY"
                error={errors.amount ? t("common:required") : undefined}
                withAsterisk
              />
            )}
          />
          <Controller
            name="requestedDate"
            control={control}
            render={({ field }) => <DatePicker value={field.value} onChange={field.onChange} />}
          />
          <Controller
            name="isInstallmentRequested"
            control={control}
            render={({ field }) => (
              <Switch
                label={t("expense:advance.installmentRequested")}
                checked={field.value}
                onChange={(event) => field.onChange(event.currentTarget.checked)}
              />
            )}
          />
          {isInstallmentRequested && (
            <Controller
              name="installmentCount"
              control={control}
              render={({ field }) => (
                <NumberInput
                  label={t("expense:advance.installmentCount")}
                  value={field.value}
                  onChange={(value) => field.onChange(typeof value === "number" ? value : 1)}
                  min={1}
                  max={12}
                />
              )}
            />
          )}
          <Controller
            name="description"
            control={control}
            render={({ field }) => <Textarea label={t("expense:advance.description")} autosize minRows={2} {...field} />}
          />
          <Group justify="flex-end" mt="sm">
            <Button type="button" variant="default" onClick={onClose}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" loading={createMutation.isPending}>
              {t("expense:advance.submit")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
