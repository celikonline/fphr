namespace Sense.Modules.Attendance.Application.SelfCheckIns;

/// <summary>Great-circle distance (S-31 GPS geofence check) - Haversine formula, accurate enough for a few-hundred-meter radius.</summary>
internal static class GeoDistance
{
    private const double EarthRadiusMeters = 6_371_000;

    public static double Meters(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2)) +
                (Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMeters * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}
