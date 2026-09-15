import { useEffect, useState, useCallback } from "react";

interface UseAsyncState<T> {
  status: "idle" | "loading" | "success" | "error";
  data: T | null;
  error: Error | null;
}

interface UseAsyncOptions {
  onSuccess?: () => void;
  onError?: (error: Error) => void;
}

const LOADING = { status: "loading", data: null, error: null } as const;

/**
 * Custom hook for handling async operations (API calls)
 *
 * @param asyncFunction - Async function to execute
 * @param immediate - Execute immediately on mount (default: true)
 * @param options - Callbacks for success/error
 * @returns State object with status, data, error, and execute function
 *
 * @example
 * const { data, status, error } = useAsync(async () => {
 *   const response = await apiClient.get("/companies");
 *   return response.data;
 * });
 */
export function useAsync<T>(
  asyncFunction: () => Promise<T>,
  immediate = true,
  options?: UseAsyncOptions
) {
  const [state, setState] = useState<UseAsyncState<T>>({
    status: "idle",
    data: null,
    error: null,
  });

  /** Runs the async function and records its outcome; the caller is responsible for the loading state. */
  const run = useCallback(
    (): Promise<T> =>
      // Same semantics as an async wrapper: asyncFunction is called synchronously and a sync throw rejects.
      // Outcomes are recorded in promise callbacks, so starting a run from an effect sets no state synchronously.
      new Promise<T>((resolve) => resolve(asyncFunction()))
        .then(
          (response) => {
            setState({ status: "success", data: response, error: null });
            options?.onSuccess?.();
            return response;
          },
          (error: unknown) => {
            const err = error instanceof Error ? error : new Error(String(error));
            setState({ status: "error", data: null, error: err });
            options?.onError?.(err);
            throw err;
          }
        ),
    [asyncFunction, options]
  );

  const execute = useCallback(() => {
    setState(LOADING);
    return run();
  }, [run]);

  // Immediate mode re-runs whenever `run` or `immediate` changes. The loading state is entered during
  // render when that happens, so the effect below only has to start the request.
  const [immediateRun, setImmediateRun] = useState<{ run: typeof run; immediate: boolean } | null>(null);
  if (immediateRun?.run !== run || immediateRun.immediate !== immediate) {
    setImmediateRun({ run, immediate });
    if (immediate) setState(LOADING);
  }

  useEffect(() => {
    if (immediate) {
      run();
    }
  }, [run, immediate]);

  return { ...state, execute };
}
