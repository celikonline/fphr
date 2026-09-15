import * as React from "react";
import { Loader2, Upload } from "lucide-react";
import { cn } from "@/lib/utils";

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

/** Click-or-drop single file picker with type/size checks; replaces a bare `<input type="file">`. */
export const FileDropzone = React.forwardRef<FileDropzoneHandle, FileDropzoneProps>(function FileDropzone(
  { accept, maxBytes, onFile, onReject, disabled, busy, title, hint, busyLabel, icon, className },
  ref
) {
  const inputRef = React.useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = React.useState(false);
  const inactive = disabled || busy;

  React.useImperativeHandle(ref, () => ({ open: () => inputRef.current?.click() }), []);

  function handle(file: File | undefined) {
    if (!file || inactive) return;
    if (!matchesAccept(file, accept)) return onReject?.("type", file);
    if (maxBytes !== undefined && file.size > maxBytes) return onReject?.("size", file);
    onFile(file);
  }

  return (
    <div
      role="button"
      tabIndex={inactive ? -1 : 0}
      aria-disabled={inactive || undefined}
      onClick={() => !inactive && inputRef.current?.click()}
      onKeyDown={(e) => {
        if (!inactive && (e.key === "Enter" || e.key === " ")) {
          e.preventDefault();
          inputRef.current?.click();
        }
      }}
      onDragOver={(e) => {
        e.preventDefault();
        if (!inactive) setDragging(true);
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={(e) => {
        e.preventDefault();
        setDragging(false);
        handle(e.dataTransfer.files?.[0]);
      }}
      className={cn(
        "flex flex-col items-center justify-center gap-1 rounded-md border-2 border-dashed p-4 text-center text-sm text-muted-foreground transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
        inactive ? "cursor-not-allowed" : "cursor-pointer",
        disabled && "opacity-50",
        dragging ? "border-primary bg-primary/5" : "border-border",
        !inactive && !dragging && "hover:bg-muted/50",
        className
      )}
    >
      {busy ? (
        <>
          <Loader2 className="h-5 w-5 animate-spin" />
          {busyLabel ?? title}
        </>
      ) : (
        <>
          {icon ?? <Upload className="h-5 w-5" />}
          <span>{title}</span>
          {hint && <span className="text-xs">{hint}</span>}
        </>
      )}
      <input
        ref={inputRef}
        type="file"
        className="hidden"
        accept={accept}
        disabled={inactive}
        onClick={(e) => e.stopPropagation()}
        onChange={(e) => {
          handle(e.target.files?.[0]);
          e.target.value = "";
        }}
      />
    </div>
  );
});
