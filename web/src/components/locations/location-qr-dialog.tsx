import { useRef } from "react";
import { useTranslation } from "react-i18next";
import { Printer } from "lucide-react";
import { QRCodeSVG } from "qrcode.react";
import { Button, Group, Modal, Stack, Text } from "@mantine/core";
import type { Location } from "@/services/locations.service";

interface LocationQrDialogProps {
  location: Location | null;
  onClose: () => void;
}

const escapeHtml = (value: string) =>
  value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");

/** Lokasyon girişine asılacak self-servis check-in QR posteri: kod QR olarak gösterilir, yazdırılabilir. */
export default function LocationQrDialog({ location, onClose }: LocationQrDialogProps) {
  const { t } = useTranslation(["locations"]);
  const qrRef = useRef<HTMLDivElement>(null);

  function handlePrint() {
    const svg = qrRef.current?.querySelector("svg")?.outerHTML;
    if (!svg || !location) return;
    const popup = window.open("", "_blank", "width=600,height=800");
    if (!popup) return;
    popup.document.write(
      `<!doctype html><html><head><title>${escapeHtml(location.name)}</title>
<style>body{font-family:sans-serif;text-align:center;padding:48px}svg{width:360px;height:360px}
h1{font-size:28px;margin:0 0 8px}p{font-size:18px;color:#444}code{font-size:32px;letter-spacing:4px}</style></head>
<body><h1>${escapeHtml(location.name)}</h1><p>${escapeHtml(t("locations:qrPosterInstruction"))}</p>${svg}
<p><code>${escapeHtml(location.selfCheckInCode)}</code></p></body></html>`
    );
    popup.document.close();
    popup.focus();
    popup.print();
  }

  return (
    <Modal opened={location !== null} onClose={onClose} title={t("locations:qrPosterTitle")} centered>
      {location && (
        <Stack align="center" gap="sm">
          <Text fw={600}>{location.name}</Text>
          <div ref={qrRef} style={{ background: "#fff", padding: 12 }}>
            <QRCodeSVG value={location.selfCheckInCode} size={240} marginSize={0} />
          </div>
          <Text ff="monospace" size="lg">
            {location.selfCheckInCode}
          </Text>
          <Text size="xs" c="dimmed" ta="center">
            {t("locations:qrPosterInstruction")}
          </Text>
          <Group>
            <Button leftSection={<Printer size={14} />} onClick={handlePrint}>
              {t("locations:print")}
            </Button>
          </Group>
        </Stack>
      )}
    </Modal>
  );
}
