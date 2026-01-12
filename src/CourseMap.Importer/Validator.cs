using Microsoft.Data.SqlClient;

namespace CourseMap.Importer;

public static class Validator
{
    public static async Task<ValidationReport> ValidateAsync(SqlConnection conn, string courseId)
    {
        // Pull boundary (must be exactly one)
        const string boundarySql = @"
            USE CourseMap;
            SELECT FeatureType, Name, Geog.STAsText() AS Wkt
            FROM dbo.CourseFeatures
            WHERE CourseId = @courseId AND FeatureType = 'boundary';";

        var boundaries = new List<(string type, string? name, string wkt)>();

        await using (var cmd = new SqlCommand(boundarySql, conn))
        {
            cmd.Parameters.AddWithValue("@courseId", courseId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                boundaries.Add((r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2)));
        }

        var violations = new List<Violation>();

        if (boundaries.Count != 1)
        {
            violations.Add(new Violation(
                Code: "BOUNDARY_COUNT",
                FeatureType: "boundary",
                Name: boundaries.FirstOrDefault().name,
                Message: $"Expected exactly 1 boundary; found {boundaries.Count}."
            ));
        }

        // Validate all features are inside boundary (if boundary exists)
        // We'll use STContains for strict containment; you can loosen to STIntersects if you want.
        const string insideSql = @"
USE CourseMap;
DECLARE @boundary geography =
(
  SELECT TOP (1) Geog
  FROM dbo.CourseFeatures
  WHERE CourseId = @courseId AND FeatureType = 'boundary'
);

SELECT FeatureType, Name
FROM dbo.CourseFeatures
WHERE CourseId = @courseId
  AND FeatureType <> 'boundary'
  AND @boundary IS NOT NULL
  AND @boundary.STContains(Geog) = 0;";

        await using (var cmd = new SqlCommand(insideSql, conn))
        {
            cmd.Parameters.AddWithValue("@courseId", courseId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                violations.Add(new Violation(
                    Code: "OUTSIDE_BOUNDARY",
                    FeatureType: r.GetString(0),
                    Name: r.IsDBNull(1) ? null : r.GetString(1),
                    Message: "Feature is not fully contained by boundary."
                ));
            }
        }

        // Total feature count
        const string countSql = @"USE CourseMap; SELECT COUNT(*) FROM dbo.CourseFeatures WHERE CourseId = @courseId;";
        int total;
        await using (var cmd = new SqlCommand(countSql, conn))
        {
            cmd.Parameters.AddWithValue("@courseId", courseId);
            var scalar = await cmd.ExecuteScalarAsync();
            total = Convert.ToInt32(scalar);
        }

        return new ValidationReport(
            CourseId: courseId,
            CreatedAtUtc: DateTime.UtcNow,
            TotalFeatures: total,
            BoundaryCount: boundaries.Count,
            ViolationsCount: violations.Count,
            Violations: violations
        );
    }
}
