import { useEffect, useRef, useState } from "react";
import {
  leaveService,
  type LeaveHalfDayPeriod,
  type LeaveRequestPreview,
  type RequestableLeaveType,
} from "@/services/LeaveService";
import { getApiErrorMessage } from "@/lib/apiError";
import { buildPreviewInput } from "./leaveRequestRules";

/** Debounce delay (ms) before a form field change triggers a new preview request (B-mobil-006). */
const PREVIEW_DEBOUNCE_MS = 300;

/** `LeaveRequestForm`'s live day-count preview (moved out of the form for the file-size budget). */
export function useLeavePreview(
  selectedType: Pick<RequestableLeaveType, "id" | "unit"> | null,
  startDate: string,
  endDate: string,
  startTime: string,
  endTime: string,
  halfDayPeriod: LeaveHalfDayPeriod
): { preview: LeaveRequestPreview | null; previewLoading: boolean; previewError: string | null } {
  const [preview, setPreview] = useState<LeaveRequestPreview | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const previewRequestIdRef = useRef(0);

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

  return { preview, previewLoading, previewError };
}
