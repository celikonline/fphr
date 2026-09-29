import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Banknote } from "lucide-react";
import { Badge, Button, Card, Group, Modal, Select, Skeleton, Stack, Tabs, Text, Textarea, Title } from "@mantine/core";
import { useMediaQuery } from "@mantine/hooks";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { formatDateOnly, formatDateTime } from "@/lib/dates";
import {
  listAdvanceRequests,
  listExpenseClaims,
  payAdvanceRequest,
  payExpenseClaim,
  type AdvanceRequest,
  type AdvanceRequestStatus,
  type ExpenseClaim,
  type ExpenseClaimStatus,
} from "@/services/expense.service";

const CLAIMS_KEY = ["expense", "claims", "admin"] as const;
const ADVANCES_KEY = ["expense", "advances", "admin"] as const;

const STATUS_COLOR: Record<string, string> = {
  submitted: "blue",
  approved: "teal",
  rejected: "red",
  paid: "green",
};

/**
 * HR/Finance ödeme iş listesi ("Faz 7 TB-6"): onaylı masraf/avans taleplerini görür ve "Öde" ile ExpenseClaim/
 * AdvanceRequest.MarkPaid'i tetikler. Onay kararının kendisi Workflow'da alınır - bu ekran yalnızca ödeme adımını
 * yürütür (bkz. ExpenseClaimsController.Pay / AdvancesController.Pay).
 */
export default function ExpensePaymentsAdminPage() {
  const canClaims = usePermission("expense.claims.view");
  const canAdvances = usePermission("expense.advances.view");
  if (!canClaims && !canAdvances) return <NoAccess />;
  return <ExpensePaymentsAdmin canClaims={canClaims} canAdvances={canAdvances} />;
}

function ExpensePaymentsAdmin({ canClaims, canAdvances }: { canClaims: boolean; canAdvances: boolean }) {
  const { t } = useTranslation(["expense", "common"]);

  return (
    <div className="w-full space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <Banknote size={24} /> {t("expense:admin.title")}
        </Title>
        <Text c="dimmed">{t("expense:admin.subtitle")}</Text>
      </div>

      <Tabs defaultValue={canClaims ? "claims" : "advances"}>
        <Tabs.List>
          {canClaims && <Tabs.Tab value="claims">{t("expense:admin.tabs.claims")}</Tabs.Tab>}
          {canAdvances && <Tabs.Tab value="advances">{t("expense:admin.tabs.advances")}</Tabs.Tab>}
        </Tabs.List>

        {canClaims && (
          <Tabs.Panel value="claims" pt="md">
            <ClaimsWorklist />
          </Tabs.Panel>
        )}
        {canAdvances && (
          <Tabs.Panel value="advances" pt="md">
            <AdvancesWorklist />
          </Tabs.Panel>
        )}
      </Tabs>
    </div>
  );
}

const CLAIM_STATUS_OPTIONS: ExpenseClaimStatus[] = ["submitted", "approved", "paid", "rejected"];
const ADVANCE_STATUS_OPTIONS: AdvanceRequestStatus[] = ["submitted", "approved", "paid", "rejected"];

function ClaimsWorklist() {
  const { t, i18n } = useTranslation(["expense", "common"]);
  const queryClient = useQueryClient();
  const canPay = usePermission("expense.claims.pay");
  // Dar telefon (Faz 7 gün açılışı tur 1 FE md.2): tablo yerine kart listesi, "Öde" aynı izin koşuluyla.
  const isMobile = useMediaQuery("(max-width: 390px)");
  const [status, setStatus] = useState<ExpenseClaimStatus>("approved");
  const [payTarget, setPayTarget] = useState<ExpenseClaim | null>(null);

  const claimsQuery = useQuery({ queryKey: [...CLAIMS_KEY, status], queryFn: () => listExpenseClaims(status) });
  const claims = useMemo(() => claimsQuery.data ?? [], [claimsQuery.data]);

  const columns: ColumnDef<ExpenseClaim>[] = [
    {
      id: "description",
      accessorKey: "description",
      header: t("expense:admin.col.description"),
      meta: { label: t("expense:admin.col.description") },
      cell: ({ row }) => row.original.description || "—",
    },
    {
      id: "submittedAt",
      accessorKey: "submittedAt",
      header: t("expense:admin.col.submittedAt"),
      meta: { label: t("expense:admin.col.submittedAt") },
      cell: ({ row }) => (row.original.submittedAt ? formatDateTime(row.original.submittedAt, i18n.language) : "—"),
    },
    {
      id: "totalAmount",
      accessorKey: "totalAmount",
      header: t("expense:admin.col.amount"),
      meta: { label: t("expense:admin.col.amount") },
      cell: ({ row }) =>
        `${row.original.totalAmount.toLocaleString(i18n.language)} ${row.original.items[0]?.currency ?? "TRY"}`,
    },
    {
      id: "status",
      accessorKey: "status",
      header: t("expense:admin.col.status"),
      meta: { label: t("expense:admin.col.status") },
      cell: ({ row }) => (
        <Badge color={STATUS_COLOR[row.original.status] ?? "gray"} variant="light">
          {t(`expense:status.${row.original.status}`, { defaultValue: row.original.status })}
        </Badge>
      ),
    },
    {
      id: "actions",
      header: "",
      meta: { label: t("common:actions"), headerClassName: "w-32" },
      enableSorting: false,
      cell: ({ row }) =>
        canPay &&
        row.original.status === "approved" && (
          <Button type="button" size="xs" variant="light" leftSection={<Banknote size={14} />} onClick={() => setPayTarget(row.original)}>
            {t("expense:admin.pay")}
          </Button>
        ),
    },
  ];

  const table = useDataTable({ columns, data: claims, getRowId: (r) => r.id });

  return (
    <Card withBorder padding="lg">
      <Group justify="flex-end" mb="md">
        <Select
          label={t("expense:admin.col.status")}
          data={CLAIM_STATUS_OPTIONS.map((value) => ({ value, label: t(`expense:status.${value}`) }))}
          value={status}
          onChange={(value) => value && setStatus(value as ExpenseClaimStatus)}
          allowDeselect={false}
          w={200}
        />
      </Group>
      {claimsQuery.isError ? (
        <Text size="sm" c="red" ta="center" p="lg">
          {getApiErrorMessage(claimsQuery.error, t("expense:genericError"))}
        </Text>
      ) : isMobile ? (
        <ClaimsMobileList
          claims={claims}
          isLoading={claimsQuery.isLoading}
          canPay={canPay}
          onPay={setPayTarget}
        />
      ) : (
        <DataTable table={table} isLoading={claimsQuery.isLoading} emptyMessage={t("expense:admin.empty")} />
      )}

      {payTarget && (
        <PayDialog
          amountLabel={`${payTarget.totalAmount.toLocaleString(i18n.language)} ${payTarget.items[0]?.currency ?? "TRY"}`}
          onClose={() => setPayTarget(null)}
          onConfirm={(note) => payExpenseClaim(payTarget.id, note)}
          onSuccess={() => {
            setPayTarget(null);
            void queryClient.invalidateQueries({ queryKey: CLAIMS_KEY });
            toast({ variant: "success", title: t("common:success"), description: t("expense:admin.paidSuccess") });
          }}
        />
      )}
    </Card>
  );
}

/** ≤390px kart listesi (Faz 7 gün açılışı tur 1 FE md.2): masaüstü tablonun aynı kolonları + "Öde" aksiyonu. */
function ClaimsMobileList({
  claims,
  isLoading,
  canPay,
  onPay,
}: {
  claims: ExpenseClaim[];
  isLoading: boolean;
  canPay: boolean;
  onPay: (claim: ExpenseClaim) => void;
}) {
  const { t, i18n } = useTranslation(["expense", "common"]);

  if (isLoading) return <Skeleton h={160} />;

  if (claims.length === 0) {
    return (
      <Text size="sm" c="dimmed" ta="center" p="lg">
        {t("expense:admin.empty")}
      </Text>
    );
  }

  return (
    <Stack gap="sm">
      {claims.map((claim) => (
        <Card key={claim.id} withBorder padding="sm" radius="md">
          <Group justify="space-between" align="flex-start" wrap="nowrap">
            <Text size="sm" truncate="end">
              {claim.description || "—"}
            </Text>
            <Badge color={STATUS_COLOR[claim.status] ?? "gray"} variant="light">
              {t(`expense:status.${claim.status}`, { defaultValue: claim.status })}
            </Badge>
          </Group>
          <Group justify="space-between" mt={4}>
            <Text size="xs" c="dimmed">
              {claim.submittedAt ? formatDateTime(claim.submittedAt, i18n.language) : "—"}
            </Text>
            <Text size="sm" fw={500}>
              {claim.totalAmount.toLocaleString(i18n.language)} {claim.items[0]?.currency ?? "TRY"}
            </Text>
          </Group>
          {canPay && claim.status === "approved" && (
            <Group justify="flex-end" mt="xs">
              <Button type="button" size="xs" variant="light" leftSection={<Banknote size={14} />} onClick={() => onPay(claim)}>
                {t("expense:admin.pay")}
              </Button>
            </Group>
          )}
        </Card>
      ))}
    </Stack>
  );
}

function AdvancesWorklist() {
  const { t, i18n } = useTranslation(["expense", "common"]);
  const queryClient = useQueryClient();
  const canPay = usePermission("expense.advances.pay");
  // Dar telefon (Faz 7 gün açılışı tur 1 FE md.2): tablo yerine kart listesi, "Öde" aynı izin koşuluyla.
  const isMobile = useMediaQuery("(max-width: 390px)");
  const [status, setStatus] = useState<AdvanceRequestStatus>("approved");
  const [payTarget, setPayTarget] = useState<AdvanceRequest | null>(null);

  const advancesQuery = useQuery({ queryKey: [...ADVANCES_KEY, status], queryFn: () => listAdvanceRequests(status) });
  const advances = useMemo(() => advancesQuery.data ?? [], [advancesQuery.data]);

  const columns: ColumnDef<AdvanceRequest>[] = [
    {
      id: "description",
      accessorKey: "description",
      header: t("expense:admin.col.description"),
      meta: { label: t("expense:admin.col.description") },
      cell: ({ row }) => row.original.description || "—",
    },
    {
      id: "requestedDate",
      accessorKey: "requestedDate",
      header: t("expense:admin.col.requestedDate"),
      meta: { label: t("expense:admin.col.requestedDate") },
      cell: ({ row }) => formatDateOnly(row.original.requestedDate, i18n.language),
    },
    {
      id: "amount",
      accessorKey: "amount",
      header: t("expense:admin.col.amount"),
      meta: { label: t("expense:admin.col.amount") },
      cell: ({ row }) => `${row.original.amount.toLocaleString(i18n.language)} ${row.original.currency}`,
    },
    {
      id: "status",
      accessorKey: "status",
      header: t("expense:admin.col.status"),
      meta: { label: t("expense:admin.col.status") },
      cell: ({ row }) => (
        <Badge color={STATUS_COLOR[row.original.status] ?? "gray"} variant="light">
          {t(`expense:status.${row.original.status}`, { defaultValue: row.original.status })}
        </Badge>
      ),
    },
    {
      id: "actions",
      header: "",
      meta: { label: t("common:actions"), headerClassName: "w-32" },
      enableSorting: false,
      cell: ({ row }) =>
        canPay &&
        row.original.status === "approved" && (
          <Button type="button" size="xs" variant="light" leftSection={<Banknote size={14} />} onClick={() => setPayTarget(row.original)}>
            {t("expense:admin.pay")}
          </Button>
        ),
    },
  ];

  const table = useDataTable({ columns, data: advances, getRowId: (r) => r.id });

  return (
    <Card withBorder padding="lg">
      <Group justify="flex-end" mb="md">
        <Select
          label={t("expense:admin.col.status")}
          data={ADVANCE_STATUS_OPTIONS.map((value) => ({ value, label: t(`expense:status.${value}`) }))}
          value={status}
          onChange={(value) => value && setStatus(value as AdvanceRequestStatus)}
          allowDeselect={false}
          w={200}
        />
      </Group>
      {advancesQuery.isError ? (
        <Text size="sm" c="red" ta="center" p="lg">
          {getApiErrorMessage(advancesQuery.error, t("expense:genericError"))}
        </Text>
      ) : isMobile ? (
        <AdvancesMobileList
          advances={advances}
          isLoading={advancesQuery.isLoading}
          canPay={canPay}
          onPay={setPayTarget}
        />
      ) : (
        <DataTable table={table} isLoading={advancesQuery.isLoading} emptyMessage={t("expense:admin.empty")} />
      )}

      {payTarget && (
        <PayDialog
          amountLabel={`${payTarget.amount.toLocaleString(i18n.language)} ${payTarget.currency}`}
          onClose={() => setPayTarget(null)}
          onConfirm={(note) => payAdvanceRequest(payTarget.id, note)}
          onSuccess={() => {
            setPayTarget(null);
            void queryClient.invalidateQueries({ queryKey: ADVANCES_KEY });
            toast({ variant: "success", title: t("common:success"), description: t("expense:admin.paidSuccess") });
          }}
        />
      )}
    </Card>
  );
}

/** ≤390px kart listesi (Faz 7 gün açılışı tur 1 FE md.2): masaüstü tablonun aynı kolonları + "Öde" aksiyonu. */
function AdvancesMobileList({
  advances,
  isLoading,
  canPay,
  onPay,
}: {
  advances: AdvanceRequest[];
  isLoading: boolean;
  canPay: boolean;
  onPay: (advance: AdvanceRequest) => void;
}) {
  const { t, i18n } = useTranslation(["expense", "common"]);

  if (isLoading) return <Skeleton h={160} />;

  if (advances.length === 0) {
    return (
      <Text size="sm" c="dimmed" ta="center" p="lg">
        {t("expense:admin.empty")}
      </Text>
    );
  }

  return (
    <Stack gap="sm">
      {advances.map((advance) => (
        <Card key={advance.id} withBorder padding="sm" radius="md">
          <Group justify="space-between" align="flex-start" wrap="nowrap">
            <Text size="sm" truncate="end">
              {advance.description || "—"}
            </Text>
            <Badge color={STATUS_COLOR[advance.status] ?? "gray"} variant="light">
              {t(`expense:status.${advance.status}`, { defaultValue: advance.status })}
            </Badge>
          </Group>
          <Group justify="space-between" mt={4}>
            <Text size="xs" c="dimmed">
              {formatDateOnly(advance.requestedDate, i18n.language)}
            </Text>
            <Text size="sm" fw={500}>
              {advance.amount.toLocaleString(i18n.language)} {advance.currency}
            </Text>
          </Group>
          {canPay && advance.status === "approved" && (
            <Group justify="flex-end" mt="xs">
              <Button type="button" size="xs" variant="light" leftSection={<Banknote size={14} />} onClick={() => onPay(advance)}>
                {t("expense:admin.pay")}
              </Button>
            </Group>
          )}
        </Card>
      ))}
    </Stack>
  );
}

function PayDialog({
  amountLabel,
  onClose,
  onConfirm,
  onSuccess,
}: {
  amountLabel: string;
  onClose: () => void;
  onConfirm: (note: string | null) => Promise<void>;
  onSuccess: () => void;
}) {
  const { t } = useTranslation(["expense", "common"]);
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: () => onConfirm(note.trim() || null),
    onSuccess,
    onError: (err) => setError(getApiErrorMessage(err, t("expense:genericError"))),
  });

  return (
    <Modal opened onClose={onClose} title={t("expense:admin.payDialogTitle")} size="sm">
      <Stack gap="md">
        <Text size="sm">
          {t("expense:admin.payAmount")}: <strong>{amountLabel}</strong>
        </Text>
        <Textarea
          label={t("expense:admin.paymentNote")}
          placeholder={t("expense:admin.paymentNotePlaceholder")}
          value={note}
          onChange={(event) => setNote(event.currentTarget.value)}
          maxLength={1000}
          autosize
          minRows={2}
        />
        {error && (
          <Text size="sm" c="red">
            {error}
          </Text>
        )}
        <Group justify="flex-end" gap="sm">
          <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button type="button" loading={mutation.isPending} onClick={() => mutation.mutate()}>
            {t("expense:admin.confirmPay")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
