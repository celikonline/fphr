import { useMemo, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef } from "@tanstack/react-table";
import { Building2, Plus } from "lucide-react";
import {
  Badge,
  Button,
  Card,
  Group,
  Modal,
  PasswordInput,
  SegmentedControl,
  Stack,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import { DataTable, useDataTable } from "@/components/ui/data-table";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiProblem } from "@/lib/api-error";
import { formatDateTime } from "@/lib/dates";
import { isValidTenantSlug, suggestTenantSlug, TENANT_SLUG_MAX_LENGTH } from "@/lib/tenant-slug";
import {
  PLATFORM_PERMISSIONS,
  createTenant,
  listTenants,
  type CreateTenantInput,
  type TenantOwnerMode,
  type TenantStatus,
  type TenantSummary,
} from "@/services/platform.service";

const TENANTS_QUERY_KEY = ["platform", "tenants"] as const;

const STATUS_COLOR: Record<TenantStatus, string> = {
  trial: "blue",
  active: "green",
  suspended: "orange",
  closed: "gray",
};

const MIN_PASSWORD_LENGTH = 10;

function tenantErrorMessage(error: unknown, t: (key: string) => string): string {
  const code = getApiProblem(error)?.code;
  if (code === "tenant.slug_taken") return t("platform:errors.slugTaken");
  if (code === "tenant.invalid_slug") return t("platform:errors.slugInvalid");
  return getApiErrorMessage(error, t("platform:errors.generic"));
}

export default function PlatformTenantsPage() {
  const canManage = usePermission(PLATFORM_PERMISSIONS.tenantsManage);
  if (!canManage) return <NoAccess />;
  return <TenantsAdmin />;
}

function TenantStatusBadge({ status }: { status: TenantStatus }) {
  const { t } = useTranslation("platform");
  return (
    <Badge variant="light" color={STATUS_COLOR[status] ?? "gray"}>
      {t(`status.${status}`)}
    </Badge>
  );
}

function TenantsAdmin() {
  const { t, i18n } = useTranslation(["platform", "common"]);
  const [dialogOpen, setDialogOpen] = useState(false);

  const tenantsQuery = useQuery({ queryKey: TENANTS_QUERY_KEY, queryFn: listTenants });
  const tenants = useMemo(() => tenantsQuery.data ?? [], [tenantsQuery.data]);

  const columns: ColumnDef<TenantSummary>[] = [
    {
      id: "name",
      accessorKey: "name",
      header: t("platform:columns.name"),
      meta: { label: t("platform:columns.name") },
      cell: ({ row }) => (
        <Group gap="xs" wrap="nowrap">
          <Text size="sm">{row.original.name}</Text>
          {row.original.isPlatform && (
            <Badge variant="outline" color="gray">
              {t("platform:platformBadge")}
            </Badge>
          )}
        </Group>
      ),
    },
    {
      id: "slug",
      accessorKey: "slug",
      header: t("platform:columns.slug"),
      meta: { label: t("platform:columns.slug") },
      cell: ({ row }) => (
        <Text size="sm" ff="monospace">
          {row.original.slug}
        </Text>
      ),
    },
    {
      id: "status",
      accessorKey: "status",
      header: t("platform:columns.status"),
      meta: { label: t("platform:columns.status"), headerClassName: "w-36" },
      cell: ({ row }) => <TenantStatusBadge status={row.original.status} />,
    },
    {
      id: "createdAt",
      accessorKey: "createdAt",
      header: t("platform:columns.createdAt"),
      meta: { label: t("platform:columns.createdAt"), headerClassName: "w-48" },
      cell: ({ row }) => (
        <Text size="sm">{formatDateTime(row.original.createdAt, i18n.language)}</Text>
      ),
    },
  ];

  const table = useDataTable({ columns, data: tenants, getRowId: (row) => row.id });

  return (
    <div className="max-w-6xl space-y-6 p-6 md:p-10">
      <Group justify="space-between" align="flex-start" gap="sm">
        <div>
          <Title order={1} size="h2" className="flex items-center gap-2">
            <Building2 size={24} /> {t("platform:title")}
          </Title>
          <Text c="dimmed">{t("platform:subtitle")}</Text>
        </div>
        <Button type="button" leftSection={<Plus size={16} />} onClick={() => setDialogOpen(true)}>
          {t("platform:add")}
        </Button>
      </Group>

      <Card withBorder padding="lg">
        {tenantsQuery.isError ? (
          <Text size="sm" c="red" ta="center" p="lg">
            {t("platform:errors.loadFailed")}
          </Text>
        ) : (
          <>
            {/* Geniş ekran: tablo */}
            <div className="hidden md:block">
              <DataTable
                table={table}
                isLoading={tenantsQuery.isLoading}
                emptyMessage={t("platform:empty")}
              />
            </div>

            {/* Dar ekran: kart listesi */}
            <div className="md:hidden">
              {tenantsQuery.isLoading ? (
                <Text size="sm" c="dimmed">
                  {t("common:loading")}
                </Text>
              ) : tenants.length === 0 ? (
                <Text size="sm" c="dimmed" ta="center" p="lg">
                  {t("platform:empty")}
                </Text>
              ) : (
                <Stack gap="sm">
                  {tenants.map((tenant) => (
                    <Card key={tenant.id} withBorder padding="sm" radius="md">
                      <Group justify="space-between" align="flex-start" wrap="nowrap">
                        <div>
                          <Text fw={500} size="sm">
                            {tenant.name}
                          </Text>
                          <Text size="xs" c="dimmed" ff="monospace">
                            {tenant.slug}
                          </Text>
                        </div>
                        <TenantStatusBadge status={tenant.status} />
                      </Group>
                    </Card>
                  ))}
                </Stack>
              )}
            </div>
          </>
        )}
      </Card>

      {dialogOpen && <CreateTenantDialog onClose={() => setDialogOpen(false)} />}
    </div>
  );
}

function CreateTenantDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation(["platform", "common"]);
  const queryClient = useQueryClient();

  const [companyName, setCompanyName] = useState("");
  const [slug, setSlug] = useState("");
  // The slug follows the company name until the operator edits it by hand.
  const [slugEdited, setSlugEdited] = useState(false);
  const [ownerFirstName, setOwnerFirstName] = useState("");
  const [ownerLastName, setOwnerLastName] = useState("");
  const [ownerEmail, setOwnerEmail] = useState("");
  const [phone, setPhone] = useState("");
  const [ownerMode, setOwnerMode] = useState<TenantOwnerMode>("invite");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: (input: CreateTenantInput) => createTenant(input),
    onSuccess: (result) => {
      void queryClient.invalidateQueries({ queryKey: TENANTS_QUERY_KEY });
      toast({
        variant: "success",
        title: t("common:success"),
        description: result.invitationSent
          ? t("platform:created.invited")
          : t("platform:created.withPassword"),
      });
      onClose();
    },
    onError: (err) => setError(tenantErrorMessage(err, t)),
  });

  function handleCompanyNameChange(value: string) {
    setCompanyName(value);
    if (!slugEdited) setSlug(suggestTenantSlug(value));
  }

  function validate(): string | null {
    if (!companyName.trim()) return t("platform:errors.companyRequired");
    if (!isValidTenantSlug(slug)) return t("platform:errors.slugInvalid");
    if (!ownerFirstName.trim() || !ownerLastName.trim())
      return t("platform:errors.ownerNameRequired");
    if (!/^\S+@\S+\.\S+$/.test(ownerEmail.trim())) return t("platform:errors.ownerEmailInvalid");
    if (ownerMode === "password" && password.length < MIN_PASSWORD_LENGTH)
      return t("platform:errors.passwordTooShort");
    return null;
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    const problem = validate();
    setError(problem);
    if (problem) return;

    mutation.mutate({
      companyName: companyName.trim(),
      slug,
      ownerEmail: ownerEmail.trim(),
      ownerFirstName: ownerFirstName.trim(),
      ownerLastName: ownerLastName.trim(),
      ownerMode,
      password: ownerMode === "password" ? password : undefined,
      phone: phone.trim() || undefined,
    });
  }

  return (
    <Modal opened onClose={onClose} title={t("platform:dialog.title")} size="36rem">
      <form onSubmit={handleSubmit}>
        <Stack gap="md">
          <TextInput
            label={t("platform:dialog.companyName")}
            withAsterisk
            value={companyName}
            maxLength={200}
            onChange={(event) => handleCompanyNameChange(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
          <TextInput
            label={t("platform:dialog.slug")}
            description={t("platform:dialog.slugHint")}
            withAsterisk
            value={slug}
            maxLength={TENANT_SLUG_MAX_LENGTH}
            onChange={(event) => {
              setSlugEdited(true);
              setSlug(event.currentTarget.value.toLowerCase());
            }}
            disabled={mutation.isPending}
            inputWrapperOrder={["label", "input", "description"]}
          />

          <Text fw={600} size="sm" mt="xs">
            {t("platform:dialog.ownerSection")}
          </Text>
          <Group grow align="flex-start">
            <TextInput
              label={t("platform:dialog.ownerFirstName")}
              withAsterisk
              value={ownerFirstName}
              maxLength={100}
              onChange={(event) => setOwnerFirstName(event.currentTarget.value)}
              disabled={mutation.isPending}
            />
            <TextInput
              label={t("platform:dialog.ownerLastName")}
              withAsterisk
              value={ownerLastName}
              maxLength={100}
              onChange={(event) => setOwnerLastName(event.currentTarget.value)}
              disabled={mutation.isPending}
            />
          </Group>
          <TextInput
            label={t("platform:dialog.ownerEmail")}
            type="email"
            withAsterisk
            value={ownerEmail}
            onChange={(event) => setOwnerEmail(event.currentTarget.value)}
            disabled={mutation.isPending}
          />
          <TextInput
            label={t("platform:dialog.ownerPhone")}
            value={phone}
            onChange={(event) => setPhone(event.currentTarget.value)}
            disabled={mutation.isPending}
          />

          <div>
            <Text size="sm" fw={500} mb={4}>
              {t("platform:dialog.ownerMode")}
            </Text>
            <SegmentedControl
              fullWidth
              value={ownerMode}
              onChange={(value) => setOwnerMode(value as TenantOwnerMode)}
              disabled={mutation.isPending}
              data={[
                { value: "invite", label: t("platform:dialog.modeInvite") },
                { value: "password", label: t("platform:dialog.modePassword") },
              ]}
            />
            <Text size="xs" c="dimmed" mt={4}>
              {ownerMode === "invite"
                ? t("platform:dialog.modeInviteHint")
                : t("platform:dialog.modePasswordHint")}
            </Text>
          </div>

          {ownerMode === "password" && (
            <PasswordInput
              label={t("platform:dialog.password")}
              description={t("platform:dialog.passwordHint")}
              withAsterisk
              value={password}
              autoComplete="new-password"
              onChange={(event) => setPassword(event.currentTarget.value)}
              disabled={mutation.isPending}
              inputWrapperOrder={["label", "input", "description"]}
            />
          )}

          {error && (
            <Text size="sm" c="red" role="alert">
              {error}
            </Text>
          )}
          <Group justify="flex-end" gap="sm">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="submit" loading={mutation.isPending}>
              {t("platform:dialog.submit")}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  );
}
