import { useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Alert, Badge, Button, Group, Stack, Text, Textarea } from "@mantine/core";
import { getApiErrorMessage } from "@/lib/api-error";
import {
  getFormulaCatalog,
  validatePayItemFormula,
  type FormulaValidation,
  type PayItemAmountBasis,
} from "@/services/payroll-pay-items.service";

export const FORMULA_MAX_LENGTH = 500;
const CATALOG_STALE_MS = 5 * 60_000;

interface FormulaEditorProps {
  value: string;
  onChange: (value: string) => void;
  amountBasis: PayItemAmountBasis;
}

/**
 * Formül alanı + sunucu kataloğundan değişken/fonksiyon listesi (tıklayınca imleç konumuna ekler) + "Doğrula".
 * Doğrulama yalnız `validate-formula` ucuyla yapılır (istemcide ayrıştırıcı yok); hata konumu 1 tabanlıdır.
 */
export function FormulaEditor({ value, onChange, amountBasis }: FormulaEditorProps) {
  const { t } = useTranslation(["payroll"]);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const [result, setResult] = useState<FormulaValidation | null>(null);
  const [requestError, setRequestError] = useState<string | null>(null);

  const catalogQuery = useQuery({ queryKey: ["payroll", "pay-items", "formula-catalog"], queryFn: getFormulaCatalog, staleTime: CATALOG_STALE_MS });
  const variables = useMemo(() => catalogQuery.data?.variables ?? [], [catalogQuery.data]);
  const functions = useMemo(() => catalogQuery.data?.functions ?? [], [catalogQuery.data]);

  const validate = useMutation({
    mutationFn: () => {
      const sampleValues: Record<string, number> = {};
      for (const variable of variables) {
        if (variable.sampleValue != null) sampleValues[variable.name] = variable.sampleValue;
      }
      return validatePayItemFormula({ formula: value, amountBasis, sampleValues });
    },
    onSuccess: (data) => {
      setRequestError(null);
      setResult(data);
    },
    onError: (err) => {
      setResult(null);
      setRequestError(getApiErrorMessage(err, t("payroll:payItems.formula.validateError")));
    },
  });

  const insert = (text: string) => {
    const element = inputRef.current;
    const focused = element !== null && document.activeElement === element;
    const start = focused ? (element.selectionStart ?? value.length) : value.length;
    const end = focused ? (element.selectionEnd ?? value.length) : value.length;
    onChange(value.slice(0, start) + text + value.slice(end));
    setResult(null);
    const caret = start + text.length;
    requestAnimationFrame(() => {
      element?.focus();
      element?.setSelectionRange(caret, caret);
    });
  };

  const invalid = result !== null && !result.isValid;
  const position = invalid ? (result.position ?? null) : null;
  const errorMessage = invalid ? result.errorMessage || result.errorCode || t("payroll:payItems.formula.invalid") : null;
  const used = result?.usedVariables ?? [];

  return (
    <Stack gap="xs" data-testid="formula-editor">
      <Textarea
        ref={inputRef}
        label={t("payroll:payItems.formula.label")}
        description={t("payroll:payItems.formula.hint")}
        value={value}
        onChange={(event) => {
          onChange(event.currentTarget.value);
          setResult(null);
        }}
        maxLength={FORMULA_MAX_LENGTH}
        rows={3}
        required
        styles={{ input: { fontFamily: "monospace" } }}
      />

      <Group gap="xs">
        <Button size="xs" variant="light" onClick={() => validate.mutate()} loading={validate.isPending} disabled={value.trim().length === 0}>
          {t("payroll:payItems.formula.validate")}
        </Button>
      </Group>

      {requestError && <Alert color="red">{requestError}</Alert>}

      {result?.isValid && (
        <Alert color="green" title={t("payroll:payItems.formula.valid")}>
          <Text size="sm">
            {t("payroll:payItems.formula.usedVariables")}: {used.length > 0 ? used.join(", ") : t("payroll:payItems.formula.noVariables")}
          </Text>
          {result.sampleResult != null && (
            <Text size="sm">
              {t("payroll:payItems.formula.sampleResult")}: <b>{result.sampleResult}</b>
            </Text>
          )}
        </Alert>
      )}

      {errorMessage && (
        <Alert color="red" title={t("payroll:payItems.formula.invalid")}>
          <Text size="sm">{errorMessage}</Text>
          {position != null && (
            <>
              <Text size="xs" c="dimmed">
                {t("payroll:payItems.formula.position", { position })}
              </Text>
              <HighlightedFormula text={value} position={position} />
            </>
          )}
        </Alert>
      )}

      <div>
        <Text size="xs" fw={600} mb={4}>
          {t("payroll:payItems.catalog.variables")}
        </Text>
        <Group gap={6}>
          {variables.map((variable) => (
            <Badge
              key={variable.name}
              component="button"
              type="button"
              variant="light"
              style={{ cursor: "pointer" }}
              title={t(`payroll:payItems.variables.${variable.name}`, { defaultValue: variable.name })}
              aria-label={t("payroll:payItems.catalog.insertVariable", { name: variable.name })}
              onClick={() => insert(variable.name)}
            >
              {variable.name}
            </Badge>
          ))}
        </Group>
        {variables.length > 0 && (
          <Stack gap={0} mt={6}>
            {variables.map((variable) => (
              <Text key={variable.name} size="xs" c="dimmed">
                <b>{variable.name}</b>: {t(`payroll:payItems.variables.${variable.name}`, { defaultValue: "" })}
              </Text>
            ))}
          </Stack>
        )}
      </div>

      <div>
        <Text size="xs" fw={600} mb={4}>
          {t("payroll:payItems.catalog.functions")}
        </Text>
        <Group gap={6}>
          {functions.map((fn) => {
            const aliases = fn.aliases ?? [];
            return (
              <Badge
                key={fn.name}
                component="button"
                type="button"
                variant="outline"
                style={{ cursor: "pointer" }}
                title={aliases.join(", ")}
                aria-label={t("payroll:payItems.catalog.insertFunction", { name: fn.name })}
                onClick={() => insert(`${fn.name}()`)}
              >
                {aliases.length > 0 ? `${fn.name} / ${aliases.join(" / ")}` : fn.name}
              </Badge>
            );
          })}
        </Group>
      </div>
    </Stack>
  );
}

/** Shows the formula text with the character at the 1-based error position marked. */
function HighlightedFormula({ text, position }: { text: string; position: number }) {
  const index = Math.min(Math.max(position - 1, 0), text.length);
  const before = text.slice(0, index);
  const at = text.slice(index, index + 1);
  const after = text.slice(index + 1);
  return (
    <Text size="sm" ff="monospace" mt={4} data-testid="formula-error-context" style={{ whiteSpace: "pre-wrap", wordBreak: "break-all" }}>
      {before}
      <mark data-testid="formula-error-mark" style={{ background: "var(--mantine-color-red-2)", textDecoration: "underline wavy red" }}>
        {at || "_"}
      </mark>
      {after}
    </Text>
  );
}
