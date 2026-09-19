import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Button, Group, Modal, NumberInput, Skeleton, Stack, Text, Textarea, Title } from "@mantine/core";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { getAssignmentForm, submitReviewResponse, type CommentMode } from "@/services/performance.service";

interface AnswerDraft {
  score: string;
  comment: string;
}

export default function FillAssignmentDialog({
  cycleId,
  assignmentId,
  onClose,
  onSubmitted,
}: {
  cycleId: string;
  assignmentId: string;
  onClose: () => void;
  onSubmitted: () => void;
}) {
  const { t } = useTranslation(["performance", "common"]);
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ["performance", "assignment-form", cycleId, assignmentId], queryFn: () => getAssignmentForm(cycleId, assignmentId) });
  const [answers, setAnswers] = useState<Record<string, AnswerDraft>>({});
  // Seeds `answers` from the loaded form's existing responses exactly once per fetch (React's "adjust state while
  // rendering" pattern - https://react.dev/learn/you-might-not-need-an-effect - avoids an effect + cascading render).
  const [seededFrom, setSeededFrom] = useState<typeof query.data>(undefined);
  if (query.data && query.data !== seededFrom) {
    const initial: Record<string, AnswerDraft> = {};
    for (const existing of query.data.existingResponses) {
      initial[existing.questionId] = { score: String(existing.score), comment: existing.comment ?? "" };
    }
    setAnswers(initial);
    setSeededFrom(query.data);
  }

  const allQuestions = (query.data?.competencies ?? []).flatMap((c) => c.questions);
  const isComplete = allQuestions.length > 0 && allQuestions.every((q) => answers[q.id]?.score.trim());

  const mutation = useMutation({
    mutationFn: () =>
      submitReviewResponse(
        cycleId,
        assignmentId,
        allQuestions.map((q) => ({ questionId: q.id, score: Number(answers[q.id]?.score ?? 0), comment: answers[q.id]?.comment.trim() || null }))
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["performance"] });
      toast({ variant: "success", title: t("common:success"), description: t("performance:fillForm.submitted") });
      onSubmitted();
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("performance:genericError")) }),
  });

  return (
    <Modal opened onClose={onClose} size="42rem" title={query.data?.reviewCycleName ?? t("performance:fillForm.title")}>
      {query.isLoading ? (
        <Skeleton h={240} />
      ) : query.isError || !query.data ? (
        <Text size="sm" c="red">
          {getApiErrorMessage(query.error, t("performance:genericError"))}
        </Text>
      ) : query.data.status === "completed" ? (
        <Text size="sm" c="dimmed">
          {t("performance:fillForm.alreadyCompleted")}
        </Text>
      ) : (
        <Stack gap="lg">
          {query.data.competencies.map((competency) => (
            <div key={competency.name}>
              <Title order={5} mb="sm">
                {competency.name}
              </Title>
              <Stack gap="md">
                {competency.questions.map((question) => (
                  <div key={question.id}>
                    <Text size="sm" fw={500} mb={4}>
                      {question.text}
                    </Text>
                    <Group align="flex-start" wrap="nowrap">
                      <NumberInput
                        w={100}
                        min={0}
                        max={100}
                        placeholder={t("performance:fillForm.score")}
                        value={answers[question.id]?.score ?? ""}
                        onChange={(v) => setAnswers((prev) => ({ ...prev, [question.id]: { ...prev[question.id], score: String(v ?? ""), comment: prev[question.id]?.comment ?? "" } }))}
                      />
                      <Textarea
                        style={{ flex: 1 }}
                        autosize
                        minRows={1}
                        placeholder={commentPlaceholder(question.commentModeUsed, t)}
                        value={answers[question.id]?.comment ?? ""}
                        onChange={(e) =>
                          setAnswers((prev) => ({ ...prev, [question.id]: { ...prev[question.id], score: prev[question.id]?.score ?? "", comment: e.currentTarget.value } }))
                        }
                      />
                    </Group>
                  </div>
                ))}
              </Stack>
            </div>
          ))}

          <Group justify="flex-end" gap="xs">
            <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
              {t("common:cancel")}
            </Button>
            <Button type="button" loading={mutation.isPending} disabled={!isComplete} onClick={() => mutation.mutate()}>
              {t("performance:fillForm.complete")}
            </Button>
          </Group>
        </Stack>
      )}
    </Modal>
  );
}

function commentPlaceholder(mode: CommentMode, t: (key: string) => string): string {
  if (mode === "required") return t("performance:commentMode.requiredHint");
  if (mode === "disabled") return t("performance:commentMode.disabledHint");
  return t("performance:commentMode.optionalHint");
}
