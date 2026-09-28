/**
 * `denied`: foreground permission refused; `servicesOff`: the device's location services (GPS) are
 * turned off system-wide; `unavailable`: permission granted (and services on) but no fix could be
 * read, including a first-fix timeout.
 */
export type LocationStatus = "pending" | "denied" | "servicesOff" | "unavailable" | "ready";

/**
 * Whether coming back to the foreground (or a pull-to-refresh) should re-check location by itself.
 * After `denied`: the user may be back from granting it in the system settings. After `servicesOff`:
 * the user may be back from turning location services on. `unavailable` usually means Google's
 * "Location Accuracy" dialog was declined, or the first fix just timed out; restarting the watch on
 * `unavailable` risks reopening that dialog on every foreground - an endless loop. That case waits
 * for an explicit "Tekrar dene".
 */
export function shouldRetryOnForeground(status: LocationStatus): boolean {
  return status === "denied" || status === "servicesOff";
}
