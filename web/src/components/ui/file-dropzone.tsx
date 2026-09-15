import * as React from "react";
import { Dropzone, type FileRejection } from "@mantine/dropzone";
import { Stack, Text } from "@mantine/core";
import { Upload } from "lucide-react";

export type FileRejectReason = "type" | "size";

export interface FileDropzoneHandle {
  /** Opens the browser file dialog (e.g. from a separate "Upload" button). */
  open: () => void;
}

export interface FileDropzoneProps {
  /** Same syntax as the input `accept` attribute: extensions (".xlsx") and/or MIME types ("image/png", "image/*"). */
  accept?: string;
  maxBytes?: number;
  onFile: (file: File) => void;
  onReject?: (reason: FileRejectReason, file: File) => void;
  disabled?: boolean;
  /** Shows a spinner and ignores new files. */
  busy?: boolean;
  title: React.ReactNode;
  hint?: React.ReactNode;
  busyLabel?: React.ReactNode;
  icon?: React.ReactNode;
  className?: string;
}

function matchesAccept(file: File, accept: string | undefined): boolean {
  if (!accept) return true;
  const name = file.name.toLowerCase();
  const type = file.type.toLowerCase();
  return accept
    .split(",")
    .map((token) => token.trim().toLowerCase())
    .filter(Boolean)
    .some((token) => {
      if (token.startsWith(".")) return name.endsWith(token);
      if (token.endsWith("/*")) return type.startsWith(token.slice(0, -1));
      return type === token;
    });
}

/** Click-or-drop single file picker with type/size checks (Mantine `Dropzone`); replaces a bare `<input type="file">`. */
export const FileDropzone = React.forwardRef<FileDropzoneHandle, FileDropzoneProps>(function FileDropzone(
  { accept, maxBytes, onFile, onReject, disabled, busy, title, hint, busyLabel, icon, className },
  ref
) {
  const openRef = React.useRef<() => void>(null);
  const inactive = disabled || busy;

  React.useImperativeHandle(ref, () => ({ open: () => openRef.current?.() }), []);

  // Validation stays ours: the `accept` attribute mixes extensions and MIME types, which react-dropzone's
  // MIME-keyed `accept` can't express. The validator only drives the drag reject styling.
  const validator = (file: File) => {
    // During drag, browsers expose DataTransferItems without a name; don't flag those.
    if (!(file instanceof File) || !file.name) return null;
    return matchesAccept(file, accept) ? null : { code: "file-invalid-type", message: "type" };
  };

  function handle(file: File | undefined) {
    if (!file || inactive) return;
    if (!matchesAccept(file, accept)) return onReject?.("type", file);
    if (maxBytes !== undefined && file.size > maxBytes) return onReject?.("size", file);
    onFile(file);
  }

  return (
    <Dropzone
      className={className}
      openRef={openRef}
      multiple={false}
      maxSize={maxBytes}
      validator={validator}
      inputProps={{ accept }}
      onDrop={() => {}}
      onDropAny={(accepted, rejections: FileRejection[]) => handle(accepted[0] ?? rejections[0]?.file)}
      disabled={disabled}
      loading={busy}
      activateOnClick={!inactive}
      opacity={disabled ? 0.5 : undefined}
      style={inactive ? { cursor: "not-allowed" } : undefined}
      p="md"
    >
      <Stack align="center" justify="center" gap={4} ta="center" c="dimmed">
        {!busy && (icon ?? <Upload size={20} />)}
        <Text size="sm" c="dimmed">
          {busy ? (busyLabel ?? title) : title}
        </Text>
        {hint && !busy && (
          <Text size="xs" c="dimmed">
            {hint}
          </Text>
        )}
      </Stack>
    </Dropzone>
  );
});
