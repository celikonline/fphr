import { useRef } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Download, FileSpreadsheet, KeyRound, Loader2, Upload } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from "@/components/ui/card";
import { FileDropzone, type FileDropzoneHandle } from "@/components/ui/file-dropzone";
import { usePermission } from "@/components/permission-guard";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  DataTransferPermissions,
  IMPORT_ALLOWED_EXTENSIONS,
  IMPORT_MAX_FILE_BYTES,
  downloadImportTemplate,
  isBlockingColumn,
  saveDownloadedFile,
  uploadImportFile,
  type ImportTemplate,
} from "@/services/data-transfer.service";
import { useApiLanguage } from "./use-api-language";

interface TemplateCardProps {
  template: ImportTemplate;
}

export default function TemplateCard({ template }: TemplateCardProps) {
  const { t } = useTranslation(["dataTransfer", "common"]);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const lang = useApiLanguage();
  const canDownload = usePermission(DataTransferPermissions.TemplateDownload);
  const canRun = usePermission(DataTransferPermissions.Run);
  const dropzoneRef = useRef<FileDropzoneHandle>(null);

  const downloadMutation = useMutation({
    mutationFn: () => downloadImportTemplate(template.code, lang),
    onSuccess: saveDownloadedFile,
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("dataTransfer:templates.downloadError")),
      }),
  });

  const uploadMutation = useMutation({
    mutationFn: (file: File) => uploadImportFile(template.code, file),
    onSuccess: (job) => {
      queryClient.invalidateQueries({ queryKey: ["datatransfer", "import-jobs"] });
      queryClient.setQueryData(["datatransfer", "import-job", job.id], job);
      navigate(`/app/settings/data-transfer/jobs/${job.id}`);
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("dataTransfer:upload.error")),
      }),
  });

  return (
    <Card className="flex flex-col">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          <FileSpreadsheet className="h-5 w-5 text-primary" /> {template.name}
        </CardTitle>
        {template.description && <CardDescription>{template.description}</CardDescription>}
      </CardHeader>
      <CardContent className="flex-1 space-y-4">
        <div>
          <p className="mb-2 text-xs font-medium uppercase text-muted-foreground">{t("dataTransfer:templates.columns")}</p>
          <div className="flex flex-wrap gap-1.5">
            {template.columns.map((column) => (
              <Badge
                key={column.key}
                variant={isBlockingColumn(column) ? "secondary" : "outline"}
                className="gap-1 font-normal"
                title={column.isMatchKey ? t("dataTransfer:templates.matchKey") : undefined}
              >
                {column.isMatchKey && <KeyRound className="h-3 w-3" />}
                {column.header}
                {isBlockingColumn(column) && <span className="text-destructive">*</span>}
              </Badge>
            ))}
          </div>
          <p className="mt-2 flex flex-wrap items-center gap-3 text-xs text-muted-foreground">
            <span>
              <span className="text-destructive">*</span> {t("dataTransfer:templates.requiredLegend")}
            </span>
            <span className="inline-flex items-center gap-1">
              <KeyRound className="h-3 w-3" /> {t("dataTransfer:templates.matchKeyLegend")}
            </span>
          </p>
        </div>

        {canRun && (
          <FileDropzone
            ref={dropzoneRef}
            accept={IMPORT_ALLOWED_EXTENSIONS.join(",")}
            maxBytes={IMPORT_MAX_FILE_BYTES}
            busy={uploadMutation.isPending}
            title={t("dataTransfer:upload.dropHint")}
            hint={t("dataTransfer:upload.limits")}
            busyLabel={t("dataTransfer:upload.uploading")}
            onFile={(file) => uploadMutation.mutate(file)}
            onReject={(reason) =>
              toast({
                variant: "destructive",
                title: t("common:error"),
                description: t(reason === "type" ? "dataTransfer:upload.invalidType" : "dataTransfer:upload.tooLarge"),
              })
            }
          />
        )}
      </CardContent>
      <CardFooter className="flex flex-wrap gap-2">
        {canDownload && (
          <Button
            type="button"
            variant="outline"
            onClick={() => downloadMutation.mutate()}
            disabled={downloadMutation.isPending}
          >
            {downloadMutation.isPending ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <Download className="mr-2 h-4 w-4" />
            )}
            {t("dataTransfer:templates.download")}
          </Button>
        )}
        {canRun && (
          <Button type="button" onClick={() => dropzoneRef.current?.open()} disabled={uploadMutation.isPending}>
            <Upload className="mr-2 h-4 w-4" /> {t("dataTransfer:templates.upload")}
          </Button>
        )}
      </CardFooter>
    </Card>
  );
}
