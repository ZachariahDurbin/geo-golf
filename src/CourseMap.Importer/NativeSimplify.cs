using System.Runtime.InteropServices;

namespace CourseMap.Importer;

public static class NativeSimplify
{
    // Linux shared library name (we’ll copy it next to the app)
    private const string LibName = "liblinesimplify.so";

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int simplify_linestring(
        double[] lon,
        double[] lat,
        int n,
        double epsilonMeters,
        double[] outLon,
        double[] outLat);

    public static (double[] lon, double[] lat) Simplify(double[] lon, double[] lat, double epsilonMeters)
    {
        if (lon.Length != lat.Length) throw new ArgumentException("lon/lat length mismatch");
        if (lon.Length < 2) return (lon, lat);

        var outLon = new double[lon.Length];
        var outLat = new double[lat.Length];

        var outCount = simplify_linestring(lon, lat, lon.Length, epsilonMeters, outLon, outLat);
        if (outCount <= 0) return (lon, lat);

        Array.Resize(ref outLon, outCount);
        Array.Resize(ref outLat, outCount);
        return (outLon, outLat);
    }
}
