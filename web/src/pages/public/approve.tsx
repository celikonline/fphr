import { useState } from "react";
import { useParams } from "react-router";
import { useTranslation } from "react-i18next";
import { useMutation } from "@tanstack/react-query";
import { Button, Card, Center, Group, Stack, Text, Textarea, Title } from "@mantine/core";
import { AlertTriangle, CheckCircle2, Clock } from "lucide-react";
import { getApiProblem } from "@/lib/api-error";
import { decideByToken, type DecideByTokenPayload } from "@/services/workflow.service";
import classes from "./approve.module.css";

type Result = "success" | "expired" | "alreadyDecided" | "error";

function CenteredCard({ children }: { children: React.ReactNode }) {
  return (
    <Center mih="100vh" px="md" py={48} bg="var(--mantine-color-default-hover)">
      <Card withBorder padding="xl" w="100%" maw={448} className={classes.card}>
        {children}
      </Card>
    </Center>
  );
}

function ResultMessage({ result }: { result: Result }) {
  const { t } = useTranslation(["workflow"]);
  const config: Record<Result, { icon: typeof CheckCircle2; color: string; textKey: string }> = {
    success: { icon: CheckCircle2, color: "var(--mantine-color-green-7)", textKey: "workflow:publicDecision.success" },
    expired: { icon: Clock, color: "var(--mantine-color-yellow-6)", textKey: "workflow:publicDecision.expired" },
    alreadyDecided: {
      icon: AlertTriangle,
      color: "var(--mantine-color-yellow-6)",
      textKey: "workflow:publicDecision.alreadyDecided",
    },
    error: { icon: AlertTriangle, color: "var(--mantine-color-red-6)", textKey: "workflow:publicDecision.error" },
  };
  const { icon: Icon, color, textKey } = config[result];

  return (
    <CenteredCard>
      <Stack align="center" ta="center" gap="xs">
        <Icon size={40} color={color} />
        <Title order={3}>{t("workflow:publicDecision.title")}</Title>
        <Text size="sm" c="dimmed">
          {t(textKey)}
        </Text>
      </Stack>
    </CenteredCard>
  );
}

export default function ApproveTokenPage() {
  const { t } = useTranslation(["workflow", "common"]);
  const { token = "" } = useParams();
  const [comment, setComment] = useState("");
  const [result, setResult] = useState<Result | null>(null);

  const mutation = useMutation({
    mutationFn: (outcome: DecideByTokenPayload["outcome"]) =>
      decideByToken({ token, outcome, comment: comment.trim() || undefined }),
    onSuccess: () => setResult("success"),
    onError: (error) => {
      const code = getApiProblem(error)?.code;
      if (code === "workflow.token.expired") setResult("expired");
      else if (code === "workflow.token.already_used") setResult("alreadyDecided");
      else setResult("error");
    },
  });

  if (!token) return <ResultMessage result="error" />;
  if (result) return <ResultMessage result={result} />;

  return (
    <CenteredCard>
      <Stack gap="md">
        <Title order={3}>{t("workflow:publicDecision.title")}</Title>
        <Textarea
          id="decision-comment"
          label={t("workflow:publicDecision.comment")}
          placeholder={t("workflow:publicDecision.commentPlaceholder")}
          value={comment}
          onChange={(e) => setComment(e.currentTarget.value)}
          disabled={mutation.isPending}
          autosize
          minRows={3}
        />
        <Group grow gap="xs">
          <Button type="button" color="red" onClick={() => mutation.mutate("rejected")} disabled={mutation.isPending}>
            {t("workflow:publicDecision.reject")}
          </Button>
          <Button type="button" onClick={() => mutation.mutate("approved")} disabled={mutation.isPending}>
            {t("workflow:publicDecision.approve")}
          </Button>
        </Group>
      </Stack>
    </CenteredCard>
  );
}
