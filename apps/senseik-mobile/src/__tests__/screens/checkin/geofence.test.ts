import i18n from "@/lib/i18n";
import {
  distanceMeters,
  formatDistance,
  isInsideGeofence,
  nearestLocation,
  punchLocationId,
  toGeofence,
} from "@/screens/app/checkin/geofence";
import type { CheckInLocation } from "@/services/AttendanceService";

const office: CheckInLocation = {
  locationId: "loc-office",
  name: "Merkez Ofis",
  latitude: 39.92,
  longitude: 32.85,
  radiusMeters: 150,
};

/** ~0.001° latitude ≈ 111 m. */
const nearOffice = { latitude: 39.921, longitude: 32.85 };
const farFromOffice = { latitude: 39.93, longitude: 32.85 };

describe("distanceMeters (haversine)", () => {
  it("is zero for the same point", () => {
    expect(distanceMeters(nearOffice, nearOffice)).toBe(0);
  });

  it("measures one degree of latitude as ~111.2 km", () => {
    const meters = distanceMeters({ latitude: 0, longitude: 0 }, { latitude: 1, longitude: 0 });
    expect(meters).toBeCloseTo(111_195, 0);
  });

  it("matches a known city distance (Ankara → İstanbul ~350 km)", () => {
    const meters = distanceMeters(
      { latitude: 39.9334, longitude: 32.8597 },
      { latitude: 41.0082, longitude: 28.9784 }
    );
    expect(meters / 1000).toBeGreaterThan(345);
    expect(meters / 1000).toBeLessThan(355);
  });

  it("is symmetric", () => {
    expect(distanceMeters(nearOffice, farFromOffice)).toBeCloseTo(
      distanceMeters(farFromOffice, nearOffice),
      6
    );
  });
});

describe("toGeofence", () => {
  it("builds a geofence only when center and radius are all present", () => {
    expect(toGeofence(office)).toEqual({
      locationId: "loc-office",
      name: "Merkez Ofis",
      center: { latitude: 39.92, longitude: 32.85 },
      radiusMeters: 150,
    });
    expect(toGeofence({ ...office, radiusMeters: null })).toBeNull();
    expect(toGeofence({ ...office, latitude: null })).toBeNull();
    expect(toGeofence({ locationId: "loc-x", name: "Depo" })).toBeNull();
  });
});

describe("nearestLocation", () => {
  it("is null for a roaming employee (no locations)", () => {
    expect(nearestLocation([], nearOffice)).toBeNull();
  });

  it("picks the closest location with a center and reports its distance", () => {
    const branch: CheckInLocation = {
      locationId: "loc-branch",
      name: "Şube",
      latitude: 39.93,
      longitude: 32.85,
      radiusMeters: 100,
    };
    const nearest = nearestLocation([branch, office], nearOffice);
    expect(nearest?.location.locationId).toBe("loc-office");
    expect(nearest?.distanceMeters).toBeCloseTo(111, -1);

    expect(nearestLocation([office, branch], farFromOffice)?.location.locationId).toBe(
      "loc-branch"
    );
  });

  it("ranks a location without a center after one with a distance", () => {
    const noCenter: CheckInLocation = { locationId: "loc-x", name: "Depo" };
    expect(nearestLocation([noCenter, office], nearOffice)?.location.locationId).toBe("loc-office");
  });

  it("returns the first location with an unknown distance before the position is known", () => {
    const nearest = nearestLocation([office], null);
    expect(nearest?.location.locationId).toBe("loc-office");
    expect(nearest?.distanceMeters).toBeNull();
  });
});

describe("isInsideGeofence (display only)", () => {
  it("is true within the radius and false outside it", () => {
    expect(isInsideGeofence(nearestLocation([office], nearOffice))).toBe(true);
    expect(isInsideGeofence(nearestLocation([office], farFromOffice))).toBe(false);
  });

  it("counts the exact radius as inside, like the server", () => {
    const edge = {
      ...office,
      radiusMeters: distanceMeters(nearOffice, { latitude: 39.92, longitude: 32.85 }),
    };
    expect(isInsideGeofence(nearestLocation([edge], nearOffice))).toBe(true);
  });

  it("has no verdict without a geofence or a position", () => {
    expect(isInsideGeofence(null)).toBeNull();
    expect(
      isInsideGeofence(nearestLocation([{ ...office, radiusMeters: null }], nearOffice))
    ).toBeNull();
    expect(isInsideGeofence(nearestLocation([office], null))).toBeNull();
  });
});

describe("punchLocationId (Karar 2)", () => {
  it("sends the locationId only for a location with a radius", () => {
    expect(punchLocationId(nearestLocation([office], nearOffice))).toBe("loc-office");
    // Outside too: the server rejects it, the client never decides.
    expect(punchLocationId(nearestLocation([office], farFromOffice))).toBe("loc-office");
  });

  it("sends nothing for a roaming employee or a location without a radius", () => {
    expect(punchLocationId(null)).toBeUndefined();
    expect(punchLocationId(nearestLocation([], nearOffice))).toBeUndefined();
    expect(
      punchLocationId(nearestLocation([{ ...office, radiusMeters: null }], nearOffice))
    ).toBeUndefined();
  });
});

describe("formatDistance", () => {
  it("shows meters under a kilometer and kilometers above it", async () => {
    expect(formatDistance(123.6)).toBe("124 m");
    expect(formatDistance(1234)).toBe("1,2 km");
    await i18n.changeLanguage("en");
    expect(formatDistance(1234)).toBe("1.2 km");
  });
});
