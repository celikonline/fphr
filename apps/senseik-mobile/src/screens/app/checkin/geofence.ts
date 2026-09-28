import type { CheckInLocation } from "@/services/AttendanceService";
import { activeLocale } from "@/lib/formatters";
import i18n from "@/lib/i18n";
import type { Coordinates } from "./MapHeader";

/** Same Earth radius as the server's `GeoDistance` - both sides measure the same distance. */
const EARTH_RADIUS_METERS = 6_371_000;
const METERS_PER_KM = 1000;

/** A location with a full geofence (center + radius) - the only kind the server verifies. */
export type Geofence = {
  locationId: string;
  name: string;
  center: Coordinates;
  radiusMeters: number;
};

/** The location shown on the check-in screen: its geofence (if any) and the device's distance to it. */
export type NearestLocation = {
  location: CheckInLocation;
  geofence: Geofence | null;
  /** Meters from the device to the geofence center; `null` without a position or a center. */
  distanceMeters: number | null;
};

function toRadians(degrees: number): number {
  return (degrees * Math.PI) / 180;
}

/** Great-circle (haversine) distance in meters - the server's `GeoDistance.Meters` formula. */
export function distanceMeters(from: Coordinates, to: Coordinates): number {
  const dLat = toRadians(to.latitude - from.latitude);
  const dLon = toRadians(to.longitude - from.longitude);
  const a =
    Math.sin(dLat / 2) * Math.sin(dLat / 2) +
    Math.cos(toRadians(from.latitude)) *
      Math.cos(toRadians(to.latitude)) *
      Math.sin(dLon / 2) *
      Math.sin(dLon / 2);
  return EARTH_RADIUS_METERS * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
}

/** The location's geofence, or `null` when its center or radius is missing (the server checks nothing then). */
export function toGeofence(location: CheckInLocation): Geofence | null {
  const { latitude, longitude, radiusMeters } = location;
  if (typeof latitude !== "number" || typeof longitude !== "number") return null;
  if (typeof radiusMeters !== "number") return null;
  return {
    locationId: location.locationId,
    name: location.name,
    center: { latitude, longitude },
    radiusMeters,
  };
}

/**
 * The location closest to `position` (by geofence center); locations without a center rank last.
 * Without a position the first location is returned with an unknown distance. `null` for `[]`.
 */
export function nearestLocation(
  locations: readonly CheckInLocation[],
  position: Coordinates | null
): NearestLocation | null {
  const candidates = locations.map((location) => {
    const geofence = toGeofence(location);
    return {
      location,
      geofence,
      distanceMeters: geofence && position ? distanceMeters(position, geofence.center) : null,
    };
  });
  if (candidates.length === 0) return null;
  return candidates.reduce((best, candidate) => {
    if (candidate.distanceMeters === null) return best;
    if (best.distanceMeters === null || candidate.distanceMeters < best.distanceMeters) {
      return candidate;
    }
    return best;
  });
}

/**
 * Display-only verdict for the "Ofis alanında / Alan dışında" pill: `null` when there is nothing to
 * verify (no geofence) or no position yet. The server decides on its own - never trust this for a punch.
 */
export function isInsideGeofence(nearest: NearestLocation | null): boolean | null {
  if (!nearest?.geofence || nearest.distanceMeters === null) return null;
  return nearest.distanceMeters <= nearest.geofence.radiusMeters;
}

/**
 * The `locationId` a GPS punch carries (Karar 2): only for a location with a full geofence, so the
 * server enforces the radius. Without one (roaming employee, no radius) nothing is sent - today's behavior.
 */
export function punchLocationId(nearest: NearestLocation | null): string | undefined {
  return nearest?.geofence ? nearest.geofence.locationId : undefined;
}

/** "120 m" under a kilometer, "1,2 km" / "1.2 km" above it, in the active language. */
export function formatDistance(meters: number): string {
  if (meters < METERS_PER_KM) {
    return i18n.t("checkin:clock.meters", { value: Math.round(meters) });
  }
  const km = (meters / METERS_PER_KM).toLocaleString(activeLocale(), {
    maximumFractionDigits: 1,
  });
  return i18n.t("checkin:geofence.kilometers", { value: km });
}
