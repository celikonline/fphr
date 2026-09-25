import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Badge, Button, Checkbox, Group, Modal, Stack, Text, TextInput } from "@mantine/core";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { createSharedFileType, listSharedFileTypes, setSharedFileTypeActive } from "@/services/shared-file-types.service";

export const SHARED_FILE_TYPES_QUERY_KEY = ["documents", "shared-file-types"] as const;

/** E8: shared file type catalog editor (`documents.sharedfiles.types.manage`): add types, toggle active. */
export default function SharedFileTypesDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation(["documents", "common"]);
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [visibleOnMobile, setVisibleOnMobile] = useState(false);
  const typesQuery = useQuery({ queryKey: SHARED_FILE_TYPES_QUERY_KEY, queryFn: () => listSharedFileTypes() });

  const onError = (error: unknown) =>
    toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("documents:sharedFiles.typesFailed")) });
  const invalidate = () => queryClient.invalidateQueries({ queryKey: SHARED_FILE_TYPES_QUERY_KEY });

  const createMutation = useMutation({
    mutationFn: () => createSharedFileType({ name: name.trim(), visibleOnMobile }),
    onSuccess: () => {
      setName("");
      setVisibleOnMobile(false);
      void invalidate();
    },
    onError,
  });
  const activeMutation = useMutation({
    mutationFn: (input: { id: string; isActive: boolean }) => setSharedFileTypeActive(input.id, input.isActive),
    onSuccess: () => void invalidate(),
    onError,
  });

  return (
    <Modal opened onClose={onClose} title={t("documents:sharedFiles.typesTitle")} size="32rem">
      <Stack gap="md">
        {(typesQuery.data ?? []).map((type) => (
          <Group key={type.id} justify="space-between" wrap="nowrap">
            <Group gap="xs">
              <Text size="sm">{type.name}</Text>
              {!type.isActive && (
                <Badge variant="light" color="gray" size="sm">
                  {t("documents:sharedFiles.typeInactive")}
                </Badge>
              )}
            </Group>
            <Button
              type="button"
              size="xs"
              variant="subtle"
              loading={activeMutation.isPending && activeMutation.variables?.id === type.id}
              onClick={() => activeMutation.mutate({ id: type.id, isActive: !type.isActive })}
            >
              {type.isActive ? t("documents:sharedFiles.typeDeactivate") : t("documents:sharedFiles.typeActivate")}
            </Button>
          </Group>
        ))}
        <TextInput label={t("documents:sharedFiles.typeName")} value={name} onChange={(e) => setName(e.currentTarget.value)} maxLength={100} />
        <Checkbox label={t("documents:sharedFiles.typeMobileDefault")} checked={visibleOnMobile} onChange={(e) => setVisibleOnMobile(e.currentTarget.checked)} />
        <Group justify="flex-end">
          <Button type="button" disabled={name.trim().length === 0} loading={createMutation.isPending} onClick={() => createMutation.mutate()}>
            {t("documents:sharedFiles.typeAdd")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
