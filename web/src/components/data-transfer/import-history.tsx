import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { useQuery } from "@tanstack/react-query";
import { Card, CardContent } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import PaginationFooter from "@/components/pagination-footer";
import { formatDateTime } from "@/lib/dates";
import { listImportJobs, type ImportTemplate } from "@/services/data-transfer.service";
import { JobStatusBadge } from "./job-badges";

interface ImportHistoryProps {
  templates: ImportTemplate[] | undefined;
}

export default function ImportHistory({ templates }: ImportHistoryProps) {
  const { t, i18n } = useTranslation(["dataTransfer", "common"]);
  const navigate = useNavigate();
  const [page, setPage] = useState(1);

  const { data, isLoading, isError } = useQuery({
    queryKey: ["datatransfer", "import-jobs", page],
    queryFn: () => listImportJobs({ page, pageSize: 20 }),
  });

  const templateName = (code: string) => templates?.find((tpl) => tpl.code === code)?.name ?? code;

  return (
    <Card>
      <CardContent className="p-0">
        {isLoading ? (
          <div className="space-y-2 p-4">
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
          </div>
        ) : isError ? (
          <p className="p-6 text-center text-sm text-destructive">{t("dataTransfer:history.loadError")}</p>
        ) : !data || data.items.length === 0 ? (
          <p className="p-6 text-center text-sm text-muted-foreground">{t("dataTransfer:history.empty")}</p>
        ) : (
          <>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("dataTransfer:history.template")}</TableHead>
                  <TableHead>{t("dataTransfer:history.file")}</TableHead>
                  <TableHead>{t("dataTransfer:history.mode")}</TableHead>
                  <TableHead>{t("dataTransfer:history.status")}</TableHead>
                  <TableHead className="text-right">{t("dataTransfer:counts.inserted")}</TableHead>
                  <TableHead className="text-right">{t("dataTransfer:counts.updated")}</TableHead>
                  <TableHead className="text-right">{t("dataTransfer:counts.skipped")}</TableHead>
                  <TableHead className="text-right">{t("dataTransfer:counts.errors")}</TableHead>
                  <TableHead>{t("dataTransfer:history.date")}</TableHead>
                  <TableHead>{t("dataTransfer:history.user")}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.items.map((job) => (
                  <TableRow
                    key={job.id}
                    className="cursor-pointer"
                    tabIndex={0}
                    onClick={() => navigate(`/app/settings/data-transfer/jobs/${job.id}`)}
                    onKeyDown={(e) => e.key === "Enter" && navigate(`/app/settings/data-transfer/jobs/${job.id}`)}
                  >
                    <TableCell className="font-medium">{templateName(job.templateCode)}</TableCell>
                    <TableCell className="max-w-[16rem] truncate" title={job.fileName}>
                      {job.fileName}
                    </TableCell>
                    <TableCell>{t(`dataTransfer:mode.${job.mode}`, job.mode)}</TableCell>
                    <TableCell>
                      <JobStatusBadge status={job.status} />
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{job.insertedCount}</TableCell>
                    <TableCell className="text-right tabular-nums">{job.updatedCount}</TableCell>
                    <TableCell className="text-right tabular-nums">{job.skippedCount}</TableCell>
                    <TableCell className={job.errorCount > 0 ? "text-right tabular-nums text-destructive" : "text-right tabular-nums"}>
                      {job.errorCount}
                    </TableCell>
                    <TableCell className="whitespace-nowrap">{formatDateTime(job.createdAt, i18n.language)}</TableCell>
                    <TableCell>{job.requestedByName ?? "—"}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
            <PaginationFooter result={data} onPageChange={setPage} />
          </>
        )}
      </CardContent>
    </Card>
  );
}
