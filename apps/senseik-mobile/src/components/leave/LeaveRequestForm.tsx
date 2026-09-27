import React, { useEffect, useRef, useState } from "react";
import {
  ActivityIndicator,
  Alert,
  ScrollView,
  StyleSheet,
  Text,
  TextInput,
  TouchableOpacity,
  View,
} from "react-native";
import * as DocumentPicker from "expo-document-picker";
import * as ImagePicker from "expo-image-picker";
import { colors } from "@/styles/colors";
import { spacing } from "@/styles/spacing";
import { typography } from "@/styles/typography";
import {
  leaveService,
  type LeaveHalfDayPeriod,
  type LeaveRequestPreview,
  type LeaveUnit,
  type RequestableLeaveType,
} from "@/services/LeaveService";
import {
  isDocumentFileSizeAllowed,
  uploadMyDocument,
  type PickedDocumentFile,
} from "@/services/DocumentService";
import {
  LeaveDocumentsSection,
  type StagedDocument,
} from "@/components/leave/LeaveDocumentsSection";
import { SubstitutePicker, type SubstituteCandidate } from "@/components/leave/SubstitutePicker";
import { DateField } from "@/components/form/DateField";
import { TimeField } from "@/components/form/TimeField";
import {
  formatDays,
  formatDurationMinutes,
  parseTimeOnly,
  toLocalDateOnly,
} from "@/lib/formatters";
import { getApiErrorMessage } from "@/lib/apiError";
import { useAuthStore } from "@/store/authStore";

/** `DateOnly` string the API expects ("yyyy-MM-dd"); today, as a sane default for the form. */
function todayDateOnly(): string {
  return toLocalDateOnly(new Date());
}

/** D2: a full-day/half-day end date can't precede the (possibly just-changed) start date. */
export function clampEndDate(newStartDate: string, currentEndDate: string): string {
  return currentEndDate < newStartDate ? newStartDate : currentEndDate;
}

/**
 * A: live preview for an hourly request ("2 sa 30 dk") from the two "HH:mm" time fields; `null`
 * when either time is missing/malformed or the range is non-positive (mirrors
 * `LeaveRequestRules.ValidatePartialDayShape`'s `endTime <= startTime` rejection - no point
 * previewing a duration the server would reject anyway).
 */
export function formatHourlyDuration(startTime: string, endTime: string): string | null {
  const start = parseTimeOnly(startTime);
  const end = parseTimeOnly(endTime);
  if (!start || !end) return null;
  const totalMinutes = end.hours * 60 + end.minutes - (start.hours * 60 + start.minutes);
  if (totalMinutes <= 0) return null;
  return formatDurationMinutes(totalMinutes);
}

/** Derives the three mutually-exclusive per-unit form shapes from a `LeaveUnit`. */
export function leaveUnitFlags(unit: LeaveUnit | undefined): {
  isFullDay: boolean;
  isHalfDay: boolean;
  isHourly: boolean;
} {
  const isHalfDay = unit === "halfDay";
  const isHourly = unit === "hourly";
  return { isFullDay: !isHalfDay && !isHourly, isHalfDay, isHourly };
}

/**
 * B-mobil-006: builds the `GET .../requests/preview` query from the current form fields, or `null`
 * when there isn't enough information yet to ask for one (mirrors the same per-unit shape rules
 * `submitRequest` validates before posting, so the preview never fires a request the server would
 * reject for being incomplete).
 */
export function buildPreviewInput(
  selectedType: Pick<RequestableLeaveType, "id" | "unit"> | null,
  startDate: string,
  endDate: string,
  startTime: string,
  endTime: string,
  halfDayPeriod: LeaveHalfDayPeriod
) {
  if (!selectedType || !startDate) return null;
  const { isFullDay, isHalfDay, isHourly } = leaveUnitFlags(selectedType.unit);
  if (isFullDay && !endDate) return null;
  if (isHourly && !formatHourlyDuration(startTime, endTime)) return null;
  return {
    leaveTypeId: selectedType.id,
    start: startDate,
    end: isFullDay ? endDate : startDate,
    ...(isHourly ? { startTime: `${startTime}:00`, endTime: `${endTime}:00` } : {}),
    ...(isHalfDay ? { halfDayPeriod } : {}),
  };
}

/** B-mobil-006: "N gün izninizden düşülecek" / "Bu tarihlerde düşülecek gün yok". */
export function previewSummaryText(preview: LeaveRequestPreview | null): string | null {
  if (!preview) return null;
  return preview.days > 0
    ? `${formatDays(preview.days)} gün izninizden düşülecek`
    : "Bu tarihlerde düşülecek gün yok";
}

/** `LeaveDomainErrors.RequestZeroDays` (`LeaveDomainErrors.cs:25`) - same fact as `previewSummaryText`'s 0-day message. */
const ZERO_DAYS_WARNING_CODE = "leave.errors.zero_days";
/** `LeaveRequestCore.DocumentRequired` (`LeaveRequestCore.cs:42`) - same fact as `LeaveDocumentsSection`'s hint. */
const DOCUMENT_REQUIRED_WARNING_CODE = "leave.errors.document_required";

/**
 * B17: the preview always adds a `document_required` warning for a `requiresDocument` type (it has
 * no idea what's staged in this unsaved draft) and, once nothing would be deducted, a
 * `zero_days` warning duplicating `previewSummaryText`'s own message - both already have a client
 * counterpart on screen (the "Belge ekle" hint, the red 0-day summary), so showing the server's copy
 * too is pure repetition. Matched by **code**, never by message text - a translated/reworded message
 * must not silently stop being recognized as the same fact. Every other warning (balance/team
 * overlap/anything else) still gets through unchanged.
 */
export function filterRedundantWarnings(
  warnings: LeaveRequestPreview["warnings"],
  context: { previewDaysZero: boolean; requiresDocument: boolean }
): LeaveRequestPreview["warnings"] {
  return warnings.filter((warning) => {
    if (warning.code === ZERO_DAYS_WARNING_CODE && context.previewDaysZero) return false;
    if (warning.code === DOCUMENT_REQUIRED_WARNING_CODE && context.requiresDocument) return false;
    return true;
  });
}

/** B-mobil-004: a `requiresDocument` type needs at least one uploaded document before it can be submitted. */
export function hasRequiredDocument(
  type: Pick<RequestableLeaveType, "requiresDocument"> | null,
  documentCount: number
): boolean {
  return !type?.requiresDocument || documentCount > 0;
}

function sanitizeFileNameSegment(value: string): string {
  return value
    .replace(/[\\/:*?"<>|]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function extensionFromMimeType(mimeType: string): string {
  return mimeType === "image/png" ? "png" : "jpg";
}

/**
 * B10: the camera returns a UUID filename (e.g. "a2d952ee-...jpeg") that HR/the approver would
 * otherwise see verbatim as the attachment's name (`leave_request_attachments.file_name`). Build a
 * human-readable one instead - leave type name + local date/time, e.g.
 * "Hastalık izni 2026-09-28 13.24.jpg" (":" is not a safe filename character on every platform, so
 * the time separator is "." instead). Falls back to "Belge" when the leave type name is unknown.
 * Picker-selected files keep their own original name (`asset.name`) - this is only for camera shots.
 */
export function buildCapturedPhotoFileName(
  leaveTypeName: string | null | undefined,
  capturedAt: Date,
  mimeType: string
): string {
  const label = sanitizeFileNameSegment(leaveTypeName?.trim() || "Belge") || "Belge";
  const year = capturedAt.getFullYear();
  const month = String(capturedAt.getMonth() + 1).padStart(2, "0");
  const day = String(capturedAt.getDate()).padStart(2, "0");
  const hours = String(capturedAt.getHours()).padStart(2, "0");
  const minutes = String(capturedAt.getMinutes()).padStart(2, "0");
  return `${label} ${year}-${month}-${day} ${hours}.${minutes}.${extensionFromMimeType(mimeType)}`;
}

const HALF_DAY_PERIOD_LABELS: Record<LeaveHalfDayPeriod, string> = {
  morning: "Sabah",
  afternoon: "Öğleden sonra",
};

/** Debounce delay (ms) before a form field change triggers a new preview request (B-mobil-006). */
const PREVIEW_DEBOUNCE_MS = 300;

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
  const [description, setDescription] = useState("");
  const [documents, setDocuments] = useState<StagedDocument[]>([]);
  const [uploadingDocument, setUploadingDocument] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const [preview, setPreview] = useState<LeaveRequestPreview | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const previewRequestIdRef = useRef(0);

  const selectedType = requestableTypes.find((t) => t.id === selectedTypeId) ?? null;
  const { isFullDay, isHalfDay, isHourly } = leaveUnitFlags(selectedType?.unit);
  const showDocuments = !!selectedType?.requiresDocument;
  const showSubstitute = !!selectedType?.requiresSubstitute;
  const missingRequiredDocument = !hasRequiredDocument(selectedType, documents.length);
  const hourlyDuration = isHourly ? formatHourlyDuration(startTime, endTime) : null;
  const previewBlocksSubmit = preview !== null && preview.days <= 0;

  // B-mobil-006: re-preview whenever type/date/time/half-day changes, debounced; a request id guard
  // discards a stale (slower) response that resolves after a newer one already landed.
  useEffect(() => {
    const input = buildPreviewInput(
      selectedType,
      startDate,
      endDate,
      startTime,
      endTime,
      halfDayPeriod
    );
    if (!input) {
      setPreview(null);
      setPreviewError(null);
      setPreviewLoading(false);
      return;
    }

    const requestId = ++previewRequestIdRef.current;
    setPreviewLoading(true);
    const handle = setTimeout(() => {
      leaveService
        .previewMyLeaveRequest(input)
        .then((result) => {
          if (previewRequestIdRef.current !== requestId) return;
          setPreview(result);
          setPreviewError(null);
        })
        .catch((previewErr: unknown) => {
          if (previewRequestIdRef.current !== requestId) return;
          setPreview(null);
          setPreviewError(getApiErrorMessage(previewErr, "Önizleme yüklenemedi."));
        })
        .finally(() => {
          if (previewRequestIdRef.current !== requestId) return;
          setPreviewLoading(false);
        });
    }, PREVIEW_DEBOUNCE_MS);

    return () => clearTimeout(handle);
    // `selectedType` is a stable reference from `requestableTypes.find()` as long as the list
    // itself hasn't been refetched, so it's safe as a dependency here.
  }, [selectedType, startDate, endDate, startTime, endTime, halfDayPeriod]);

  function selectType(id: string) {
    setSelectedTypeId(id);
    // A different type's "vekil" is not automatically valid for the newly selected one.
    setSubstitute(null);
  }

  function changeStartDate(value: string) {
    setStartDate(value);
    // D-mobil-005: keep the end date from silently trailing before the (now later) start date.
    setEndDate((current) => clampEndDate(value, current));
  }

  /** B-mobil-004: uploads via the presigned-URL flow and stages the resulting document id/name. */
  async function handleDocumentPicked(file: PickedDocumentFile) {
    if (!isDocumentFileSizeAllowed(file.size)) {
      Alert.alert(
        "Dosya yüklenemedi",
        "Dosya boyutu okunamadı ya da 100 MB sınırını aşıyor. Lütfen başka bir dosya seçin."
      );
      return;
    }
    setUploadingDocument(true);
    try {
      const documentId = await uploadMyDocument(file);
      setDocuments((prev) => [...prev, { id: documentId, name: file.name }]);
    } catch (uploadError) {
      Alert.alert(
        "Hata",
        getApiErrorMessage(uploadError, "Belge yüklenemedi. Lütfen tekrar deneyin.")
      );
    } finally {
      setUploadingDocument(false);
    }
  }

  async function captureDocumentPhoto() {
    const permission = await ImagePicker.requestCameraPermissionsAsync();
    if (!permission.granted) {
      Alert.alert(
        "Kamera izni gerekli",
        "Belge fotoğrafı çekebilmek için ayarlardan kamera iznini açmanız gerekir."
      );
      return;
    }
    const result = await ImagePicker.launchCameraAsync({ mediaTypes: ["images"], quality: 0.8 });
    if (result.canceled || result.assets.length === 0) {
      return;
    }
    const asset = result.assets[0];
    const mimeType = asset.mimeType ?? "image/jpeg";
    // B10: never use the camera's own UUID filename - build a readable one instead.
    await handleDocumentPicked({
      uri: asset.uri,
      name: buildCapturedPhotoFileName(selectedType?.name, new Date(), mimeType),
      mimeType,
      size: asset.fileSize ?? 0,
    });
  }

  async function pickDocumentFile() {
    const result = await DocumentPicker.getDocumentAsync({
      type: ["application/pdf", "image/*"],
      copyToCacheDirectory: true,
    });
    if (result.canceled || result.assets.length === 0) {
      return;
    }
    const asset = result.assets[0];
    // B10: a user-picked file already has a meaningful name - keep it as-is.
    await handleDocumentPicked({
      uri: asset.uri,
      name: asset.name,
      mimeType: asset.mimeType ?? "application/octet-stream",
      size: asset.size ?? 0,
    });
  }

  function removeDocument(documentId: string) {
    setDocuments((prev) => prev.filter((doc) => doc.id !== documentId));
  }

  async function submitRequest() {
    if (!selectedTypeId || !selectedType) {
      Alert.alert("Eksik bilgi", "Lütfen bir izin türü seçin.");
      return;
    }
    if (!startDate) {
      Alert.alert("Eksik bilgi", "Lütfen bir tarih girin.");
      return;
    }
    if (isFullDay) {
      if (!endDate) {
        Alert.alert("Eksik bilgi", "Lütfen başlangıç ve bitiş tarihini girin.");
        return;
      }
      if (endDate < startDate) {
        Alert.alert("Geçersiz tarih", "Bitiş tarihi başlangıç tarihinden önce olamaz.");
        return;
      }
    }
    if (isHourly && !hourlyDuration) {
      Alert.alert("Geçersiz saat", "Bitiş saati başlangıç saatinden sonra olmalıdır.");
      return;
    }
    if (selectedType.requiresDescription && description.trim().length === 0) {
      Alert.alert("Eksik bilgi", "Bu izin türü için açıklama zorunludur.");
      return;
    }
    if (showSubstitute && !substitute) {
      Alert.alert("Eksik bilgi", "Bu izin türü için bir vekil seçmeniz gerekir.");
      return;
    }
    if (!hasRequiredDocument(selectedType, documents.length)) {
      Alert.alert("Eksik bilgi", "Bu izin türü için en az bir belge eklemeniz gerekir.");
      return;
    }
    if (previewBlocksSubmit) {
      Alert.alert("Gün düşülmeyecek", "Bu tarihlerde izninizden düşülecek gün yok.");
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
      setDocuments([]);
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
    return <Text style={styles.emptyText}>Talep edebileceğiniz bir izin türü yok.</Text>;
  }

  return (
    <>
      <Text style={styles.fieldLabel}>İzin türü</Text>
      <ScrollView horizontal showsHorizontalScrollIndicator={false} style={styles.chipRow}>
        {requestableTypes.map((type) => {
          const selected = type.id === selectedTypeId;
          return (
            <TouchableOpacity
              key={type.id}
              style={[styles.chip, selected && styles.chipSelected]}
              onPress={() => selectType(type.id)}
            >
              <Text style={[styles.chipText, selected && styles.chipTextSelected]}>
                {type.name}
              </Text>
            </TouchableOpacity>
          );
        })}
      </ScrollView>

      <Text style={styles.fieldLabel}>{isFullDay ? "Başlangıç tarihi" : "Tarih"}</Text>
      <DateField value={startDate} onChange={changeStartDate} />

      {isHalfDay && (
        <>
          <Text style={styles.fieldLabel}>Gün yarısı</Text>
          <View style={styles.chipRowWrap}>
            {(Object.keys(HALF_DAY_PERIOD_LABELS) as LeaveHalfDayPeriod[]).map((period) => {
              const selected = period === halfDayPeriod;
              return (
                <TouchableOpacity
                  key={period}
                  style={[styles.chip, selected && styles.chipSelected]}
                  onPress={() => setHalfDayPeriod(period)}
                >
                  <Text style={[styles.chipText, selected && styles.chipTextSelected]}>
                    {HALF_DAY_PERIOD_LABELS[period]}
                  </Text>
                </TouchableOpacity>
              );
            })}
          </View>
        </>
      )}

      {isHourly && (
        <>
          <View style={styles.timeRow}>
            <View style={styles.timeField}>
              <Text style={styles.fieldLabel}>Başlangıç saati</Text>
              <TimeField value={startTime} onChange={setStartTime} />
            </View>
            <View style={styles.timeField}>
              <Text style={styles.fieldLabel}>Bitiş saati</Text>
              <TimeField value={endTime} onChange={setEndTime} />
            </View>
          </View>
          <Text style={hourlyDuration ? styles.previewText : styles.previewTextInvalid}>
            {hourlyDuration ?? "Bitiş saati başlangıçtan sonra olmalı"}
          </Text>
        </>
      )}

      {isFullDay && (
        <>
          <Text style={styles.fieldLabel}>Bitiş tarihi</Text>
          <DateField value={endDate} onChange={setEndDate} minimumDateValue={startDate} />
        </>
      )}

      {/* B-mobil-006: live day-count preview - the whole reason B12 exists (a Sunday-only request
          silently deducted 0 days and the server rejection only showed up after submit). */}
      <View style={styles.previewBox}>
        {previewLoading ? (
          <ActivityIndicator size="small" color={colors.primary} />
        ) : preview ? (
          <>
            <Text style={preview.days > 0 ? styles.previewText : styles.previewTextInvalid}>
              {previewSummaryText(preview)}
            </Text>
            {filterRedundantWarnings(preview.warnings, {
              previewDaysZero: preview.days <= 0,
              requiresDocument: showDocuments,
            }).map((warning) => (
              <Text key={warning.code} style={styles.previewWarning}>
                {warning.message}
              </Text>
            ))}
          </>
        ) : previewError ? (
          <Text style={styles.previewTextInvalid}>{previewError}</Text>
        ) : null}
      </View>

      <Text style={styles.fieldLabel}>
        Açıklama {selectedType?.requiresDescription ? "(zorunlu)" : "(opsiyonel)"}
      </Text>
      <TextInput
        style={[styles.input, styles.textArea]}
        value={description}
        onChangeText={setDescription}
        placeholder="Talebinizle ilgili not ekleyin"
        multiline
        numberOfLines={3}
      />

      {showSubstitute && (
        <>
          <Text style={styles.fieldLabel}>Vekil (zorunlu)</Text>
          {substitute ? (
            <View style={styles.substituteRow}>
              <Text style={styles.substituteName}>{substitute.displayName}</Text>
              <TouchableOpacity onPress={() => setSubstitutePickerVisible(true)}>
                <Text style={styles.linkText}>Değiştir</Text>
              </TouchableOpacity>
            </View>
          ) : (
            <TouchableOpacity
              style={styles.secondaryButton}
              onPress={() => setSubstitutePickerVisible(true)}
            >
              <Text style={styles.secondaryButtonText}>Vekil seç</Text>
            </TouchableOpacity>
          )}
        </>
      )}

      {showDocuments && (
        <LeaveDocumentsSection
          documents={documents}
          uploading={uploadingDocument}
          missingRequired={missingRequiredDocument}
          onCapturePhoto={() => void captureDocumentPhoto()}
          onPickFile={() => void pickDocumentFile()}
          onRemove={removeDocument}
        />
      )}

      <TouchableOpacity
        style={[
          styles.submitButton,
          (submitting || uploadingDocument || missingRequiredDocument || previewBlocksSubmit) &&
            styles.submitButtonDisabled,
        ]}
        onPress={() => void submitRequest()}
        disabled={submitting || uploadingDocument || missingRequiredDocument || previewBlocksSubmit}
      >
        {submitting ? (
          <ActivityIndicator color={colors.white} />
        ) : (
          <Text style={styles.submitButtonText}>Talebi gönder</Text>
        )}
      </TouchableOpacity>

      <SubstitutePicker
        visible={substitutePickerVisible}
        onClose={() => setSubstitutePickerVisible(false)}
        onSelect={(candidate) => {
          setSubstitute(candidate);
          setSubstitutePickerVisible(false);
        }}
        excludeEmployeeId={authUser?.employeeId}
      />
    </>
  );
}

const styles = StyleSheet.create({
  emptyText: {
    ...typography.bodySm,
    color: colors.textTertiary,
  },
  fieldLabel: {
    ...typography.caption,
    color: colors.textSecondary,
    marginTop: spacing.md,
    marginBottom: spacing.xs,
  },
  chipRow: {
    flexDirection: "row",
  },
  chipRowWrap: {
    flexDirection: "row",
    flexWrap: "wrap",
  },
  chip: {
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.lg,
    borderRadius: 20,
    borderWidth: 1,
    borderColor: colors.border,
    marginRight: spacing.sm,
    marginBottom: spacing.sm,
    backgroundColor: colors.gray50,
  },
  chipSelected: {
    backgroundColor: colors.primary,
    borderColor: colors.primary,
  },
  chipText: {
    ...typography.bodySm,
    color: colors.text,
  },
  chipTextSelected: {
    color: colors.white,
  },
  input: {
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 8,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    ...typography.body,
    color: colors.text,
    backgroundColor: colors.white,
  },
  textArea: {
    minHeight: 72,
    textAlignVertical: "top",
  },
  timeRow: {
    flexDirection: "row",
    gap: spacing.sm,
  },
  timeField: {
    flex: 1,
  },
  previewBox: {
    marginTop: spacing.sm,
  },
  previewText: {
    ...typography.bodySm,
    color: colors.textSecondary,
  },
  previewTextInvalid: {
    ...typography.bodySm,
    color: colors.error,
  },
  previewWarning: {
    ...typography.bodyXs,
    color: colors.warning,
    marginTop: spacing.xs,
  },
  substituteRow: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 8,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    backgroundColor: colors.white,
  },
  substituteName: {
    ...typography.body,
    color: colors.text,
  },
  linkText: {
    ...typography.bodyXs,
    color: colors.error,
  },
  secondaryButton: {
    flex: 1,
    backgroundColor: colors.gray50,
    borderWidth: 1,
    borderColor: colors.border,
    borderRadius: 8,
    paddingVertical: spacing.sm,
    alignItems: "center",
  },
  secondaryButtonText: {
    ...typography.button,
    color: colors.text,
  },
  submitButton: {
    backgroundColor: colors.primary,
    borderRadius: 8,
    paddingVertical: spacing.md,
    alignItems: "center",
    marginTop: spacing.lg,
  },
  submitButtonDisabled: {
    opacity: 0.6,
  },
  submitButtonText: {
    ...typography.button,
    color: colors.white,
  },
});
