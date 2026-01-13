SHELL := /bin/bash
DOCKER ?= sudo docker
COMPOSE ?= sudo docker compose

# Load .env if present
-include .env
export

# ---- Config ----
SQL_HOST ?= localhost
SQL_PORT ?= 1433
SQL_DB   ?= CourseMap
SQL_USER ?= sa

# Required in .env
SA_PASSWORD ?= $(error SA_PASSWORD is not set. Copy .env.example to .env and set SA_PASSWORD.)

# API port (your earlier run used 5087)
COURSEMAP_PORT ?= 5087

# Shared connection string used by BOTH Importer and API
export COURSEMAP_CONNECTION_STRING := Server=$(SQL_HOST),$(SQL_PORT);Database=$(SQL_DB);User Id=$(SQL_USER);Password=$(SA_PASSWORD);Encrypt=True;TrustServerCertificate=True;

# Convenience: sqlcmd invocation (ODBC 18 requires -C for local container cert)
SQLCMD := sqlcmd -S $(SQL_HOST),$(SQL_PORT) -U $(SQL_USER) -P "$(SA_PASSWORD)" -C

.PHONY: help demo up down logs wait-sql init native build import validate api curl-check clean

help:
	@echo "Targets:"
	@echo "  make demo       - Full demo: up -> wait -> init -> native -> validate -> api (prints curl commands)"
	@echo "  make up         - Start SQL Server container"
	@echo "  make down       - Stop containers"
	@echo "  make logs       - Tail SQL Server logs"
	@echo "  make wait-sql   - Wait until SQL Server is accepting connections"
	@echo "  make init       - Create DB + run schema + spatial index"
	@echo "  make native     - Build C++ native library (liblinesimplify.so)"
	@echo "  make build      - Build .NET solution"
	@echo "  make import     - Import demo GeoJSON (no validation)"
	@echo "  make validate   - Import + validate (writes artifacts/validation_report.json)"
	@echo "  make api        - Run API on http://localhost:$(COURSEMAP_PORT)"
	@echo "  make clean      - Remove artifacts"

up:
	$(COMPOSE) up -d

down:
	$(COMPOSE) up -d down

logs:
	$(DOCKER) logs -f coursemap-sql

wait-sql:
	@echo "Waiting for SQL Server on $(SQL_HOST):$(SQL_PORT)..."
	@until $(SQLCMD) -Q "SELECT 1" >/dev/null 2>&1; do \
		echo "  ...not ready yet"; \
		sleep 2; \
	done
	@echo "SQL Server is ready."

init: wait-sql
	@echo "Creating database (if needed) + applying schema..."
	@$(SQLCMD) -Q "IF DB_ID('$(SQL_DB)') IS NULL CREATE DATABASE $(SQL_DB);"
	@$(SQLCMD) -i sql/schema.sql
	@$(SQLCMD) -i sql/spatial_index.sql
	@echo "Schema applied."

native:
	@echo "Building native C++ library..."
	@if [ ! -d native/linesimplify/build ]; then \
		cmake -S native/linesimplify -B native/linesimplify/build -G Ninja; \
	fi
	cmake --build native/linesimplify/build
	@ls -l native/linesimplify/build/liblinesimplify.so >/dev/null

build: native
	dotnet build

import: build
	DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet run --project src/CourseMap.Importer -- \
	  --file data/demo.geojson --course demo-course --replace true --validate false

validate: build
	mkdir -p artifacts
	DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet run --project src/CourseMap.Importer -- \
	  --file data/demo.geojson --course demo-course --replace true --validate true
	@echo "Wrote artifacts/validation_report.json"

api:
	@echo "Starting API on http://localhost:$(COURSEMAP_PORT)"
	@echo "Try:"
	@echo "  make curl-check"
	DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet run --project src/CourseMap.Api -- \
	  --urls "http://localhost:$(COURSEMAP_PORT)"

curl-check:
	curl "http://localhost:$(COURSEMAP_PORT)/contains?lat=39.1007&lon=-94.5772"
	@echo
	curl "http://localhost:$(COURSEMAP_PORT)/nearest?lat=39.1007&lon=-94.5772&type=bunker"
	@echo
	curl "http://localhost:$(COURSEMAP_PORT)/within?lat=39.1007&lon=-94.5772&radiusMeters=500"
	@echo

demo: up init validate
	@echo ""
	@echo "✅ Demo is ready."
	@echo "Next, in another terminal run:"
	@echo "  make api"
	@echo ""
	@echo "Then run:"
	@echo "  make curl-check"
	@echo ""

clean:
	rm -rf artifacts
