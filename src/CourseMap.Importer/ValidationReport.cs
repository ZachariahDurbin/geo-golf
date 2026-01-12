namespace CourseMap.Importer;

public sealed record ValidationReport(
    string CourseId,
    DateTime CreatedAtUtc,
    int TotalFeatures,
    int BoundaryCount,
    int ViolationsCount,
    IReadOnlyList<Violation> Violations
);

public sealed record Violation(
    string Code,
    string FeatureType,
    string? Name,
    string Message
);
