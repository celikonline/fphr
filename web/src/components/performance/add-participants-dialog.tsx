import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Badge, Button, Card, Checkbox, CloseButton, Group, Modal, Select, Stack, Text } from "@mantine/core";
import { toast } from "@/hooks/use-toast";
import { getApiErrorMessage } from "@/lib/api-error";
import { listEmployees } from "@/services/employees.service";
import { addParticipants, type EvaluatorRelation, type ReviewCycleDto } from "@/services/performance.service";

interface EvaluatorEntry {
  id: string;
  name: string;
}

interface ParticipantDraft {
  employeeId: string;
  name: string;
  includeGoals: boolean;
  evaluators: Partial<Record<EvaluatorRelation, EvaluatorEntry[]>>;
}

/** Debounced employee search reused for picking the participant and each relation's evaluators. */
function useEmployeeSearch(excludeIds: string[]) {
  const [search, setSearch] = useState("");
  const [debounced, setDebounced] = useState("");
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(search.trim()), 250);
    return () => window.clearTimeout(timer);
  }, [search]);

  const query = useQuery({
    queryKey: ["employees", "performance-picker", debounced],
    queryFn: () => listEmployees({ page: 1, pageSize: 20, q: debounced || undefined, status: "Active", sort: "displayName" }),
  });

  const options = (query.data?.items ?? [])
    .filter((e) => !excludeIds.includes(e.id))
    .map((e) => ({ value: e.id, label: e.displayName }));

  return { search, setSearch, options, isLoading: query.isLoading };
}

export default function AddParticipantsDialog({ cycle, onClose, onAdded }: { cycle: ReviewCycleDto; onClose: () => void; onAdded: () => void }) {
  const { t } = useTranslation(["performance", "common"]);
  const queryClient = useQueryClient();
  const [participants, setParticipants] = useState<ParticipantDraft[]>([]);
  const existingIds = cycle.participants.map((p) => p.employeeId);
  const participantSearch = useEmployeeSearch([...existingIds, ...participants.map((p) => p.employeeId)]);

  const enabledRelations = cycle.relationConfigs.filter((r) => r.isEnabled && r.relation !== "self");

  const mutation = useMutation({
    mutationFn: () =>
      addParticipants(
        cycle.id,
        participants.map((p) => ({
          employeeId: p.employeeId,
          includeGoals: p.includeGoals,
          evaluators: enabledRelations.map((r) => ({
            relation: r.relation,
            evaluatorEmployeeIds: (p.evaluators[r.relation] ?? []).map((e) => e.id),
          })),
        }))
      ),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["performance"] });
      toast({ variant: "success", title: t("common:success"), description: t("performance:addParticipants.success") });
      onAdded();
    },
    onError: (error) => toast({ variant: "destructive", title: t("common:error"), description: getApiErrorMessage(error, t("performance:genericError")) }),
  });

  function addParticipant(employeeId: string) {
    const option = participantSearch.options.find((o) => o.value === employeeId);
    if (!option) return;
    setParticipants((prev) => [...prev, { employeeId, name: option.label, includeGoals: cycle.kind === "competencyAndGoal", evaluators: {} }]);
  }

  return (
    <Modal opened onClose={onClose} size="42rem" title={t("performance:addParticipants.title")}>
      <Stack gap="md">
        <Select
          label={t("performance:addParticipants.pick")}
          placeholder={t("performance:addParticipants.search")}
          searchable
          searchValue={participantSearch.search}
          onSearchChange={participantSearch.setSearch}
          data={participantSearch.options}
          value={null}
          onChange={(value) => value && addParticipant(value)}
          nothingFoundMessage={participantSearch.isLoading ? t("common:loading") : t("performance:addParticipants.noResults")}
        />

        <Stack gap="sm">
          {participants.map((participant, index) => (
            <ParticipantRow
              key={participant.employeeId}
              participant={participant}
              relations={enabledRelations.map((r) => r.relation)}
              showGoalsToggle={cycle.kind === "competencyAndGoal"}
              onRemove={() => setParticipants((prev) => prev.filter((_, i) => i !== index))}
              onChange={(next) => setParticipants((prev) => prev.map((p, i) => (i === index ? next : p)))}
            />
          ))}
        </Stack>

        <Group justify="flex-end" gap="xs">
          <Button type="button" variant="default" onClick={onClose} disabled={mutation.isPending}>
            {t("common:cancel")}
          </Button>
          <Button type="button" loading={mutation.isPending} disabled={participants.length === 0} onClick={() => mutation.mutate()}>
            {t("common:save")}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}

function ParticipantRow({
  participant,
  relations,
  showGoalsToggle,
  onRemove,
  onChange,
}: {
  participant: ParticipantDraft;
  relations: EvaluatorRelation[];
  showGoalsToggle: boolean;
  onRemove: () => void;
  onChange: (next: ParticipantDraft) => void;
}) {
  const { t } = useTranslation(["performance"]);
  const usedEvaluatorIds = Object.values(participant.evaluators).flatMap((list) => (list ?? []).map((e) => e.id));
  const evaluatorSearch = useEmployeeSearch(usedEvaluatorIds);

  return (
    <Card withBorder padding="sm">
      <Group justify="space-between" mb="xs">
        <Text fw={500}>{participant.name}</Text>
        <CloseButton aria-label={t("performance:addParticipants.remove")} onClick={onRemove} />
      </Group>
      {showGoalsToggle && (
        <Checkbox
          label={t("performance:addParticipants.includeGoals")}
          checked={participant.includeGoals}
          onChange={(e) => onChange({ ...participant, includeGoals: e.currentTarget.checked })}
          mb="sm"
        />
      )}
      <Stack gap="xs">
        {relations.map((relation) => (
          <div key={relation}>
            <Text size="xs" c="dimmed" mb={4}>
              {t(`performance:relation.${relation}`)}
            </Text>
            <Group gap={6} mb={4}>
              {(participant.evaluators[relation] ?? []).map((evaluator) => (
                <Badge
                  key={evaluator.id}
                  variant="light"
                  tt="none"
                  rightSection={
                    <CloseButton
                      size="xs"
                      radius="xl"
                      variant="transparent"
                      onClick={() =>
                        onChange({
                          ...participant,
                          evaluators: {
                            ...participant.evaluators,
                            [relation]: (participant.evaluators[relation] ?? []).filter((e) => e.id !== evaluator.id),
                          },
                        })
                      }
                    />
                  }
                >
                  {evaluator.name}
                </Badge>
              ))}
            </Group>
            <Select
              size="xs"
              placeholder={t("performance:addParticipants.addEvaluator")}
              searchable
              searchValue={evaluatorSearch.search}
              onSearchChange={evaluatorSearch.setSearch}
              data={evaluatorSearch.options}
              value={null}
              onChange={(value) => {
                const option = evaluatorSearch.options.find((o) => o.value === value);
                if (!option) return;
                onChange({
                  ...participant,
                  evaluators: {
                    ...participant.evaluators,
                    [relation]: [...(participant.evaluators[relation] ?? []), { id: option.value, name: option.label }],
                  },
                });
              }}
            />
          </div>
        ))}
      </Stack>
    </Card>
  );
}
