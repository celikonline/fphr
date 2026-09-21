import { useQuery } from "@tanstack/react-query";
import { getEmployeePhoto, OWN_PHOTO_TARGET } from "@/services/employees.service";
import { useAuthStore } from "@/store/auth.store";

const PHOTO_STALE_MS = 5 * 60 * 1000;

/**
 * One object URL per photo target, created lazily and revoked when its blob is replaced (upload/removal). Keeping
 * the URL in a module cache instead of component state + effect cleanup survives React StrictMode's mount/unmount
 * replay in dev, and every avatar of the same person shares one URL.
 */
const urlsByTarget = new Map<string, { blob: Blob; url: string }>();

function objectUrlFor(target: string, blob: Blob): string {
  const cached = urlsByTarget.get(target);
  if (cached?.blob === blob) return cached.url;
  if (cached) URL.revokeObjectURL(cached.url);
  const url = URL.createObjectURL(blob);
  urlsByTarget.set(target, { blob, url });
  return url;
}

/** Query key of a photo; uploads/removals invalidate `["employees", "photo"]` to refresh every avatar at once. */
/** The caller's own photo ("me") is also keyed by their employee id, so a different user signing in on the same tab never reuses it. */
export const employeePhotoKey = (target: string, ownEmployeeId = "") => ["employees", "photo", target, ownEmployeeId] as const;

/** The caller's own photo is read through `/me` (needs no directory permission); everyone else's by id. */
export function useOwnEmployeeId(): string | undefined {
  return useAuthStore((state) => state.user?.employeeId) ?? undefined;
}

/**
 * Object URL of an employee's photo, or null while loading / when there is none. Browsers cannot send the bearer
 * token from an <img src>, so the bytes are fetched through the API client and exposed as a blob URL.
 */
export function useEmployeePhotoUrl(employeeId: string | undefined): string | null {
  const ownEmployeeId = useOwnEmployeeId();
  const target = employeeId && employeeId === ownEmployeeId ? OWN_PHOTO_TARGET : employeeId;

  const { data: blob } = useQuery({
    queryKey: employeePhotoKey(target ?? "", ownEmployeeId),
    queryFn: () => getEmployeePhoto(target as string),
    enabled: Boolean(target),
    staleTime: PHOTO_STALE_MS,
    retry: false,
  });

  return blob && target ? objectUrlFor(target, blob) : null;
}
