import { useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Button, Group, Select, Stack, Switch, Text, TextInput, Title } from "@mantine/core";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  getModuleSettings,
  updateModuleSettings,
  type ModuleSettings,
} from "@/services/module-settings.service";
import { completeTenantSetup, inviteUser } from "@/services/tenant-setup.service";
import { listRoles, type Role } from "@/services/users.service";
import { useAuthStore } from "@/store/auth.store";
import { StepActions, type SetupStepProps } from "./step-actions";

const EMAIL_PATTERN = /^\S+@\S+\.\S+$/;

/** Step 4 - optional modules on/off, first invitations, and the final "finish setup" action. */
export default function ModulesUsersStep({ onBack }: Pick<SetupStepProps, "onBack">) {
  const { t } = useTranslation(["setup", "common"]);
  const loaded = useQuery({
    queryKey: ["setup", "modules-users"],
    queryFn: async () => {
      const [modules, roles] = await Promise.all([getModuleSettings(), listRoles()]);
      // Owner is locked to the person who created the company; everything else can be handed to an invited user.
      return { modules, roles: roles.filter((role) => !role.isLocked) };
    },
  });

  if (loaded.isError) {
    return (
      <Text c="red" role="alert">
        {t("setup:errors.load")}
      </Text>
    );
  }
  if (!loaded.data) {
    return <Text c="dimmed">{t("common:loading")}</Text>;
  }
  return (
    <ModulesUsersForm modules={loaded.data.modules} roles={loaded.data.roles} onBack={onBack} />
  );
}

function ModulesUsersForm({
  modules,
  roles,
  onBack,
}: Pick<SetupStepProps, "onBack"> & { modules: ModuleSettings; roles: Role[] }) {
  const { t, i18n } = useTranslation(["setup", "common", "settings"]);
  const navigate = useNavigate();

  const [disabled, setDisabled] = useState<string[]>(modules.disabledModules);
  const [invited, setInvited] = useState<string[]>([]);
  const [email, setEmail] = useState("");
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [roleId, setRoleId] = useState<string | null>(null);
  const [inviteError, setInviteError] = useState<string | null>(null);
  const [finishError, setFinishError] = useState<string | null>(null);

  const optionalModules = modules.modules.filter((module) => !module.isCore);
  const modulesChanged = [...disabled].sort().join() !== [...modules.disabledModules].sort().join();

  const inviteMutation = useMutation({
    mutationFn: () =>
      inviteUser({
        email: email.trim(),
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        roleIds: [roleId as string],
        language: i18n.language,
      }),
    onSuccess: () => {
      setInvited((previous) => [...previous, email.trim()]);
      setEmail("");
      setFirstName("");
      setLastName("");
    },
    onError: (err) => setInviteError(getApiErrorMessage(err, t("setup:errors.generic"))),
  });

  const finishMutation = useMutation({
    mutationFn: async () => {
      if (modulesChanged) await updateModuleSettings(disabled);
      await completeTenantSetup();
      // The guard reads setupCompleted from /me: refresh before leaving so the wizard doesn't bounce us back.
      await useAuthStore.getState().refreshMe();
    },
    onSuccess: () => navigate("/app", { replace: true }),
    onError: (err) => setFinishError(getApiErrorMessage(err, t("setup:errors.finish"))),
  });

  function toggleModule(key: string, enabled: boolean) {
    setDisabled((previous) =>
      enabled ? previous.filter((item) => item !== key) : [...previous, key]
    );
  }

  function handleInvite() {
    if (!firstName.trim() || !lastName.trim() || !EMAIL_PATTERN.test(email.trim()) || !roleId) {
      return setInviteError(t("setup:modules.invalid"));
    }
    setInviteError(null);
    inviteMutation.mutate();
  }

  function handleFinish(event: FormEvent) {
    event.preventDefault();
    setFinishError(null);
    finishMutation.mutate();
  }

  return (
    <form onSubmit={handleFinish}>
      <Stack gap="md">
        <div>
          <Title order={3} size="h4">
            {t("setup:modules.heading")}
          </Title>
          <Text size="sm" c="dimmed">
            {t("setup:modules.description")}
          </Text>
        </div>

        <div>
          <Text size="sm" fw={500} mb="xs">
            {t("setup:modules.modulesHeading")}
          </Text>
          <Stack gap="xs">
            {optionalModules.map((module) => (
              <Switch
                key={module.key}
                label={t(`settings:modules.names.${module.key}`, module.key)}
                checked={!disabled.includes(module.key)}
                onChange={(event) => toggleModule(module.key, event.currentTarget.checked)}
                disabled={finishMutation.isPending}
              />
            ))}
          </Stack>
        </div>

        <div>
          <Text size="sm" fw={500}>
            {t("setup:modules.usersHeading")}
          </Text>
          <Text size="xs" c="dimmed" mb="xs">
            {t("setup:modules.usersDescription")}
          </Text>
          <Stack gap="sm">
            <Group grow align="flex-start">
              <TextInput
                label={t("setup:modules.firstName")}
                value={firstName}
                maxLength={100}
                onChange={(event) => setFirstName(event.currentTarget.value)}
              />
              <TextInput
                label={t("setup:modules.lastName")}
                value={lastName}
                maxLength={100}
                onChange={(event) => setLastName(event.currentTarget.value)}
              />
            </Group>
            <Group grow align="flex-start">
              <TextInput
                label={t("setup:modules.email")}
                type="email"
                value={email}
                onChange={(event) => setEmail(event.currentTarget.value)}
              />
              <Select
                label={t("setup:modules.role")}
                value={roleId}
                data={roles.map((role) => ({ value: role.id, label: role.name ?? role.code }))}
                onChange={setRoleId}
              />
            </Group>
            <Group>
              <Button
                type="button"
                variant="light"
                onClick={handleInvite}
                loading={inviteMutation.isPending}
              >
                {t("setup:modules.invite")}
              </Button>
            </Group>
            {inviteError && (
              <Text size="sm" c="red" role="alert">
                {inviteError}
              </Text>
            )}
            {invited.map((invitedEmail) => (
              <Text key={invitedEmail} size="sm" c="teal">
                {t("setup:modules.invited", { email: invitedEmail })}
              </Text>
            ))}
          </Stack>
        </div>

        <StepActions
          onBack={onBack}
          submitLabel={
            finishMutation.isPending ? t("setup:actions.finishing") : t("setup:actions.finish")
          }
          isSubmitting={finishMutation.isPending}
          error={finishError}
        />
      </Stack>
    </form>
  );
}
