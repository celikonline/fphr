/** `denied`: foreground permission refused; `unavailable`: permission granted but no fix could be read. */
export type LocationStatus = "pending" | "denied" | "unavailable" | "ready";

/**
 * Whether coming back to the foreground (or a pull-to-refresh) should re-check location by itself.
 * Only after `denied`: the user may be back from granting it in the system settings. `unavailable`
 * usually means Google's "Location Accuracy" dialog was declined; restarting the watch reopens that
 * dialog, and its closing brings the app to the foreground again - an endless loop. That case waits
 * for an explicit "Tekrar dene".
 */
export function shouldRetryOnForeground(status: LocationStatus): boolean {
  return status === "denied";
}
