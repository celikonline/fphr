/** Reads (never verifies) a JWT payload; the server stays the authority on every claim. */
export function decodeJwtPayload(token: string | null | undefined): Record<string, unknown> | null {
  if (!token) return null;
  const part = token.split(".")[1];
  if (!part) return null;
  try {
    const base64 = part.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(part.length / 4) * 4, "=");
    const json = decodeURIComponent(
      atob(base64)
        .split("")
        .map((c) => `%${c.charCodeAt(0).toString(16).padStart(2, "0")}`)
        .join("")
    );
    return JSON.parse(json) as Record<string, unknown>;
  } catch {
    return null;
  }
}

/** `act` claim (ClaimNames.Impersonator): set only on tokens issued through an approved support-access grant. */
export function getImpersonatorId(token: string | null | undefined): string | null {
  const act = decodeJwtPayload(token)?.act;
  return typeof act === "string" && act.length > 0 ? act : null;
}
