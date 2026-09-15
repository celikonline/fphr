import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Inbox as InboxIcon } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Textarea } from "@/components/ui/textarea";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import NoAccess from "@/components/no-access";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  decideTask,
  listInbox,
  overrideDecideTask,
  type DecideTaskPayload,
  type InboxTaskDto,
} from "@/services/workflow.service";

const INBOX_QUERY_KEY = ["workflow", "inbox"] as const;

export default function WorkflowInboxPage() {
  const canView = usePermission("workflow.inbox.view");
  if (!canView) return <NoAccess />;
  return <WorkflowInbox />;
}

function WorkflowInbox() {
  const { t } = useTranslation(["workflow", "common"]);
  const canOverride = usePermission("workflow.request.override");
  const queryClient = useQueryClient();

  const inboxQuery = useQuery({ queryKey: INBOX_QUERY_KEY, queryFn: listInbox });
  const items = inboxQuery.data ?? [];

  const [decideTarget, setDecideTarget] = useState<{ task: InboxTaskDto; override: boolean } | null>(null);

  function refresh() {
    queryClient.invalidateQueries({ queryKey: INBOX_QUERY_KEY });
  }

  return (
    <div className="mx-auto max-w-6xl space-y-6 p-6 md:p-10">
      <div>
        <h1 className="flex items-center gap-2 text-2xl font-bold">
          <InboxIcon className="h-6 w-6" /> {t("workflow:inbox.title")}
        </h1>
        <p className="text-muted-foreground">{t("workflow:inbox.subtitle")}</p>
      </div>

      <Card>
        <CardContent className="p-0">
          {inboxQuery.isLoading ? (
            <div className="space-y-2 p-4">
              {Array.from({ length: 4 }, (_, i) => (
                <Skeleton key={i} className="h-8 w-full" />
              ))}
            </div>
          ) : inboxQuery.isError ? (
            <p className="p-6 text-center text-sm text-destructive">
              {getApiErrorMessage(inboxQuery.error, t("common:errorGeneric"))}
            </p>
          ) : items.length === 0 ? (
            <p className="p-8 text-center text-sm text-muted-foreground">{t("workflow:inbox.empty")}</p>
          ) : (
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{t("workflow:inbox.requestType", { defaultValue: "Talep türü" })}</TableHead>
                    <TableHead>{t("workflow:inbox.subject", { defaultValue: "Kayıt" })}</TableHead>
                    <TableHead>{t("workflow:inbox.requester")}</TableHead>
                    <TableHead>{t("workflow:inbox.stepColumn", { defaultValue: "Adım" })}</TableHead>
                    <TableHead />
                    <TableHead className="text-right">{t("common:actions")}</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((task) => (
                    <TableRow key={task.taskId}>
                      <TableCell className="font-medium">{t(`workflow:requestType.${task.requestType}`)}</TableCell>
                      <TableCell className="max-w-xs truncate font-mono text-xs">{task.subjectId}</TableCell>
                      <TableCell>{task.requesterName ?? "—"}</TableCell>
                      <TableCell>{t("workflow:inbox.step", { number: task.stepNumber })}</TableCell>
                      <TableCell>
                        {task.isFallbackRoleTask && <Badge variant="outline">{t("workflow:inbox.fallbackRole")}</Badge>}
                      </TableCell>
                      <TableCell className="text-right">
                        <div className="flex justify-end gap-2">
                          <Button type="button" size="sm" onClick={() => setDecideTarget({ task, override: false })}>
                            {t("workflow:inbox.decide")}
                          </Button>
                          {canOverride && (
                            <Button
                              type="button"
                              size="sm"
                              variant="outline"
                              onClick={() => setDecideTarget({ task, override: true })}
                            >
                              {t("workflow:inbox.overrideDecide")}
                            </Button>
                          )}
                        </div>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
        </CardContent>
      </Card>

      {decideTarget && (
        <DecideDialog
          task={decideTarget.task}
          override={decideTarget.override}
          onClose={() => setDecideTarget(null)}
          onDecided={refresh}
        />
      )}
    </div>
  );
}

interface DecideDialogProps {
  task: InboxTaskDto;
  override: boolean;
  onClose: () => void;
  onDecided: () => void;
}

function DecideDialog({ task, override, onClose, onDecided }: DecideDialogProps) {
  const { t } = useTranslation(["workflow", "common"]);
  const [comment, setComment] = useState("");

  const mutation = useMutation({
    mutationFn: (outcome: DecideTaskPayload["outcome"]) => {
      const payload: DecideTaskPayload = { outcome, comment: comment.trim() || undefined };
      return override ? overrideDecideTask(task.taskId, payload) : decideTask(task.taskId, payload);
    },
    onSuccess: (_data, outcome) => {
      toast({
        variant: "success",
        title: t("common:success"),
        description: t(outcome === "approved" ? "workflow:inbox.approveSuccess" : "workflow:inbox.rejectSuccess"),
      });
      onDecided();
      onClose();
    },
    onError: (error) =>
      toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("common:errorGeneric")) }),
  });

  return (
    <Dialog open onOpenChange={(open) => !open && !mutation.isPending && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{override ? t("workflow:inbox.overrideDecide") : t("workflow:inbox.decide")}</DialogTitle>
          <DialogDescription>
            {t(`workflow:requestType.${task.requestType}`)} · {task.subjectId}
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-4">
          {override && <p className="text-sm text-muted-foreground">{t("workflow:inbox.overrideHint")}</p>}
          <div>
            <Label htmlFor="decide-comment">{t("workflow:inbox.comment")}</Label>
            <Textarea
              id="decide-comment"
              placeholder={t("workflow:inbox.commentPlaceholder")}
              value={comment}
              onChange={(e) => setComment(e.target.value)}
              disabled={mutation.isPending}
            />
          </div>
        </div>
        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button type="button" variant="destructive" onClick={() => mutation.mutate("rejected")} disabled={mutation.isPending}>
            {t("workflow:inbox.reject")}
          </Button>
          <Button type="button" onClick={() => mutation.mutate("approved")} disabled={mutation.isPending}>
            {t("workflow:inbox.approve")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
