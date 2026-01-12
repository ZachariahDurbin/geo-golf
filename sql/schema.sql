USE CourseMap;
GO

IF OBJECT_ID('dbo.CourseFeatures', 'U') IS NOT NULL DROP TABLE dbo.CourseFeatures;
GO

CREATE TABLE dbo.CourseFeatures
(
    Id            UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    CourseId      NVARCHAR(64)     NOT NULL,
    FeatureType   NVARCHAR(32)     NOT NULL, -- green, bunker, water, fairway, path, boundary, etc.
    Name          NVARCHAR(128)    NULL,
    PropertiesJson NVARCHAR(MAX)   NULL,

    -- GPS lat/lon on Earth (SRID 4326)
    Geog          GEOGRAPHY        NOT NULL,

    CreatedAtUtc  DATETIME2(3)     NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

-- Helpful non-spatial indexes
CREATE INDEX IX_CourseFeatures_CourseId ON dbo.CourseFeatures(CourseId);
CREATE INDEX IX_CourseFeatures_Type ON dbo.CourseFeatures(FeatureType);
GO

-- Spatial index (helps within/nearest/contains)
CREATE SPATIAL INDEX SIX_CourseFeatures_Geog ON dbo.CourseFeatures(Geog);
GO
