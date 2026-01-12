#include <vector>
#include <cstdint>
#include <cstdlib>

#if defined(_WIN32)
  #define API __declspec(dllexport)
#else
  #define API __attribute__((visibility("default")))
#endif

extern "C" {

// Returns number of output points written to outLon/outLat.
// Caller allocates outLon/outLat arrays of size >= n.
// This placeholder keeps every 2nd point + last.
API int simplify_linestring(
    const double* lon,
    const double* lat,
    int n,
    double /*epsilonMeters*/,
    double* outLon,
    double* outLat)
{
    if (!lon || !lat || !outLon || !outLat || n <= 0) return 0;
    if (n == 1) { outLon[0] = lon[0]; outLat[0] = lat[0]; return 1; }

    int out = 0;
    for (int i = 0; i < n; i += 2) {
        outLon[out] = lon[i];
        outLat[out] = lat[i];
        out++;
    }

    // Ensure last point included
    if (outLon[out-1] != lon[n-1] || outLat[out-1] != lat[n-1]) {
        outLon[out] = lon[n-1];
        outLat[out] = lat[n-1];
        out++;
    }

    return out;
}

}
