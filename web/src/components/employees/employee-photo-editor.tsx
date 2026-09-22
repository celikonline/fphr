import { useRef } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Camera, Trash2 } from "lucide-react";
import { ActionIcon, Menu } from "@mantine/core";
import { usePermission } from "@/components/permission-guard";
import { useOwnEmployeeId } from "@/hooks/use-employee-photo";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { resizeImageToJpeg } from "@/lib/resize-image";
import {
  deleteEmployeePhoto,
  OWN_PHOTO_TARGET,
  uploadEmployeePhoto,
} from "@/services/employees.service";
import { EmployeeAvatar } from "./employee-avatar";

interface EmployeePhotoEditorProps {
  employee: { id: string; firstName: string; lastName: string };
  size?: number;
  radius?: string | number;
  fz?: number;
}

/**
 * The employee's avatar with a small camera menu ("Fotoğraf yükle / Kaldır") for users who may change it: HR with
 * `employee.employees.update`, or the employee themself with `employee.self.edit`. Others just see the avatar.
 */
export function EmployeePhotoEditor({
  employee,
  size = 88,
  radius = "md",
  fz,
}: EmployeePhotoEditorProps) {
  const { t } = useTranslation(["employees", "common"]);
  const queryClient = useQueryClient();
  const fileInput = useRef<HTMLInputElement>(null);
  const canUpdate = usePermission("employee.employees.update");
  const canSelfEdit = usePermission("employee.self.edit");
  const isOwn = employee.id === useOwnEmployeeId();

  // The own card goes through /me (self-service permission); anyone else's, or an HR user without self-edit, by id.
  const useSelfPath = isOwn && canSelfEdit;
  const canEdit = useSelfPath || canUpdate;
  const target = useSelfPath ? OWN_PHOTO_TARGET : employee.id;

  const onDone = (message: string) => {
    void queryClient.invalidateQueries({ queryKey: ["employees", "photo"] });
    toast({ variant: "success", title: t("common:success"), description: message });
  };
  const onFailed = (error: unknown) =>
    toast({
      variant: "destructive",
      title: t("common:error"),
      description: getApiErrorMessage(error, t("employees:photo.failed")),
    });

  const upload = useMutation({
    mutationFn: async (file: File) => uploadEmployeePhoto(target, await resizeImageToJpeg(file)),
    onSuccess: () => onDone(t("employees:photo.uploaded")),
    onError: onFailed,
  });
  const remove = useMutation({
    mutationFn: () => deleteEmployeePhoto(target),
    onSuccess: () => onDone(t("employees:photo.removed")),
    onError: onFailed,
  });

  const avatar = (
    <EmployeeAvatar
      employeeId={employee.id}
      firstName={employee.firstName}
      lastName={employee.lastName}
      size={size}
      radius={radius}
      fz={fz}
    />
  );
  if (!canEdit) return avatar;

  const busy = upload.isPending || remove.isPending;

  return (
    <div className="relative shrink-0" style={{ width: size, height: size }}>
      {avatar}
      <input
        ref={fileInput}
        type="file"
        accept="image/*"
        hidden
        data-testid="photo-file-input"
        onChange={(event) => {
          const file = event.currentTarget.files?.[0];
          event.currentTarget.value = "";
          if (file) upload.mutate(file);
        }}
      />
      <Menu position="bottom-end" withinPortal>
        <Menu.Target>
          <ActionIcon
            variant="default"
            size="sm"
            radius="xl"
            loading={busy}
            aria-label={t("employees:photo.change")}
            className="absolute -right-1 -bottom-1 shadow-sm"
          >
            <Camera size={14} />
          </ActionIcon>
        </Menu.Target>
        <Menu.Dropdown>
          <Menu.Item leftSection={<Camera size={14} />} onClick={() => fileInput.current?.click()}>
            {t("employees:photo.upload")}
          </Menu.Item>
          <Menu.Item color="red" leftSection={<Trash2 size={14} />} onClick={() => remove.mutate()}>
            {t("employees:photo.remove")}
          </Menu.Item>
        </Menu.Dropdown>
      </Menu>
    </div>
  );
}
