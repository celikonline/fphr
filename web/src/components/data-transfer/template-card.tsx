import { useRef } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Badge, Button, Card, Group, Stack, Text, Title } from "@mantine/core";
import { Download, FileSpreadsheet, KeyRound, Upload } from "lucide-react";
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
    <Card withBorder padding="lg" className="flex flex-col">
      <Stack gap={4}>
        <Title order={4} className="flex items-center gap-2">
          <FileSpreadsheet size={20} color="var(--mantine-primary-color-filled)" /> {template.name}
        </Title>
        {template.description && (
          <Text size="sm" c="dimmed">
            {template.description}
          </Text>
        )}
      </Stack>

      <Stack gap="md" mt="md" className="flex-1">
        <div>
          <Text size="xs" fw={500} tt="uppercase" c="dimmed" mb="xs">
            {t("dataTransfer:templates.columns")}
          </Text>
          <Group gap={6}>
            {template.columns.map((column) => (
              <Badge
                key={column.key}
                variant={isBlockingColumn(column) ? "light" : "outline"}
                color="gray"
                fw={400}
                tt="none"
                title={column.isMatchKey ? t("dataTransfer:templates.matchKey") : undefined}
                leftSection={column.isMatchKey ? <KeyRound size={12} /> : undefined}
                rightSection={
                  isBlockingColumn(column) ? (
                    <Text span c="red" inherit>
                      *
                    </Text>
                  ) : undefined
                }
              >
                {column.header}
              </Badge>
            ))}
          </Group>
          <Group gap="md" mt="xs">
            <Text size="xs" c="dimmed">
              <Text span c="red" inherit>
                *
              </Text>{" "}
              {t("dataTransfer:templates.requiredLegend")}
            </Text>
            <Text size="xs" c="dimmed" className="inline-flex items-center gap-1">
              <KeyRound size={12} /> {t("dataTransfer:templates.matchKeyLegend")}
            </Text>
          </Group>
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
      </Stack>

      {(canDownload || canRun) && (
        <Group gap="xs" mt="lg">
          {canDownload && (
            <Button
              type="button"
              variant="default"
              leftSection={<Download size={16} />}
              loading={downloadMutation.isPending}
              onClick={() => downloadMutation.mutate()}
            >
              {t("dataTransfer:templates.download")}
            </Button>
          )}
          {canRun && (
            <Button
              type="button"
              leftSection={<Upload size={16} />}
              onClick={() => dropzoneRef.current?.open()}
              disabled={uploadMutation.isPending}
            >
              {t("dataTransfer:templates.upload")}
            </Button>
          )}
        </Group>
      )}
    </Card>
  );
}
