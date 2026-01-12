USE CourseMap;
GO

DECLARE @boundaryWkt NVARCHAR(MAX) =
'POLYGON((
  -94.5780 39.1000,
  -94.5760 39.1000,
  -94.5760 39.1015,
  -94.5780 39.1015,
  -94.5780 39.1000
))';

DECLARE @bunkerWkt NVARCHAR(MAX) =
'POLYGON((
  -94.5774 39.1006,
  -94.5770 39.1006,
  -94.5770 39.1009,
  -94.5774 39.1009,
  -94.5774 39.1006
))';

INSERT INTO dbo.CourseFeatures (CourseId, FeatureType, Name, Geog, PropertiesJson)
VALUES
('demo-course', 'boundary', 'Demo Boundary', geography::STGeomFromText(@boundaryWkt, 4326), N'{}'),
('demo-course', 'bunker',   'Demo Bunker',   geography::STGeomFromText(@bunkerWkt,   4326), N'{}');
GO
