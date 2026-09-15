import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Search, ShieldCheck, ShieldOff, Users as UsersIcon, MoreHorizontal } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { Checkbox } from "@/components/ui/checkbox";
import { Separator } from "@/components/ui/separator";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import PaginationFooter from "@/components/pagination-footer";
import { toast } from "@/hooks/use-toast";
import { usePermission } from "@/components/permission-guard";
import {
  bulkAssignRole,
  deriveAccessType,
  listPermissionGroups,
  listRoles,
  listUsers,
  setUserAccessType,
  setUserRoles,
  setUserStatus,
  type AccessType,
  type UserListItem,
  type UserStatus,
} from "@/services/users.service";

const ALL_VALUE = "__all__";
const PAGE_SIZE = 10;
/** Demo dataset is ~50 users (Item 18 seed) - fetched once and filtered/paginated client-side. */
const FETCH_ALL_SIZE = 500;

const STATUS_BADGE: Record<UserStatus, "success" | "secondary" | "destructive" | "outline"> = {
  active: "success",
  invited: "secondary",
  locked: "destructive",
  disabled: "outline",
};

export default function UsersRolesPage() {
  const { t, i18n } = useTranslation(["users", "common"]);
  const canManage = usePermission("identity.users.manage");

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <div>
        <h1 className="flex items-center gap-2 text-2xl font-bold">
          <UsersIcon className="h-6 w-6" /> {t("users:title")}
        </h1>
        <p className="text-muted-foreground">{t("users:subtitle")}</p>
      </div>

      <Tabs defaultValue="users">
        <TabsList>
          <TabsTrigger value="users">{t("users:tabUsers")}</TabsTrigger>
          <TabsTrigger value="roles">{t("users:tabRoles")}</TabsTrigger>
        </TabsList>
        <TabsContent value="users">
          <UsersTab canManage={canManage} t={t} locale={i18n.language} />
        </TabsContent>
        <TabsContent value="roles">
          <RolesTab t={t} />
        </TabsContent>
      </Tabs>
    </div>
  );
}

type TFn = (key: string, opts?: Record<string, unknown>) => string;

function UsersTab({ canManage, t, locale }: { canManage: boolean; t: TFn; locale: string }) {
  const queryClient = useQueryClient();

  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState(ALL_VALUE);
  const [roleFilter, setRoleFilter] = useState(ALL_VALUE);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [bulkRoleId, setBulkRoleId] = useState<string>("");
  const [rolesDialogUser, setRolesDialogUser] = useState<UserListItem | null>(null);
  const [accessTypeDialogUser, setAccessTypeDialogUser] = useState<UserListItem | null>(null);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), 300);
    return () => clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    setPage(1);
    setSelected(new Set());
  }, [debouncedSearch, statusFilter, roleFilter]);

  const { data: allUsers, isLoading } = useQuery({
    queryKey: ["users-all"],
    queryFn: () => listUsers({ page: 1, pageSize: FETCH_ALL_SIZE, sort: "lastName" }),
  });

  const { data: roles } = useQuery({ queryKey: ["roles"], queryFn: listRoles });

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

  const totalCount = filtered.length;
  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const pageItems = filtered.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE);
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

  const allOnPageSelected = pageItems.length > 0 && pageItems.every((u) => selected.has(u.id));
  const selectedUsers = (allUsers?.items ?? []).filter((u) => selected.has(u.id));

  function formatLastLogin(value: string | null) {
    if (!value) return t("users:never");
    return new Date(value).toLocaleString(locale === "tr" ? "tr-TR" : "en-US");
  }

  return (
    <div className="space-y-4 pt-4">
      <div className="flex flex-wrap items-center gap-3">
        <div className="relative max-w-sm flex-1">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            className="pl-9"
            placeholder={t("users:searchPlaceholder")}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <Select value={statusFilter} onValueChange={setStatusFilter}>
          <SelectTrigger className="w-44">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL_VALUE}>{t("users:allStatuses")}</SelectItem>
            {(["invited", "active", "locked", "disabled"] as UserStatus[]).map((s) => (
              <SelectItem key={s} value={s}>
                {t(`users:status.${s}`)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Select value={roleFilter} onValueChange={setRoleFilter}>
          <SelectTrigger className="w-56">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL_VALUE}>{t("users:allRoles")}</SelectItem>
            {roles?.map((r) => (
              <SelectItem key={r.id} value={r.id}>
                {r.name ?? r.code}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {canManage && selected.size > 0 && (
        <div className="flex flex-wrap items-center gap-3 rounded-md border border-border bg-muted/40 p-3">
          <span className="text-sm font-medium">{t("users:selectedCount", { count: selected.size })}</span>
          <Select value={bulkRoleId} onValueChange={setBulkRoleId}>
            <SelectTrigger className="w-56">
              <SelectValue placeholder={t("users:bulkAssignRole")} />
            </SelectTrigger>
            <SelectContent>
              {roles?.map((r) => (
                <SelectItem key={r.id} value={r.id}>
                  {r.name ?? r.code}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Button
            type="button"
            size="sm"
            disabled={!bulkRoleId || bulkMutation.isPending}
            onClick={() => bulkMutation.mutate({ users: selectedUsers, roleId: bulkRoleId })}
          >
            {t("users:assign")}
          </Button>
        </div>
      )}

      <Card>
        <CardContent className="p-0">
          {isLoading ? (
            <div className="space-y-2 p-4">
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-8 w-full" />
              <Skeleton className="h-8 w-full" />
            </div>
          ) : pageItems.length === 0 ? (
            <p className="p-6 text-center text-sm text-muted-foreground">{t("users:empty")}</p>
          ) : (
            <>
              <Table>
                <TableHeader>
                  <TableRow>
                    {canManage && (
                      <TableHead className="w-10">
                        <Checkbox
                          checked={allOnPageSelected}
                          onCheckedChange={(v) => toggleSelectAllOnPage(Boolean(v))}
                          aria-label={t("common:actions")}
                        />
                      </TableHead>
                    )}
                    <TableHead>{t("users:name")}</TableHead>
                    <TableHead>{t("users:email")}</TableHead>
                    <TableHead>{t("users:statusFilter")}</TableHead>
                    <TableHead>{t("users:accessType")}</TableHead>
                    <TableHead>{t("users:roles")}</TableHead>
                    <TableHead>{t("users:mfa")}</TableHead>
                    <TableHead>{t("users:lastLogin")}</TableHead>
                    {canManage && <TableHead className="text-right">{t("common:actions")}</TableHead>}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {pageItems.map((user) => (
                    <TableRow key={user.id}>
                      {canManage && (
                        <TableCell>
                          <Checkbox
                            checked={selected.has(user.id)}
                            onCheckedChange={(v) => toggleSelect(user.id, Boolean(v))}
                            aria-label={`${user.firstName} ${user.lastName}`}
                          />
                        </TableCell>
                      )}
                      <TableCell className="font-medium">
                        {user.firstName} {user.lastName}
                      </TableCell>
                      <TableCell className="text-muted-foreground">{user.email}</TableCell>
                      <TableCell>
                        <Badge variant={STATUS_BADGE[user.status]}>{t(`users:status.${user.status}`)}</Badge>
                      </TableCell>
                      <TableCell>
                        {(() => {
                          const accessType = deriveAccessType(user.roles);
                          return (
                            <Badge variant="outline">
                              {accessType ? t(`users:accessType.${accessType.toLowerCase()}`) : t("users:accessType.none")}
                            </Badge>
                          );
                        })()}
                      </TableCell>
                      <TableCell>
                        <div className="flex flex-wrap gap-1">
                          {user.roles.slice(0, 2).map((r) => (
                            <Badge key={r.id} variant="outline">
                              {r.name ?? r.code}
                            </Badge>
                          ))}
                          {user.roles.length > 2 && <Badge variant="outline">+{user.roles.length - 2}</Badge>}
                        </div>
                      </TableCell>
                      <TableCell>
                        {user.mfaEnabled ? (
                          <ShieldCheck className="h-4 w-4 text-emerald-600" aria-label={t("users:mfa")} />
                        ) : (
                          <ShieldOff className="h-4 w-4 text-muted-foreground" aria-label={t("users:mfa")} />
                        )}
                      </TableCell>
                      <TableCell className="text-muted-foreground">{formatLastLogin(user.lastLoginAt)}</TableCell>
                      {canManage && (
                        <TableCell className="text-right">
                          <DropdownMenu>
                            <DropdownMenuTrigger asChild>
                              <Button type="button" variant="ghost" size="icon">
                                <MoreHorizontal className="h-4 w-4" />
                              </Button>
                            </DropdownMenuTrigger>
                            <DropdownMenuContent align="end">
                              <DropdownMenuItem onClick={() => setAccessTypeDialogUser(user)}>
                                {t("users:editAccessType")}
                              </DropdownMenuItem>
                              <DropdownMenuItem onClick={() => setRolesDialogUser(user)}>
                                {t("users:editRoles")}
                              </DropdownMenuItem>
                              <DropdownMenuItem
                                onClick={() =>
                                  statusMutation.mutate({ id: user.id, enabled: user.status === "disabled" })
                                }
                              >
                                {user.status === "disabled" ? t("users:activate") : t("users:deactivate")}
                              </DropdownMenuItem>
                            </DropdownMenuContent>
                          </DropdownMenu>
                        </TableCell>
                      )}
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
              <PaginationFooter result={pageResult} onPageChange={setPage} />
            </>
          )}
        </CardContent>
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
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("users:dialogEditAccessTypeTitle", { name: `${user.firstName} ${user.lastName}` })}</DialogTitle>
        </DialogHeader>
        <p className="text-sm text-muted-foreground">{t("users:accessTypeHint")}</p>
        <Select value={selected} onValueChange={(v) => setSelected(v as AccessType)}>
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {ACCESS_TYPES.map((type) => (
              <SelectItem key={type} value={type}>
                {t(`users:accessType.${type.toLowerCase()}`)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose} disabled={isSaving}>
            {t("common:cancel")}
          </Button>
          <Button type="button" onClick={() => onSave(selected)} disabled={isSaving}>
            {isSaving ? t("common:saving") : t("common:save")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
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
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{t("users:dialogEditRolesTitle", { name: `${user.firstName} ${user.lastName}` })}</DialogTitle>
        </DialogHeader>
        <div className="max-h-80 space-y-2 overflow-y-auto">
          {roles.map((role) => {
            const checked = selectedIds.has(role.id);
            return (
              <label key={role.id} className="flex items-center gap-2 rounded-md p-2 text-sm hover:bg-accent">
                <Checkbox
                  checked={checked}
                  onCheckedChange={(v) => {
                    setSelectedIds((prev) => {
                      const next = new Set(prev);
                      if (v) next.add(role.id);
                      else next.delete(role.id);
                      return next;
                    });
                  }}
                />
                {role.name ?? role.code}
              </label>
            );
          })}
        </div>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose} disabled={isSaving}>
            {t("common:cancel")}
          </Button>
          <Button type="button" onClick={() => onSave(Array.from(selectedIds))} disabled={isSaving}>
            {isSaving ? t("common:saving") : t("common:save")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function RolesTab({ t }: { t: TFn }) {
  const { data: roles, isLoading } = useQuery({ queryKey: ["roles"], queryFn: listRoles });
  const { data: permissionGroups } = useQuery({ queryKey: ["permission-groups"], queryFn: listPermissionGroups });
  const [selectedRoleId, setSelectedRoleId] = useState<string | null>(null);

  useEffect(() => {
    if (!selectedRoleId && roles && roles.length > 0) setSelectedRoleId(roles[0].id);
  }, [roles, selectedRoleId]);

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
      <div className="space-y-2 pt-4">
        <Skeleton className="h-8 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  return (
    <div className="grid grid-cols-1 gap-4 pt-4 md:grid-cols-3">
      <Card className="md:col-span-1">
        <CardContent className="max-h-[32rem] space-y-1 overflow-y-auto p-2">
          {roles?.map((role) => (
            <button
              key={role.id}
              type="button"
              onClick={() => setSelectedRoleId(role.id)}
              className={`flex w-full flex-col rounded-md px-3 py-2 text-left text-sm transition-colors hover:bg-accent ${
                role.id === selectedRoleId ? "bg-accent font-medium" : ""
              }`}
            >
              <span>{role.name ?? role.code}</span>
              <span className="text-xs text-muted-foreground">{t("users:userCount", { count: role.userCount })}</span>
            </button>
          ))}
        </CardContent>
      </Card>

      <Card className="md:col-span-2">
        <CardContent className="space-y-4 p-4">
          {!selectedRole ? (
            <p className="text-sm text-muted-foreground">{t("users:selectRoleHint")}</p>
          ) : (
            <>
              <div className="flex flex-wrap items-center gap-2">
                <h2 className="text-lg font-semibold">{selectedRole.name ?? selectedRole.code}</h2>
                <Badge variant={selectedRole.isSystem ? "secondary" : "outline"}>
                  {selectedRole.isSystem ? t("users:systemRole") : t("users:customRole")}
                </Badge>
              </div>
              {selectedRole.description && (
                <p className="text-sm text-muted-foreground">{selectedRole.description}</p>
              )}
              <Separator />
              <div>
                <h3 className="mb-2 text-sm font-semibold">{t("users:permissionsGranted")}</h3>
                {selectedRole.permissions.length === 0 ? (
                  <p className="text-sm text-muted-foreground">{t("users:noPermissions")}</p>
                ) : (
                  <ul className="space-y-1.5">
                    {selectedRole.permissions.map((perm) => (
                      <li
                        key={perm.permission}
                        className="flex items-center justify-between gap-2 rounded-md border border-border px-3 py-1.5 text-sm"
                      >
                        <span>{permissionMap.get(perm.permission)?.displayName ?? perm.permission}</span>
                        <Badge variant="outline" className="shrink-0">
                          {t(`users:scope.${perm.scope}`)}
                        </Badge>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
