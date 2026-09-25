import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Modal, Stack } from "@mantine/core";
import { Download, ShieldAlert } from "lucide-react";
import { FileDropzone } from "@/components/ui/file-dropzone";
import { useApiLanguage } from "@/components/data-transfer/use-api-language";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage, getApiErrorStatus } from "@/lib/api-error";
import {
  DataTransferPermissions,
  IMPORT_ALLOWED_EXTENSIONS,
  IMPORT_MAX_FILE_BYTES,
  downloadImportTemplate,
  saveDownloadedFile,
  uploadImportFile,
} from "@/services/data-transfer.service";
import { usePermission } from "@/components/permission-guard";

/** T2 (Z5.4) DataTransfer şablon kodu. */
export const TERMINAL_CREDENTIAL_TEMPLATE_CODE = "terminal-credentials";

/**
 * Terminal kimliklerini Excel ile içe aktarma: mevcut DataTransfer yüklemesini (`uploadImportFile`) kullanır, sonra
 * standart iş sayfasına (eşleme + çalıştırma) yönlendirir. Dosya kart/PIN içerdiği için uyarı gösterilir.
 */
export default function CredentialImportDialog({ opened, onClose }: { opened: boolean; onClose: () => void }) {
  const { t } = useTranslation(["terminals", "dataTransfer", "common"]);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const lang = useApiLanguage();
  const canDownload = usePermission(DataTransferPermissions.TemplateDownload);

  const upload = useMutation({
    mutationFn: (file: File) => uploadImportFile(TERMINAL_CREDENTIAL_TEMPLATE_CODE, file),
    onSuccess: (job) => {
      queryClient.invalidateQueries({ queryKey: ["datatransfer", "import-jobs"] });
      queryClient.setQueryData(["datatransfer", "import-job", job.id], job);
      onClose();
      navigate(`/app/settings/data-transfer/jobs/${job.id}`);
    },
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description:
          getApiErrorStatus(error) === 503
            ? t("terminals:credential.pepperMissing")
            : getApiErrorMessage(error, t("terminals:credential.uploadFailed")),
      }),
  });
  const download = useMutation({
    mutationFn: () => downloadImportTemplate(TERMINAL_CREDENTIAL_TEMPLATE_CODE, lang),
    onSuccess: saveDownloadedFile,
    onError: (error) =>
      toast({
        variant: "destructive",
        title: t("common:error"),
        description: getApiErrorMessage(error, t("dataTransfer:templates.downloadError")),
      }),
  });

  return (
    <Modal opened={opened} onClose={onClose} title={t("terminals:credential.importTitle")} data-template-code={TERMINAL_CREDENTIAL_TEMPLATE_CODE}>
      <Stack gap="md">
        <Alert color="yellow" icon={<ShieldAlert size={16} />}>
          {t("terminals:credential.sensitiveNotice")}
        </Alert>
        {canDownload && (
          <Button variant="default" leftSection={<Download size={16} />} loading={download.isPending} onClick={() => download.mutate()}>
            {t("terminals:credential.downloadTemplate")}
          </Button>
        )}
        <FileDropzone
          accept={IMPORT_ALLOWED_EXTENSIONS.join(",")}
          maxBytes={IMPORT_MAX_FILE_BYTES}
          busy={upload.isPending}
          title={t("dataTransfer:upload.dropHint")}
          hint={t("dataTransfer:upload.limits")}
          busyLabel={t("dataTransfer:upload.uploading")}
          onFile={(file) => upload.mutate(file)}
          onReject={(reason) =>
            toast({
              variant: "destructive",
              title: t("common:error"),
              description: t(reason === "type" ? "dataTransfer:upload.invalidType" : "dataTransfer:upload.tooLarge"),
            })
          }
        />
      </Stack>
    </Modal>
  );
}
