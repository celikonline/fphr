import { Button, Text, Title } from "@mantine/core";
import { useQuery } from "@tanstack/react-query";
import { ArrowUpRight, Sparkles } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { getApiErrorStatus } from "@/lib/api-error";
import { listInbox } from "@/services/workflow.service";
import styles from "./home.module.css";

interface HomeFocusProps {
  canViewInbox: boolean;
  canRequestLeave: boolean;
  canViewDirectory: boolean;
}

export function HomeFocus({ canViewInbox, canRequestLeave, canViewDirectory }: HomeFocusProps) {
  const { t } = useTranslation("home");
  const inbox = useQuery({
    queryKey: ["home", "pending-approvals"],
    queryFn: listInbox,
    enabled: canViewInbox,
    retry: (failureCount, error) => {
      const status = getApiErrorStatus(error);
      return status !== 401 && status !== 403 && status !== 404 && failureCount < 1;
    },
  });

  const kind = canViewInbox && inbox.isSuccess && inbox.data.length > 0
    ? "approvals"
    : canRequestLeave
      ? "leave"
      : canViewDirectory
        ? "directory"
        : "account";
  const href = {
    approvals: "/app/inbox",
    leave: "/app/leave",
    directory: "/app/employees",
    account: "/app/account",
  }[kind];
  const count = kind === "approvals" ? inbox.data?.length : undefined;

  return (
    <section className={styles.focus} aria-labelledby="home-focus-title">
      <div className={styles.focusContent}>
        <div className={styles.focusEyebrow}>
          <Sparkles size={15} aria-hidden="true" />
          <span>{t("focus.eyebrow")}</span>
        </div>
        <Title id="home-focus-title" order={1} className={styles.focusTitle}>
          {t(`focus.${kind}.title`, { count })}
        </Title>
        <Text className={styles.focusDescription}>{t(`focus.${kind}.description`)}</Text>
      </div>
      <Button
        component={Link}
        to={href}
        size="md"
        radius="xs"
        variant="filled"
        rightSection={<ArrowUpRight size={18} aria-hidden="true" />}
        className={styles.focusAction}
      >
        {t(`focus.${kind}.action`)}
      </Button>
    </section>
  );
}
