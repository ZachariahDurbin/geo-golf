# CourseMap Pipeline (SQL Server Spatial + .NET + C++)

I was concerned my resume wouldn’t clearly demonstrate GIS / mapping experience, so I built a small end-to-end “map tooling” project that mirrors the shape of real mapping systems:

- **Ingest** GeoJSON features (boundary, hazards, paths, points)
- Store them in **SQL Server** using the **`geography`** type (SRID 4326) + spatial indexing
- Run a **validation gate** that produces a CI-friendly report (`validation_report.json`)
- Expose core spatial queries via a **.NET Web API**
- Use a native **C++** shared library (via P/Invoke) for geometry processing (line simplification)

This repo is intentionally “tooling-shaped” (import → validate → query) rather than a UI project.

---

## Architecture

GeoJSON (data/demo.geojson)
|
v
CourseMap.Importer (.NET)

GeoJSON -> WKT -> SQL geography

calls C++ native lib for LineString simplification

writes artifacts/validation_report.json
|
v
SQL Server (Docker) <-- geography SRID 4326 + spatial index
|
v
CourseMap.Api (.NET)

/nearest /within /contains


---

## Tech stack

- **.NET 8** (Importer + ASP.NET Core API)
- **SQL Server 2022** (Docker) with spatial `geography`
- **C++17** native library built with **CMake/Ninja** (`liblinesimplify.so`)
- Linux-friendly dev flow (tested on Debian)

---

## What this demonstrates (skills mapping)

**GIS / mapping fundamentals**
- Correct handling of lat/lon storage (SRID 4326) in SQL Server `geography`
- Core spatial queries: point-in-polygon (`STContains`) + proximity (`STDistance`)
- Spatial index usage (to avoid full scans on distance/within queries)

**Tooling & systems engineering**
- Deterministic import pipeline
- Validation gate with artifact + non-zero exit code (CI-friendly)
- Clear separation of concerns: import vs validate vs query

**C++ + .NET interoperability**
- C++ shared library built via CMake
- .NET P/Invoke into native code
- Native geometry processing used in the import pipeline (not just a standalone demo)

---

## Quickstart (5 minutes)

### Prereqs
- Docker + Docker Compose
- .NET 8 SDK
- (Optional) `sqlcmd` if you want to run SQL scripts directly

### 1) Configure env
```bash
cp .env.example .env
# edit .env and set SA_PASSWORD to a strong value

2) Start SQL Server
make up

3) Initialize DB schema
make init

4) Import demo dataset + validate
make validate


This writes:

artifacts/validation_report.json

5) Run API
make api

Demo: spatial queries

Assuming API is running on COURSEMAP_PORT (default 5087):

curl "http://localhost:5087/contains?lat=39.1007&lon=-94.5772"
curl "http://localhost:5087/nearest?lat=39.1007&lon=-94.5772&type=bunker"
curl "http://localhost:5087/within?lat=39.1007&lon=-94.5772&radiusMeters=500"

Demo: validation as a “pipeline gate”

A successful run produces artifacts/validation_report.json and exits 0:

make validate
echo $?


To see a failure:

Edit data/demo.geojson so a feature lies outside the boundary

Re-run make validate

Observe a non-zero exit code and violations listed in validation_report.json

Demo: C++ line simplification in the import pipeline

The importer runs a native C++ function (via P/Invoke) to simplify LineString features before storing them.
You’ll see a log like:

Simplify LineString (eps=2m): 200 -> 65 points


You can confirm the stored geometry has fewer points:

sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -C -Q "
USE CourseMap;
SELECT FeatureType, Name, Geog.STNumPoints() AS NumPoints
FROM dbo.CourseFeatures
WHERE CourseId='demo-course' AND FeatureType='path';
"

Project layout
.
├── data/
│   └── demo.geojson
├── sql/
│   ├── schema.sql
│   └── spatial_index.sql
├── native/
│   └── linesimplify/
│       ├── CMakeLists.txt
│       └── linesimplify.cpp
├── src/
│   ├── CourseMap.Api/
│   └── CourseMap.Importer/
├── docker-compose.yml
├── Makefile
└── README.md

Notes / decisions

SQL Server is run via Docker for repeatability.

ODBC Driver 18 encrypts by default; local dev uses TrustServerCertificate=True.

GeoJSON coordinates are [lon, lat]; SQL Server geography::Point uses (lat, lon, SRID) — the code normalizes consistently.