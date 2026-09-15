import { useEffect, useState, useCallback, useRef } from "react";

interface UseAsyncState<T> {
  status: "idle" | "loading" | "success" | "error";
  data: T | null;
  error: Error | null;
}

interface UseAsyncOptions {
  onSuccess?: () => void;
  onError?: (error: Error) => void;
}

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

  const executeRef = useRef<(() => Promise<T>) | undefined>(undefined);

  const execute = useCallback(async () => {
    setState({ status: "loading", data: null, error: null });
    try {
      const response = await asyncFunction();
      setState({ status: "success", data: response, error: null });
      options?.onSuccess?.();
      return response;
    } catch (error) {
      const err = error instanceof Error ? error : new Error(String(error));
      setState({ status: "error", data: null, error: err });
      options?.onError?.(err);
      throw err;
    }
  }, [asyncFunction, options]);

  executeRef.current = execute;

  useEffect(() => {
    if (immediate) {
      execute();
    }
  }, [execute, immediate]);

  return { ...state, execute };
}
