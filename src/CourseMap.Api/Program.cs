using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);

var cs = Environment.GetEnvironmentVariable("COURSEMAP_CONNECTION_STRING")
         ?? builder.Configuration.GetConnectionString("CourseMap")
         ?? throw new InvalidOperationException("Missing COURSEMAP_CONNECTION_STRING or ConnectionStrings:CourseMap.");

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { ok = true }));

// GET /contains?lat=39.1007&lon=-94.5772
app.MapGet("/contains", async (double lat, double lon) =>
{
    const string sql = @"
USE CourseMap;
DECLARE @p geography = geography::Point(@lat, @lon, 4326);
SELECT TOP (50) FeatureType, Name
FROM dbo.CourseFeatures
WHERE Geog.STContains(@p) = 1;";

    await using var conn = new SqlConnection(cs);
    await conn.OpenAsync();

    await using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@lat", lat);
    cmd.Parameters.AddWithValue("@lon", lon);

    await using var r = await cmd.ExecuteReaderAsync();
    var items = new List<object>();
    while (await r.ReadAsync())
        items.Add(new { FeatureType = r.GetString(0), Name = r.IsDBNull(1) ? null : r.GetString(1) });

    return Results.Ok(items);
});

// GET /within?lat=39.1007&lon=-94.5772&radiusMeters=200
app.MapGet("/within", async (double lat, double lon, double radiusMeters, string? type) =>
{
    var sql = @"
USE CourseMap;
DECLARE @p geography = geography::Point(@lat, @lon, 4326);
SELECT TOP (200) FeatureType, Name, Geog.STDistance(@p) AS DistanceMeters
FROM dbo.CourseFeatures
WHERE (@type IS NULL OR FeatureType = @type)
  AND Geog.STDistance(@p) <= @radiusMeters
ORDER BY DistanceMeters ASC;";

    await using var conn = new SqlConnection(cs);
    await conn.OpenAsync();

    await using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@lat", lat);
    cmd.Parameters.AddWithValue("@lon", lon);
    cmd.Parameters.AddWithValue("@radiusMeters", radiusMeters);
    cmd.Parameters.AddWithValue("@type", (object?)type ?? DBNull.Value);

    await using var r = await cmd.ExecuteReaderAsync();
    var items = new List<object>();
    while (await r.ReadAsync())
    {
        items.Add(new
        {
            FeatureType = r.GetString(0),
            Name = r.IsDBNull(1) ? null : r.GetString(1),
            DistanceMeters = r.GetDouble(2)
        });
    }

    return Results.Ok(items);
});

// GET /nearest?lat=39.1007&lon=-94.5772&type=bunker
app.MapGet("/nearest", async (double lat, double lon, string? type) =>
{
    var sql = @"
USE CourseMap;
DECLARE @p geography = geography::Point(@lat, @lon, 4326);

SELECT TOP (1) FeatureType, Name, Geog.STDistance(@p) AS DistanceMeters
FROM dbo.CourseFeatures
WHERE (@type IS NULL OR FeatureType = @type)
ORDER BY DistanceMeters ASC;";

    await using var conn = new SqlConnection(cs);
    await conn.OpenAsync();

    await using var cmd = new SqlCommand(sql, conn);
    cmd.Parameters.AddWithValue("@lat", lat);
    cmd.Parameters.AddWithValue("@lon", lon);
    cmd.Parameters.AddWithValue("@type", (object?)type ?? DBNull.Value);

    await using var r = await cmd.ExecuteReaderAsync();
    if (!await r.ReadAsync()) return Results.NotFound();

    return Results.Ok(new
    {
        FeatureType = r.GetString(0),
        Name = r.IsDBNull(1) ? null : r.GetString(1),
        DistanceMeters = r.GetDouble(2)
    });
});

app.Run();

