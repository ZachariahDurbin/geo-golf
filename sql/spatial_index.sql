USE CourseMap;
GO

-- Required SET options for spatial index operations
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_CourseFeatures_Geog_Spatial'
      AND object_id = OBJECT_ID('dbo.CourseFeatures')
)
BEGIN
    CREATE SPATIAL INDEX IX_CourseFeatures_Geog_Spatial
    ON dbo.CourseFeatures(Geog)
    USING GEOGRAPHY_AUTO_GRID;
END
GO
