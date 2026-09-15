import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link, useParams } from "react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Lock, Mail, MoreVertical, SearchX } from "lucide-react";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import ConfirmDialog from "@/components/confirm-dialog";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiErrorStatus } from "@/lib/api-error";
import { getEmployee, getMyEmployee, type EmployeeDetail } from "@/services/employees.service";
import { useAuthStore } from "@/store/auth.store";
import { inviteEmployee } from "@/services/invitations.service";
import { AccountStatusBadge, EmployeeStatusBadge, initials } from "@/components/employees/employee-badges";
import { AccountSection, CareerSection, GeneralSection } from "@/components/employees/employee-card-sections";
import { useOrgUnits } from "@/components/employees/use-org-units";

export default function EmployeeCardPage() {
  const { id = "" } = useParams();
  const { t } = useTranslation(["employees", "common"]);

  const canReadEmployees = usePermission("employee.employees.read");
  const ownEmployeeId = useAuthStore((state) => state.user?.employeeId);
  // Employee-role users can't read arbitrary cards, but may open their own via /employees/me.
  const isOwnCard = !canReadEmployees && Boolean(ownEmployeeId) && id === ownEmployeeId;

  const { data: employee, isLoading, error } = useQuery({
    queryKey: ["employees", "detail", id],
    queryFn: () => (isOwnCard ? getMyEmployee() : getEmployee(id)),
    enabled: Boolean(id),
    retry: (count, err) => {
      const status = getApiErrorStatus(err);
      return status !== 404 && status !== 403 && count < 1;
    },
  });

  if (isLoading) {
    return (
      <div className="mx-auto max-w-5xl space-y-4 p-6 md:p-10">
        <Skeleton className="h-20 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  if (error || !employee) {
    const status = getApiErrorStatus(error);
    const forbidden = status === 403;
    const notFound = status === 404 || (!error && !employee);
    return (
      <div className="mx-auto max-w-3xl p-6 md:p-10">
        <Card>
          <CardContent className="flex flex-col items-center gap-3 p-10 text-center">
            {forbidden ? <Lock className="h-8 w-8 text-muted-foreground" /> : <SearchX className="h-8 w-8 text-muted-foreground" />}
            <p className="font-medium">
              {forbidden ? t("common:noAccessTitle") : notFound ? t("employees:card.notFoundTitle") : t("common:error")}
            </p>
            <p className="text-sm text-muted-foreground">
              {forbidden
                ? t("common:noAccessDescription")
                : notFound
                  ? t("employees:card.notFoundDescription")
                  : getApiErrorMessage(error, t("employees:genericError"))}
            </p>
            <Button asChild variant="outline">
              <Link to="/app/employees">{t("employees:card.backToList")}</Link>
            </Button>
          </CardContent>
        </Card>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-6 md:p-10">
      <Button asChild variant="ghost" size="sm" className="-ml-3">
        <Link to="/app/employees">
          <ArrowLeft className="mr-2 h-4 w-4" /> {t("employees:card.backToList")}
        </Link>
      </Button>
      <CardHeaderBlock key={`header-${employee.id}`} employee={employee} />
      <GeneralSection key={`general-${employee.id}`} employee={employee} />
      <CareerSection key={`career-${employee.id}`} employee={employee} />
      <AccountSection key={`account-${employee.id}-${employee.accessType ?? ""}`} employee={employee} />
    </div>
  );
}

function CardHeaderBlock({ employee }: { employee: EmployeeDetail }) {
  const { t } = useTranslation(["employees", "common"]);
  const queryClient = useQueryClient();
  const { nameById } = useOrgUnits();
  const canInvite = usePermission("identity.users.invite");
  const [confirmOpen, setConfirmOpen] = useState(false);

  const targetEmail = employee.workEmail ?? employee.personalEmail;
  const targetSourceKey = employee.workEmail ? "work" : "personal";
  const showInvite = canInvite && employee.accountStatus !== "active";
  const inviteLabel = employee.accountStatus === "invited" ? t("employees:invite.resend") : t("employees:invite.send");

  const mutation = useMutation({
    mutationFn: () => inviteEmployee(employee.id),
    onSuccess: (result) => {
      setConfirmOpen(false);
      queryClient.invalidateQueries({ queryKey: ["employees"] });
      // Tolerates both "AlreadyActive" and camelCase enum serialization.
      const outcome = String(result.outcome).toLowerCase();
      toast({
        variant: outcome === "alreadyactive" ? "default" : "success",
        title: t(`employees:invite.outcome.${outcome}`, { defaultValue: String(result.outcome) }),
        description: result.sentToEmail ? t("employees:invite.sentTo", { email: result.sentToEmail }) : undefined,
      });
    },
    onError: (error) => {
      setConfirmOpen(false);
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("employees:genericError")) });
    },
  });

  const unitName = employee.orgUnitId ? nameById.get(employee.orgUnitId) : undefined;

  return (
    <Card>
      <CardContent className="flex flex-wrap items-center gap-4 p-6">
        <Avatar className="h-16 w-16">
          <AvatarFallback className="text-lg">{initials(employee.firstName, employee.lastName)}</AvatarFallback>
        </Avatar>
        <div className="min-w-0 flex-1">
          <h1 className="truncate text-2xl font-bold">{employee.displayName}</h1>
          <div className="mt-1 flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            {unitName && <span>{unitName}</span>}
            <EmployeeStatusBadge status={employee.status} />
            <AccountStatusBadge status={employee.accountStatus} />
          </div>
        </div>
        {showInvite && (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button type="button" variant="ghost" size="icon" title={t("common:actions")}>
                <MoreVertical className="h-5 w-5" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-64">
              <DropdownMenuLabel>{t("common:actions")}</DropdownMenuLabel>
              <DropdownMenuItem disabled={!targetEmail} onSelect={() => setConfirmOpen(true)}>
                <Mail className="mr-2 h-4 w-4" /> {inviteLabel}
              </DropdownMenuItem>
              {!targetEmail && <p className="px-2 pb-2 text-xs text-muted-foreground">{t("employees:invite.emailRequiredHint")}</p>}
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </CardContent>

      <ConfirmDialog
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title={inviteLabel}
        description={t(`employees:invite.confirm.${targetSourceKey}`, { email: targetEmail ?? "", name: employee.displayName })}
        confirmLabel={t("common:yes")}
        confirmVariant="default"
        isLoading={mutation.isPending}
        onConfirm={() => mutation.mutate()}
      />
    </Card>
  );
}
