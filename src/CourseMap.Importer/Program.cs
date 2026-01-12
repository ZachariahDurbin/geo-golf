using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var cs = Environment.GetEnvironmentVariable("COURSEMAP_CONNECTION_STRING")
         ?? config.GetConnectionString("CourseMap")
         ?? throw new InvalidOperationException("Missing COURSEMAP_CONNECTION_STRING or ConnectionStrings:CourseMap.");

// ---- Args (simple parser) ----
// Example:
// dotnet run --project src/CourseMap.Importer -- --file data/demo.geojson --course demo-course --replace true
string? file = null;
string courseId = "demo-course";
bool replace = false;
bool validate = true;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--file":
        case "-f":
            file = i + 1 < args.Length ? args[++i] : null;
            break;
        case "--validate":
            var vv = i + 1 < args.Length ? args[++i] : "true";
            validate = !bool.TryParse(vv, out var vb) || vb;
            break;
        case "--course":
        case "-c":
            courseId = i + 1 < args.Length ? args[++i] : courseId;
            break;
        case "--replace":
        case "-r":
            var v = i + 1 < args.Length ? args[++i] : "false";
            replace = bool.TryParse(v, out var b) && b;
            break;
        case "--help":
        case "-h":
            PrintHelp();
            return;
    }
}

if (string.IsNullOrWhiteSpace(file))
{
    PrintHelp();
    return;
}

var path = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), file));

if (!File.Exists(path))
    throw new FileNotFoundException($"GeoJSON not found: {path}");

Console.WriteLine($"Importing: {path}");
Console.WriteLine($"CourseId:  {courseId}");
Console.WriteLine($"Replace:   {replace}");

using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
if (doc.RootElement.GetProperty("type").GetString() != "FeatureCollection")
    throw new InvalidOperationException("Only FeatureCollection is supported for now.");

var features = doc.RootElement.GetProperty("features");

await using var conn = new SqlConnection(cs);
await conn.OpenAsync();

await using var tx = await conn.BeginTransactionAsync();

if (replace)
{
    var del = new SqlCommand("USE CourseMap; DELETE FROM dbo.CourseFeatures WHERE CourseId = @courseId;", conn, (SqlTransaction)tx);
    del.Parameters.AddWithValue("@courseId", courseId);
    var deleted = await del.ExecuteNonQueryAsync();
    Console.WriteLine($"Deleted {deleted} existing features for course '{courseId}'.");
}

int inserted = 0;

foreach (var f in features.EnumerateArray())
{
    var props = f.TryGetProperty("properties", out var p) ? p : default;
    var geom = f.GetProperty("geometry");

    var featureType = props.ValueKind != JsonValueKind.Undefined && props.TryGetProperty("featureType", out var ft)
        ? ft.GetString() ?? "unknown"
        : "unknown";

    var name = props.ValueKind != JsonValueKind.Undefined && props.TryGetProperty("name", out var nm)
        ? nm.GetString()
        : null;

    // store all properties as-is (handy later)
    var propsJson = props.ValueKind == JsonValueKind.Undefined ? "{}" : props.GetRawText();

    var wkt = GeoJsonGeometryToWkt(geom);

    var sql = @"
USE CourseMap;
INSERT INTO dbo.CourseFeatures (CourseId, FeatureType, Name, Geog, PropertiesJson)
VALUES (@courseId, @featureType, @name, geography::STGeomFromText(@wkt, 4326), @propsJson);
";

    await using var cmd = new SqlCommand(sql, conn, (SqlTransaction)tx);
    cmd.Parameters.AddWithValue("@courseId", courseId);
    cmd.Parameters.AddWithValue("@featureType", featureType);
    cmd.Parameters.AddWithValue("@name", (object?)name ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@wkt", wkt);
    cmd.Parameters.AddWithValue("@propsJson", propsJson);

    await cmd.ExecuteNonQueryAsync();
    inserted++;
}

await tx.CommitAsync();

if (validate)
{
    // No transaction needed for read-only validation
    var report = await CourseMap.Importer.Validator.ValidateAsync(conn, courseId);

    var repoRoot = Directory.GetCurrentDirectory();
    var artifactsDir = Path.Combine(repoRoot, "artifacts");
    Directory.CreateDirectory(artifactsDir);

    var outPath = Path.Combine(artifactsDir, "validation_report.json");
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(outPath, json);

    Console.WriteLine($"Validation report: {outPath}");
    Console.WriteLine($"Violations: {report.ViolationsCount}");

    if (report.ViolationsCount > 0)
        Environment.ExitCode = 2;
}

Console.WriteLine($"Inserted {inserted} features.");

static void PrintHelp()
{
    Console.WriteLine("""
CourseMap.Importer
Usage:
  dotnet run --project src/CourseMap.Importer -- --file data/demo.geojson --course demo-course --replace true

Options:
  --file,   -f   Path to GeoJSON FeatureCollection
  --course, -c   CourseId to stamp on inserted rows (default: demo-course)
  --replace,-r   true/false - delete existing rows for that course first
""");
}

// ---------------- Geometry conversion ----------------

static string GeoJsonGeometryToWkt(JsonElement geometry)
{
    var type = geometry.GetProperty("type").GetString();
    if (type is null) throw new InvalidOperationException("Geometry missing type.");

    return type switch
    {
        "Point" => PointToWkt(geometry.GetProperty("coordinates")),
        "LineString" => LineStringToWkt(geometry.GetProperty("coordinates")),
        "Polygon" => PolygonToWkt(geometry.GetProperty("coordinates")),
        "MultiPolygon" => MultiPolygonToWkt(geometry.GetProperty("coordinates")),
        _ => throw new NotSupportedException($"Unsupported geometry type: {type}")
    };
}

// GeoJSON coordinate: [lon, lat]
static (double lon, double lat) ReadLonLat(JsonElement coord)
{
    if (coord.ValueKind != JsonValueKind.Array || coord.GetArrayLength() < 2)
        throw new InvalidOperationException("Invalid coordinate (expected [lon,lat]).");

    var lon = coord[0].GetDouble();
    var lat = coord[1].GetDouble();
    return (lon, lat);
}

static string PointToWkt(JsonElement coords)
{
    var (lon, lat) = ReadLonLat(coords);
    return $"POINT({lon} {lat})";
}

static string LineStringToWkt(JsonElement coords)
{
    var points = coords.EnumerateArray()
        .Select(ReadLonLat)
        .ToList();

    if (points.Count < 2) throw new InvalidOperationException("LineString needs at least 2 points.");

    var lon = points.Select(p => p.lon).ToArray();
    var lat = points.Select(p => p.lat).ToArray();

    // epsilon in meters (tweakable / make CLI arg later)
    var (slon, slat) = CourseMap.Importer.NativeSimplify.Simplify(lon, lat, epsilonMeters: 2.0);

    Console.WriteLine($"Simplify LineString: {lon.Length} -> {slon.Length} points");

    var pts = Enumerable.Range(0, slon.Length)
        .Select(i => $"{slon[i]} {slat[i]}")
        .ToList();

    return $"LINESTRING({string.Join(", ", pts)})";
}


// Polygon coordinates: array of rings, each ring is array of [lon,lat]
static string PolygonToWkt(JsonElement rings)
{
    var ringTexts = new List<string>();

    foreach (var ring in rings.EnumerateArray())
    {
        var pts = ring.EnumerateArray()
            .Select(c => {
                var (lon, lat) = ReadLonLat(c);
                return (lon, lat);
            }).ToList();

        if (pts.Count < 4) throw new InvalidOperationException("Polygon ring needs at least 4 points.");

        // Ensure ring closed
        if (pts[0] != pts[^1])
            pts.Add(pts[0]);

        var txt = string.Join(", ", pts.Select(p => $"{p.lon} {p.lat}"));
        ringTexts.Add($"({txt})");
    }

    if (ringTexts.Count == 0) throw new InvalidOperationException("Polygon requires at least one ring.");

    return $"POLYGON({string.Join(", ", ringTexts)})";
}

// MultiPolygon: array of polygons, each polygon is array of rings
static string MultiPolygonToWkt(JsonElement polygons)
{
    var polyTexts = new List<string>();

    foreach (var poly in polygons.EnumerateArray())
    {
        // poly is rings
        var rings = new List<string>();
        foreach (var ring in poly.EnumerateArray())
        {
            var pts = ring.EnumerateArray()
                .Select(c => ReadLonLat(c)).ToList();

            if (pts.Count < 4) throw new InvalidOperationException("Polygon ring needs at least 4 points.");
            if (pts[0] != pts[^1]) pts.Add(pts[0]);

            var txt = string.Join(", ", pts.Select(p => $"{p.lon} {p.lat}"));
            rings.Add($"({txt})");
        }

        polyTexts.Add($"({string.Join(", ", rings)})");
    }

    if (polyTexts.Count == 0) throw new InvalidOperationException("MultiPolygon requires at least one polygon.");

    return $"MULTIPOLYGON({string.Join(", ", polyTexts)})";
}
