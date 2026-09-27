import React, { useEffect, useState } from "react";
import { Alert, Text, View } from "react-native";
import {
  leaveService,
  type LeaveHalfDayPeriod,
  type RequestableLeaveType,
} from "@/services/LeaveService";
import { LeaveDocumentsSection } from "@/components/leave/LeaveDocumentsSection";
import { SubstitutePicker, type SubstituteCandidate } from "@/components/leave/SubstitutePicker";
import { DateRangeSheet } from "@/components/form/DateRangeSheet";
import { OptionSheet } from "@/components/form/OptionSheet";
import { formatDayMonthYear } from "@/components/form/dateRange";
import { PrimaryButton, SelectField, TextField } from "@/components/ui";
import { requestFormStyles as formStyles } from "@/components/requests/requestFormStyles";
import { formatDateWithWeekday, toLocalDateOnly } from "@/lib/formatters";
import { getApiErrorMessage } from "@/lib/apiError";
import { useAuthStore } from "@/store/authStore";
import { LeavePreviewSummary } from "./LeavePreviewSummary";
import { HalfDayPicker } from "./form/HalfDayPicker";
import { HourlyTimeFields } from "./form/HourlyTimeFields";
import {
  clampEndDate,
  formatHourlyDuration,
  hasRequiredDocument,
  leaveRequestBlocker,
  leaveUnitFlags,
} from "./leaveRequestRules";
import { useLeaveDocuments } from "./useLeaveDocuments";
import { useLeavePreview } from "./useLeavePreview";

export {
  buildCapturedPhotoFileName,
  buildPreviewInput,
  clampEndDate,
  filterRedundantWarnings,
  formatHourlyDuration,
  hasRequiredDocument,
  leaveRequestBlocker,
  leaveUnitFlags,
  previewSummaryText,
} from "./leaveRequestRules";

/** `DateOnly` string the API expects ("yyyy-MM-dd"); today, as a sane default for the form. */
function todayDateOnly(): string {
  return toLocalDateOnly(new Date());
}

type Props = {
  requestableTypes: RequestableLeaveType[];
  /** Called after a successful submit so the parent can refresh balances/"Taleplerim". */
  onSubmitted: () => void | Promise<void>;
};

/**
 * "Yeni izin talebi" card, split out of `LeaveScreen` (Kalite: 800-line soft ceiling). Owns every
 * piece of state and side effect the request draft needs: type/date/time selection, live day-count
 * preview (B-mobil-006), substitute (B-mobil-005), and document upload (B-mobil-004).
 */
export function LeaveRequestForm({ requestableTypes, onSubmitted }: Props) {
  const authUser = useAuthStore((state) => state.user);

  const [selectedTypeId, setSelectedTypeId] = useState<string | null>(
    requestableTypes[0]?.id ?? null
  );

  // Self-heals if `requestableTypes` first arrives empty (e.g. a failed initial load) and a later
  // refresh populates it - this component stays mounted across `LeaveScreen`'s refreshes, so the
  // `useState` initializer above only ever sees the very first render's list.
  useEffect(() => {
    if (!selectedTypeId && requestableTypes.length > 0) {
      setSelectedTypeId(requestableTypes[0].id);
    }
  }, [requestableTypes, selectedTypeId]);
  const [startDate, setStartDate] = useState(todayDateOnly());
  const [endDate, setEndDate] = useState(todayDateOnly());
  const [halfDayPeriod, setHalfDayPeriod] = useState<LeaveHalfDayPeriod>("morning");
  const [startTime, setStartTime] = useState("09:00");
  const [endTime, setEndTime] = useState("18:00");
  const [substitute, setSubstitute] = useState<SubstituteCandidate | null>(null);
  const [substitutePickerVisible, setSubstitutePickerVisible] = useState(false);
  const [typeSheetVisible, setTypeSheetVisible] = useState(false);
  const [dateSheetVisible, setDateSheetVisible] = useState(false);
  const [description, setDescription] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const selectedType = requestableTypes.find((t) => t.id === selectedTypeId) ?? null;
  // B-mobil-006: re-previews whenever type/date/time/half-day changes (debounced, stale-guarded).
  const { preview, previewLoading, previewError } = useLeavePreview(
    selectedType,
    startDate,
    endDate,
    startTime,
    endTime,
    halfDayPeriod
  );
  const {
    documents,
    uploadingDocument,
    captureDocumentPhoto,
    pickDocumentFile,
    removeDocument,
    clearDocuments,
  } = useLeaveDocuments(selectedType?.name);
  const { isFullDay, isHalfDay, isHourly } = leaveUnitFlags(selectedType?.unit);
  const showDocuments = !!selectedType?.requiresDocument;
  const showSubstitute = !!selectedType?.requiresSubstitute;
  const missingRequiredDocument = !hasRequiredDocument(selectedType, documents.length);
  const hourlyDuration = isHourly ? formatHourlyDuration(startTime, endTime) : null;
  const previewBlocksSubmit = preview !== null && preview.days <= 0;

  function selectType(id: string) {
    setSelectedTypeId(id);
    // A different type's "vekil" is not automatically valid for the newly selected one.
    setSubstitute(null);
  }

  function applyDates(from: string, to: string) {
    setStartDate(from);
    // D-mobil-005: the end date never trails before the start date.
    setEndDate(clampEndDate(from, to));
  }

  async function submitRequest() {
    const blocker = leaveRequestBlocker({
      selectedType,
      startDate,
      endDate,
      hourlyDuration,
      description,
      hasSubstitute: substitute !== null,
      documentCount: documents.length,
      previewBlocksSubmit,
    });
    if (blocker || !selectedTypeId || !selectedType) {
      if (blocker) Alert.alert(blocker.title, blocker.message);
      return;
    }

    setSubmitting(true);
    try {
      await leaveService.submitLeaveRequest({
        leaveTypeId: selectedTypeId,
        startDate,
        endDate: isFullDay ? endDate : startDate,
        description: description.trim().length > 0 ? description.trim() : null,
        ...(isHalfDay ? { halfDayPeriod } : {}),
        ...(isHourly ? { startTime: `${startTime}:00`, endTime: `${endTime}:00` } : {}),
        ...(showSubstitute ? { substituteEmployeeId: substitute?.id } : {}),
        ...(selectedType.requiresDocument ? { documentIds: documents.map((doc) => doc.id) } : {}),
      });
      setDescription("");
      clearDocuments();
      setSubstitute(null);
      await onSubmitted();
      Alert.alert("Gönderildi", "İzin talebiniz onaya gönderildi.");
    } catch (submitError) {
      Alert.alert(
        "Hata",
        getApiErrorMessage(submitError, "İzin talebi gönderilemedi. Lütfen tekrar deneyin.")
      );
    } finally {
      setSubmitting(false);
    }
  }

  if (requestableTypes.length === 0) {
    return <Text style={formStyles.hint}>Talep edebileceğiniz bir izin türü yok.</Text>;
  }

  const submitDisabled = uploadingDocument || missingRequiredDocument || previewBlocksSubmit;
  const isMultiDay = isFullDay && endDate !== startDate;
  const dateLabel = isMultiDay
    ? `${formatDayMonthYear(startDate)} → ${formatDayMonthYear(endDate)}`
    : formatDateWithWeekday(startDate);
  const typeOptions = requestableTypes.map((type) => ({
    value: type.id,
    label: type.name,
    description: type.description ?? undefined,
  }));

  return (
    <View style={formStyles.form}>
      <SelectField
        label="İzin türü"
        required
        value={selectedType?.name}
        placeholder="İzin türü seçin"
        onPress={() => setTypeSheetVisible(true)}
      />
      <SelectField
        label="Tarih"
        required
        value={dateLabel}
        placeholder="Tarih seçin"
        onPress={() => setDateSheetVisible(true)}
      />

      {isHalfDay ? <HalfDayPicker value={halfDayPeriod} onChange={setHalfDayPeriod} /> : null}

      {isHourly ? (
        <HourlyTimeFields
          startTime={startTime}
          endTime={endTime}
          onStartTimeChange={setStartTime}
          onEndTimeChange={setEndTime}
          duration={hourlyDuration}
        />
      ) : null}

      {/* B-mobil-006: live day-count preview - the whole reason B12 exists (a Sunday-only request
          silently deducted 0 days and the server rejection only showed up after submit). */}
      <LeavePreviewSummary
        loading={previewLoading}
        preview={preview}
        error={previewError}
        requiresDocument={showDocuments}
      />

      <TextField
        label={selectedType?.requiresDescription ? "Açıklama" : "Açıklama (opsiyonel)"}
        required={!!selectedType?.requiresDescription}
        value={description}
        onChangeText={setDescription}
        placeholder="Talebinizle ilgili not ekleyin"
        multiline
        numberOfLines={3}
        style={formStyles.textArea}
      />

      {showSubstitute ? (
        <SelectField
          label="Vekil"
          required
          value={substitute?.displayName}
          placeholder="Vekil seç"
          onPress={() => setSubstitutePickerVisible(true)}
        />
      ) : null}

      {showDocuments ? (
        <LeaveDocumentsSection
          documents={documents}
          uploading={uploadingDocument}
          missingRequired={missingRequiredDocument}
          onCapturePhoto={() => void captureDocumentPhoto()}
          onPickFile={() => void pickDocumentFile()}
          onRemove={removeDocument}
        />
      ) : null}

      <View style={formStyles.submit}>
        <PrimaryButton
          label="Talebi gönder"
          onPress={() => void submitRequest()}
          loading={submitting}
          disabled={submitDisabled}
        />
      </View>

      <OptionSheet
        visible={typeSheetVisible}
        title="İzin türü"
        options={typeOptions}
        selected={selectedTypeId}
        onSelect={selectType}
        onClose={() => setTypeSheetVisible(false)}
      />
      <DateRangeSheet
        visible={dateSheetVisible}
        mode={isMultiDay ? "range" : "single"}
        allowModeSwitch={isFullDay}
        from={startDate}
        to={isFullDay ? endDate : startDate}
        onApply={applyDates}
        onClose={() => setDateSheetVisible(false)}
      />
      <SubstitutePicker
        visible={substitutePickerVisible}
        onClose={() => setSubstitutePickerVisible(false)}
        onSelect={(candidate) => {
          setSubstitute(candidate);
          setSubstitutePickerVisible(false);
        }}
        excludeEmployeeId={authUser?.employeeId}
      />
    </View>
  );
}
