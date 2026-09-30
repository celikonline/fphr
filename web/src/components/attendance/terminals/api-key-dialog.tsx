import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Alert, Button, Group, Modal, Stack, TextInput } from "@mantine/core";
import { Copy, TriangleAlert } from "lucide-react";
import { toast } from "@/hooks/use-toast";

interface ApiKeyDialogProps {
  /** Ham anahtar; null = kapalı. Sahibi (üst bileşen) kapanınca null'a çeker, böylece bellekten silinir. */
  apiKey: string | null;
  onClose: () => void;
}

/**
 * Ham terminal API anahtarını bir kez gösterir. Anahtar yalnız bu bileşenin props'unda yaşar:
 * URL'ye, localStorage'a veya log'a yazılmaz; kapatılınca üst bileşen state'ini temizler.
 */
export default function ApiKeyDialog({ apiKey, onClose }: ApiKeyDialogProps) {
  const { t } = useTranslation(["terminals"]);
  const [copied, setCopied] = useState(false);

  async function copy() {
    if (!apiKey) return;
    try {
      await navigator.clipboard.writeText(apiKey);
      setCopied(true);
    } catch {
      toast({ variant: "destructive", description: t("terminals:apiKey.copyFailed") });
    }
  }

  function close() {
    setCopied(false);
    onClose();
  }

  return (
    <Modal
      opened={apiKey !== null}
      onClose={close}
      title={t("terminals:apiKey.title")}
      closeOnClickOutside={false}
      closeOnEscape={false}
      keepMounted={false}
    >
      <Stack gap="md">
        <Alert color="yellow" icon={<TriangleAlert size={16} />}>
          {t("terminals:apiKey.warning")}
        </Alert>
        <TextInput
          label={t("terminals:apiKey.label")}
          value={apiKey ?? ""}
          readOnly
          autoComplete="off"
          onFocus={(e) => e.currentTarget.select()}
        />
        <Group justify="flex-end">
          <Button variant="default" leftSection={<Copy size={16} />} onClick={copy}>
            {copied ? t("terminals:apiKey.copied") : t("terminals:apiKey.copy")}
          </Button>
          <Button onClick={close}>{t("terminals:apiKey.close")}</Button>
        </Group>
      </Stack>
    </Modal>
  );
}
