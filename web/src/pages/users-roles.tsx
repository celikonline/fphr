import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ColumnDef, SortingState } from "@tanstack/react-table";
import { Search, ShieldCheck, ShieldOff, Users as UsersIcon, MoreHorizontal } from "lucide-react";
import {
  ActionIcon,
  Badge,
  Button,
  Card,
  Checkbox,
  Divider,
  Group,
  Menu,
  Modal,
  NavLink,
  Paper,
  ScrollArea,
  Select,
  Skeleton,
  Stack,
  Tabs,
  Text,
  TextInput,
  Title,
  type BadgeProps,
} from "@mantine/core";
import { DataTable, DataTableViewOptions, useDataTable } from "@/components/ui/data-table";
import { ConfirmModal } from "@/components/ui/confirm-modal";
import PaginationFooter from "@/components/pagination-footer";
import { toast } from "@/hooks/use-toast";
import { usePermission } from "@/components/permission-guard";
import { getApiProblem } from "@/lib/api-error";
import { enumLabel } from "@/lib/i18n-enum";
import {
  bulkAssignRole,
  deriveAccessType,
  listPermissionGroups,
  listRoles,
  listUsers,
  resetUserMfa,
  setUserAccessType,
  setUserRoles,
  setUserStatus,
  type AccessType,
  type UserListItem,
  type UserStatus,
} from "@/services/users.service";

const ALL_VALUE = "__all__";
const DEFAULT_PAGE_SIZE = 10;
const COLUMN_VISIBILITY_KEY = "senseik.users.grid.columns";
/** Demo dataset is ~50 users (Item 18 seed) - fetched once and filtered/sorted/paginated client-side. */
const FETCH_ALL_SIZE = 500;

type UserComparer = (a: UserListItem, b: UserListItem, collator: Intl.Collator) => number;

/** Client-side sort per column id; the whole user list is loaded, so sorting spans every page. */
const SORT_COMPARERS: Record<string, UserComparer> = {
  name: (a, b, c) => c.compare(a.lastName, b.lastName) || c.compare(a.firstName, b.firstName),
  email: (a, b, c) => c.compare(a.email, b.email),
  status: (a, b, c) => c.compare(a.status, b.status),
  accessType: (a, b, c) => c.compare(deriveAccessType(a.roles) ?? "", deriveAccessType(b.roles) ?? ""),
  lastLogin: (a, b) => (a.lastLoginAt ?? "").localeCompare(b.lastLoginAt ?? ""),
};

const STATUS_BADGE: Record<UserStatus, Pick<BadgeProps, "variant" | "color">> = {
  active: { variant: "filled", color: "green" },
  invited: { variant: "light", color: "gray" },
  locked: { variant: "filled", color: "red" },
  disabled: { variant: "outline", color: "gray" },
};

const USER_STATUSES: UserStatus[] = ["invited", "active", "locked", "disabled"];

export default function UsersRolesPage() {
  const { t, i18n } = useTranslation(["users", "common"]);
  const canManage = usePermission("identity.users.manage");

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <div>
        <Title order={1} size="h2" className="flex items-center gap-2">
          <UsersIcon className="h-6 w-6" /> {t("users:title")}
        </Title>
        <Text c="dimmed">{t("users:subtitle")}</Text>
      </div>

      <Tabs defaultValue="users" keepMounted={false}>
        <Tabs.List>
          <Tabs.Tab value="users">{t("users:tabUsers")}</Tabs.Tab>
          <Tabs.Tab value="roles">{t("users:tabRoles")}</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="users">
          <UsersTab canManage={canManage} t={t} locale={i18n.language} />
        </Tabs.Panel>
        <Tabs.Panel value="roles">
          <RolesTab t={t} />
        </Tabs.Panel>
      </Tabs>
    </div>
  );
}

type TFn = (key: string, opts?: Record<string, unknown>) => string;

/** Aksiyon menüsü — masaüstü tablo satırı ve mobil kart listesi arasında paylaşılır (aynı koşullar/aynı öğeler). */
function UserActionsMenu({
  user,
  t,
  onEditAccessType,
  onEditRoles,
  onToggleStatus,
  onResetMfa,
}: {
  user: UserListItem;
  t: TFn;
  onEditAccessType: () => void;
  onEditRoles: () => void;
  onToggleStatus: () => void;
  onResetMfa: () => void;
}) {
  return (
    <Menu position="bottom-end" withinPortal>
      <Menu.Target>
        <ActionIcon type="button" variant="subtle" color="gray" aria-label={t("common:actions")}>
          <MoreHorizontal className="h-4 w-4" />
        </ActionIcon>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Item onClick={onEditAccessType}>{t("users:editAccessType")}</Menu.Item>
        <Menu.Item onClick={onEditRoles}>{t("users:editRoles")}</Menu.Item>
        <Menu.Item onClick={onToggleStatus}>
          {user.status === "disabled" ? t("users:activate") : t("users:deactivate")}
        </Menu.Item>
        {user.mfaEnabled && <Menu.Item onClick={onResetMfa}>{t("users:mfaReset.action")}</Menu.Item>}
      </Menu.Dropdown>
    </Menu>
  );
}

function UsersTab({ canManage, t, locale }: { canManage: boolean; t: TFn; locale: string }) {
  const queryClient = useQueryClient();

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [sort, setSort] = useState<{ id: string; desc: boolean }>({ id: "name", desc: false });
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState(ALL_VALUE);
  const [roleFilter, setRoleFilter] = useState(ALL_VALUE);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [bulkRoleId, setBulkRoleId] = useState<string>("");
  const [rolesDialogUser, setRolesDialogUser] = useState<UserListItem | null>(null);
  const [accessTypeDialogUser, setAccessTypeDialogUser] = useState<UserListItem | null>(null);
  const [statusDialogUser, setStatusDialogUser] = useState<UserListItem | null>(null);
  const [mfaResetDialogUser, setMfaResetDialogUser] = useState<UserListItem | null>(null);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), 300);
    return () => clearTimeout(timer);
  }, [search]);

  // New filters start from page 1 with an empty selection (adjusted during render, not in an effect).
  const filterKey = JSON.stringify([debouncedSearch, statusFilter, roleFilter]);
  const [prevFilterKey, setPrevFilterKey] = useState(filterKey);
  if (prevFilterKey !== filterKey) {
    setPrevFilterKey(filterKey);
    setPage(1);
    setSelected(new Set());
  }

  const { data: allUsers, isLoading } = useQuery({
    queryKey: ["users-all"],
    queryFn: () => listUsers({ page: 1, pageSize: FETCH_ALL_SIZE, sort: "lastName" }),
  });

  const { data: roles } = useQuery({ queryKey: ["roles"], queryFn: listRoles });

  const roleOptions = useMemo(() => (roles ?? []).map((r) => ({ value: r.id, label: r.name ?? r.code })), [roles]);

  const filtered = useMemo(() => {
    const items = allUsers?.items ?? [];
    const needle = debouncedSearch.trim().toLowerCase();
    return items.filter((u) => {
      const matchesSearch =
        !needle ||
        `${u.firstName} ${u.lastName} ${u.email}`.toLowerCase().includes(needle);
      const matchesStatus = statusFilter === ALL_VALUE || u.status === statusFilter;
      const matchesRole = roleFilter === ALL_VALUE || u.roles.some((r) => r.id === roleFilter);
      return matchesSearch && matchesStatus && matchesRole;
    });
  }, [allUsers, debouncedSearch, statusFilter, roleFilter]);

  const sorted = useMemo(() => {
    const compare = SORT_COMPARERS[sort.id];
    if (!compare) return filtered;
    const collator = new Intl.Collator(locale, { sensitivity: "base" });
    const direction = sort.desc ? -1 : 1;
    return [...filtered].sort((a, b) => direction * compare(a, b, collator));
  }, [filtered, sort, locale]);

  const totalCount = sorted.length;
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  const pageItems = useMemo(() => sorted.slice((page - 1) * pageSize, page * pageSize), [sorted, page, pageSize]);
  const pageResult = {
    page,
    totalPages,
    totalCount,
    hasNext: page < totalPages,
    hasPrevious: page > 1,
  };

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["users-all"] });

  const statusMutation = useMutation({
    mutationFn: ({ id, enabled }: { id: string; enabled: boolean }) => setUserStatus(id, enabled),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("users:statusUpdateSuccess") });
    },
    onError: () => toast({ variant: "destructive", title: t("common:error"), description: t("users:genericError") }),
  });

  const rolesMutation = useMutation({
    mutationFn: ({ id, roleIds }: { id: string; roleIds: string[] }) => setUserRoles(id, roleIds),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("users:rolesUpdateSuccess") });
      setRolesDialogUser(null);
    },
    onError: () => toast({ variant: "destructive", title: t("common:error"), description: t("users:genericError") }),
  });

  const accessTypeMutation = useMutation({
    mutationFn: ({ id, accessType }: { id: string; accessType: AccessType }) => setUserAccessType(id, accessType),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("users:accessTypeUpdateSuccess") });
      setAccessTypeDialogUser(null);
    },
    onError: () => toast({ variant: "destructive", title: t("common:error"), description: t("users:genericError") }),
  });

  /** `identity.mfa.self_reset_forbidden` (S-12): the signed-in admin appears in their own list and tried to
   * reset their own MFA from here — the security card (self-service disable) is the right place for that. */
  const mfaResetMutation = useMutation({
    mutationFn: (id: string) => resetUserMfa(id),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("users:mfaReset.success") });
    },
    onError: (err: unknown) => {
      const code = getApiProblem(err)?.code;
      const description =
        code === "identity.mfa.self_reset_forbidden" ? t("users:mfaReset.errors.selfForbidden") : t("users:mfaReset.errors.generic");
      toast({ variant: "destructive", title: t("common:error"), description });
    },
  });

  const bulkMutation = useMutation({
    mutationFn: ({ users, roleId }: { users: UserListItem[]; roleId: string }) => bulkAssignRole(users, roleId),
    onSuccess: () => {
      invalidate();
      toast({ variant: "success", title: t("common:success"), description: t("users:bulkAssignSuccess") });
      setSelected(new Set());
      setBulkRoleId("");
    },
    onError: () => toast({ variant: "destructive", title: t("common:error"), description: t("users:genericError") }),
  });

  function toggleSelectAllOnPage(checked: boolean) {
    setSelected((prev) => {
      const next = new Set(prev);
      for (const u of pageItems) {
        if (checked) next.add(u.id);
        else next.delete(u.id);
      }
      return next;
    });
  }

  function toggleSelect(id: string, checked: boolean) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (checked) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  const selectedOnPage = pageItems.filter((u) => selected.has(u.id)).length;
  const allOnPageSelected = pageItems.length > 0 && selectedOnPage === pageItems.length;
  const selectedUsers = (allUsers?.items ?? []).filter((u) => selected.has(u.id));

  function formatLastLogin(value: string | null) {
    if (!value) return t("users:never");
    return new Date(value).toLocaleString(locale === "tr" ? "tr-TR" : "en-US");
  }

  function changeSorting(next: SortingState) {
    const [first] = next;
    if (!first) return;
    setSort({ id: first.id, desc: first.desc });
    setPage(1);
  }

  function changePageSize(size: number) {
    setPageSize(size);
    setPage(1);
  }

  const columns: ColumnDef<UserListItem>[] = [
    ...(canManage
      ? [
          {
            id: "select",
            enableSorting: false,
            enableHiding: false,
            meta: { headerClassName: "w-10" },
            header: () => (
              <Checkbox
                checked={allOnPageSelected}
                indeterminate={!allOnPageSelected && selectedOnPage > 0}
                onChange={(e) => toggleSelectAllOnPage(e.currentTarget.checked)}
                aria-label={t("common:actions")}
              />
            ),
            cell: ({ row }) => (
              <div onClick={(e) => e.stopPropagation()}>
                <Checkbox
                  checked={selected.has(row.original.id)}
                  onChange={(e) => toggleSelect(row.original.id, e.currentTarget.checked)}
                  aria-label={`${row.original.firstName} ${row.original.lastName}`}
                />
              </div>
            ),
          } satisfies ColumnDef<UserListItem>,
        ]
      : []),
    {
      id: "name",
      accessorFn: (user) => `${user.lastName} ${user.firstName}`,
      enableHiding: false,
      header: t("users:name"),
      meta: { label: t("users:name") },
      cell: ({ row }) => (
        <Text size="sm" fw={500}>
          {row.original.firstName} {row.original.lastName}
        </Text>
      ),
    },
    {
      id: "email",
      accessorKey: "email",
      header: t("users:email"),
      meta: { label: t("users:email") },
      cell: ({ row }) => (
        <Text size="sm" c="dimmed">
          {row.original.email}
        </Text>
      ),
    },
    {
      id: "status",
      accessorKey: "status",
      header: t("users:statusFilter"),
      meta: { label: t("users:statusFilter") },
      cell: ({ row }) => (
        <Badge {...STATUS_BADGE[row.original.status]}>{t(`users:status.${row.original.status}`)}</Badge>
      ),
    },
    {
      id: "accessType",
      accessorFn: (user) => deriveAccessType(user.roles) ?? "",
      header: t("users:accessType"),
      meta: { label: t("users:accessType") },
      cell: ({ row }) => {
        const accessType = deriveAccessType(row.original.roles);
        return (
          <Badge variant="outline" color="gray">
            {accessType ? enumLabel(t, "users:accessType", accessType) : t("users:accessType.none")}
          </Badge>
        );
      },
    },
    {
      id: "roles",
      enableSorting: false,
      header: t("users:roles"),
      meta: { label: t("users:roles") },
      cell: ({ row }) => (
        <Group gap={4}>
          {row.original.roles.slice(0, 2).map((r) => (
            <Badge key={r.id} variant="outline" color="gray">
              {r.name ?? r.code}
            </Badge>
          ))}
          {row.original.roles.length > 2 && (
            <Badge variant="outline" color="gray">
              +{row.original.roles.length - 2}
            </Badge>
          )}
        </Group>
      ),
    },
    {
      id: "mfa",
      enableSorting: false,
      header: t("users:mfa"),
      meta: { label: t("users:mfa") },
      cell: ({ row }) =>
        row.original.mfaEnabled ? (
          <Text component="span" c="green" className="inline-flex">
            <ShieldCheck className="h-4 w-4" aria-label={t("users:mfa")} />
          </Text>
        ) : (
          <Text component="span" c="dimmed" className="inline-flex">
            <ShieldOff className="h-4 w-4" aria-label={t("users:mfa")} />
          </Text>
        ),
    },
    {
      id: "lastLogin",
      accessorFn: (user) => user.lastLoginAt ?? "",
      header: t("users:lastLogin"),
      meta: { label: t("users:lastLogin") },
      cell: ({ row }) => (
        <Text size="sm" c="dimmed">
          {formatLastLogin(row.original.lastLoginAt)}
        </Text>
      ),
    },
    ...(canManage
      ? [
          {
            id: "actions",
            enableSorting: false,
            enableHiding: false,
            header: t("common:actions"),
            meta: { headerClassName: "text-right", cellClassName: "text-right" },
            cell: ({ row }) => {
              const user = row.original;
              return (
                <div onClick={(e) => e.stopPropagation()}>
                  <UserActionsMenu
                    user={user}
                    t={t}
                    onEditAccessType={() => setAccessTypeDialogUser(user)}
                    onEditRoles={() => setRolesDialogUser(user)}
                    onToggleStatus={() => setStatusDialogUser(user)}
                    onResetMfa={() => setMfaResetDialogUser(user)}
                  />
                </div>
              );
            },
          } satisfies ColumnDef<UserListItem>,
        ]
      : []),
  ];

  const table = useDataTable({
    columns,
    data: pageItems,
    getRowId: (user) => user.id,
    sorting: [{ id: sort.id, desc: sort.desc }],
    onSortingChange: changeSorting,
    columnVisibilityKey: COLUMN_VISIBILITY_KEY,
  });

  return (
    <div className="space-y-4 pt-4">
      <Group gap="sm">
        <TextInput
          className="max-w-sm flex-1"
          leftSection={<Search className="h-4 w-4" />}
          placeholder={t("users:searchPlaceholder")}
          aria-label={t("users:searchPlaceholder")}
          value={search}
          onChange={(e) => setSearch(e.currentTarget.value)}
        />
        <Select
          className="w-44"
          aria-label={t("users:statusFilter")}
          value={statusFilter}
          onChange={(value) => setStatusFilter(value ?? ALL_VALUE)}
          allowDeselect={false}
          data={[
            { value: ALL_VALUE, label: t("users:allStatuses") },
            ...USER_STATUSES.map((s) => ({ value: s, label: t(`users:status.${s}`) })),
          ]}
        />
        <Select
          className="w-56"
          aria-label={t("users:roleFilter")}
          value={roleFilter}
          onChange={(value) => setRoleFilter(value ?? ALL_VALUE)}
          allowDeselect={false}
          data={[{ value: ALL_VALUE, label: t("users:allRoles") }, ...roleOptions]}
        />
        <DataTableViewOptions table={table} className="ml-auto" />
      </Group>

      {canManage && selected.size > 0 && (
        <Paper withBorder p="sm" bg="var(--mantine-color-default-hover)">
          <Group gap="sm">
            <Text size="sm" fw={500}>
              {t("users:selectedCount", { count: selected.size })}
            </Text>
            <Select
              className="w-56"
              aria-label={t("users:bulkAssignRole")}
              placeholder={t("users:bulkAssignRole")}
              value={bulkRoleId || null}
              onChange={(value) => setBulkRoleId(value ?? "")}
              data={roleOptions}
            />
            <Button
              type="button"
              size="sm"
              disabled={!bulkRoleId || bulkMutation.isPending}
              onClick={() => bulkMutation.mutate({ users: selectedUsers, roleId: bulkRoleId })}
            >
              {t("users:assign")}
            </Button>
          </Group>
        </Paper>
      )}

      <Card withBorder padding={0}>
        {/* Geniş ekran: tablo */}
        <div className="hidden md:block">
          <DataTable
            table={table}
            isLoading={isLoading}
            emptyMessage={t("users:empty")}
            isRowSelected={(user) => selected.has(user.id)}
          />
        </div>

        {/* Dar ekran (≤390px dahil): kart listesi */}
        <div className="md:hidden">
          {isLoading ? (
            <Stack gap="sm" p="sm">
              <Skeleton h={72} />
              <Skeleton h={72} />
              <Skeleton h={72} />
            </Stack>
          ) : pageItems.length === 0 ? (
            <Text size="sm" c="dimmed" ta="center" p="lg">
              {t("users:empty")}
            </Text>
          ) : (
            <Stack gap="sm" p="sm">
              {pageItems.map((user) => (
                <Card key={user.id} withBorder padding="sm" radius="md">
                  <Group justify="space-between" wrap="nowrap" align="flex-start">
                    <Stack gap={2} className="min-w-0">
                      <Text fw={500} size="sm" truncate>
                        {user.firstName} {user.lastName}
                      </Text>
                      <Text size="xs" c="dimmed" truncate>
                        {user.email}
                      </Text>
                    </Stack>
                    {canManage && (
                      <UserActionsMenu
                        user={user}
                        t={t}
                        onEditAccessType={() => setAccessTypeDialogUser(user)}
                        onEditRoles={() => setRolesDialogUser(user)}
                        onToggleStatus={() => setStatusDialogUser(user)}
                        onResetMfa={() => setMfaResetDialogUser(user)}
                      />
                    )}
                  </Group>
                  <Group gap={4} mt="xs" wrap="wrap">
                    <Badge {...STATUS_BADGE[user.status]}>{t(`users:status.${user.status}`)}</Badge>
                    {user.roles.slice(0, 2).map((r) => (
                      <Badge key={r.id} variant="outline" color="gray">
                        {r.name ?? r.code}
                      </Badge>
                    ))}
                    {user.roles.length > 2 && (
                      <Badge variant="outline" color="gray">
                        +{user.roles.length - 2}
                      </Badge>
                    )}
                    {user.mfaEnabled ? (
                      <Badge
                        variant="light"
                        color="green"
                        leftSection={<ShieldCheck className="h-3 w-3" />}
                      >
                        {t("users:mfa")}
                      </Badge>
                    ) : (
                      <Badge
                        variant="outline"
                        color="gray"
                        leftSection={<ShieldOff className="h-3 w-3" />}
                      >
                        {t("users:mfa")}
                      </Badge>
                    )}
                  </Group>
                </Card>
              ))}
            </Stack>
          )}
        </div>

        {!isLoading && (
          <PaginationFooter
            result={pageResult}
            onPageChange={setPage}
            pageSize={pageSize}
            onPageSizeChange={changePageSize}
          />
        )}
      </Card>

      {rolesDialogUser && (
        <EditRolesDialog
          user={rolesDialogUser}
          roles={roles ?? []}
          onClose={() => setRolesDialogUser(null)}
          onSave={(roleIds) => rolesMutation.mutate({ id: rolesDialogUser.id, roleIds })}
          isSaving={rolesMutation.isPending}
          t={t}
        />
      )}

      {accessTypeDialogUser && (
        <EditAccessTypeDialog
          user={accessTypeDialogUser}
          onClose={() => setAccessTypeDialogUser(null)}
          onSave={(accessType) => accessTypeMutation.mutate({ id: accessTypeDialogUser.id, accessType })}
          isSaving={accessTypeMutation.isPending}
          t={t}
        />
      )}

      {statusDialogUser && (
        <ConfirmModal
          opened
          onClose={() => setStatusDialogUser(null)}
          title={
            statusDialogUser.status === "disabled" ? t("users:activateConfirmTitle") : t("users:deactivateConfirmTitle")
          }
          message={
            statusDialogUser.status === "disabled"
              ? t("users:activateConfirmMessage", { name: `${statusDialogUser.firstName} ${statusDialogUser.lastName}` })
              : t("users:deactivateConfirmMessage", { name: `${statusDialogUser.firstName} ${statusDialogUser.lastName}` })
          }
          confirmLabel={statusDialogUser.status === "disabled" ? t("users:activate") : t("users:deactivate")}
          danger={statusDialogUser.status !== "disabled"}
          onConfirm={() =>
            statusMutation.mutateAsync({ id: statusDialogUser.id, enabled: statusDialogUser.status === "disabled" })
          }
        />
      )}

      {mfaResetDialogUser && (
        <ConfirmModal
          opened
          onClose={() => setMfaResetDialogUser(null)}
          title={t("users:mfaReset.confirmTitle")}
          message={t("users:mfaReset.confirmMessage", {
            name: `${mfaResetDialogUser.firstName} ${mfaResetDialogUser.lastName}`,
          })}
          confirmLabel={t("users:mfaReset.action")}
          danger
          onConfirm={() => mfaResetMutation.mutateAsync(mfaResetDialogUser.id)}
        />
      )}
    </div>
  );
}

const ACCESS_TYPES: AccessType[] = ["Employee", "Owner", "Admin"];

function EditAccessTypeDialog({
  user,
  onClose,
  onSave,
  isSaving,
  t,
}: {
  user: UserListItem;
  onClose: () => void;
  onSave: (accessType: AccessType) => void;
  isSaving: boolean;
  t: TFn;
}) {
  const [selected, setSelected] = useState<AccessType>(deriveAccessType(user.roles) ?? "Employee");

  return (
    <Modal
      opened
      onClose={onClose}
      title={t("users:dialogEditAccessTypeTitle", { name: `${user.firstName} ${user.lastName}` })}
    >
      <Stack>
        <Select
          label={t("users:accessType")}
          description={t("users:accessTypeHint")}
          value={selected}
          onChange={(value) => value && setSelected(value as AccessType)}
          allowDeselect={false}
          data={ACCESS_TYPES.map((type) => ({ value: type, label: enumLabel(t, "users:accessType", type) }))}
        />
        <Group justify="flex-end">
          <Button type="button" variant="default" onClick={onClose} disabled={isSaving}>
            {t("common:cancel")}
          </Button>
          <Button type="button" onClick={() => onSave(selected)} disabled={isSaving}>
            {isSaving ? t("common:saving") : t("common:save")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function EditRolesDialog({
  user,
  roles,
  onClose,
  onSave,
  isSaving,
  t,
}: {
  user: UserListItem;
  roles: { id: string; code: string; name: string | null }[];
  onClose: () => void;
  onSave: (roleIds: string[]) => void;
  isSaving: boolean;
  t: TFn;
}) {
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set(user.roles.map((r) => r.id)));

  return (
    <Modal
      opened
      onClose={onClose}
      title={t("users:dialogEditRolesTitle", { name: `${user.firstName} ${user.lastName}` })}
    >
      <Stack>
        <ScrollArea.Autosize mah={320}>
          <Stack gap="sm" p={4}>
            {roles.map((role) => (
              <Checkbox
                key={role.id}
                label={role.name ?? role.code}
                checked={selectedIds.has(role.id)}
                onChange={(e) => {
                  const checked = e.currentTarget.checked;
                  setSelectedIds((prev) => {
                    const next = new Set(prev);
                    if (checked) next.add(role.id);
                    else next.delete(role.id);
                    return next;
                  });
                }}
              />
            ))}
          </Stack>
        </ScrollArea.Autosize>
        <Group justify="flex-end">
          <Button type="button" variant="default" onClick={onClose} disabled={isSaving}>
            {t("common:cancel")}
          </Button>
          <Button type="button" onClick={() => onSave(Array.from(selectedIds))} disabled={isSaving}>
            {isSaving ? t("common:saving") : t("common:save")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function RolesTab({ t }: { t: TFn }) {
  const { data: roles, isLoading } = useQuery({ queryKey: ["roles"], queryFn: listRoles });
  const { data: permissionGroups } = useQuery({ queryKey: ["permission-groups"], queryFn: listPermissionGroups });
  const [selectedRoleId, setSelectedRoleId] = useState<string | null>(null);

  // Default to the first role once roles load (guarded adjustment during render, not in an effect).
  if (!selectedRoleId && roles && roles.length > 0) setSelectedRoleId(roles[0].id);

  const permissionMap = useMemo(() => {
    const map = new Map<string, { displayName: string; group: string }>();
    for (const group of permissionGroups ?? []) {
      for (const perm of group.permissions) {
        map.set(perm.key, { displayName: perm.displayName, group: group.group });
      }
    }
    return map;
  }, [permissionGroups]);

  const selectedRole = roles?.find((r) => r.id === selectedRoleId) ?? null;

  if (isLoading) {
    return (
      <Stack gap="xs" pt="md">
        <Skeleton h={32} />
        <Skeleton h={256} />
      </Stack>
    );
  }

  return (
    <div className="grid grid-cols-1 gap-4 pt-4 md:grid-cols-3">
      <Card withBorder padding="xs" className="md:col-span-1">
        <ScrollArea.Autosize mah="32rem">
          <Stack gap={4}>
            {roles?.map((role) => (
              <NavLink
                key={role.id}
                component="button"
                type="button"
                onClick={() => setSelectedRoleId(role.id)}
                active={role.id === selectedRoleId}
                variant="light"
                label={role.name ?? role.code}
                description={t("users:userCount", { count: role.userCount })}
              />
            ))}
          </Stack>
        </ScrollArea.Autosize>
      </Card>

      <Card withBorder padding="md" className="md:col-span-2">
        {!selectedRole ? (
          <Text size="sm" c="dimmed">
            {t("users:selectRoleHint")}
          </Text>
        ) : (
          <Stack>
            <Group gap="xs">
              <Title order={2} size="h4">
                {selectedRole.name ?? selectedRole.code}
              </Title>
              {selectedRole.isSystem ? (
                <Badge variant="light" color="gray">
                  {t("users:systemRole")}
                </Badge>
              ) : (
                <Badge variant="outline" color="gray">
                  {t("users:customRole")}
                </Badge>
              )}
            </Group>
            {selectedRole.description && (
              <Text size="sm" c="dimmed">
                {selectedRole.description}
              </Text>
            )}
            <Divider />
            <div>
              <Title order={3} size="h6" mb="xs">
                {t("users:permissionsGranted")}
              </Title>
              {selectedRole.permissions.length === 0 ? (
                <Text size="sm" c="dimmed">
                  {t("users:noPermissions")}
                </Text>
              ) : (
                <Stack component="ul" gap={6}>
                  {selectedRole.permissions.map((perm) => (
                    <Paper component="li" key={perm.permission} withBorder px="sm" py={6}>
                      <Group justify="space-between" gap="xs" wrap="nowrap">
                        <Text size="sm">{permissionMap.get(perm.permission)?.displayName ?? perm.permission}</Text>
                        <Badge variant="outline" color="gray" className="shrink-0">
                          {t(`users:scope.${perm.scope}`)}
                        </Badge>
                      </Group>
                    </Paper>
                  ))}
                </Stack>
              )}
            </div>
          </Stack>
        )}
      </Card>
    </div>
  );
}
