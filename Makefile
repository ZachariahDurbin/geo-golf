SHELL := /bin/bash

-include .env
export

PORT ?= $(COURSEMAP_PORT)

.PHONY: help up down logs init import validate api demo clean

help:
	@echo "Targets:"
	@echo "  make up        - start SQL Server container"
	@echo "  make down      - stop container"
	@echo "  make logs      - tail container logs"
	@echo "  make init      - create DB + schema"
	@echo "  make import    - import demo geojson"
	@echo "  make validate  - import + validate (writes artifacts/validation_report.json)"
	@echo "  make api       - run API on port $(PORT)"
	@echo "  make demo      - up + init + validate + start API"
	@echo "  make clean     - remove artifacts"

up:
	docker compose up -d

down:
	docker compose down

logs:
	docker logs -f coursemap-sql

# Runs schema from inside the container so the user doesn't need sqlcmd installed
init:
	@echo "Initializing DB + schema..."
	docker exec -i coursemap-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$$MSSQL_SA_PASSWORD" -C -Q "IF DB_ID('CourseMap') IS NULL CREATE DATABASE CourseMap;"
	docker exec -i coursemap-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$$MSSQL_SA_PASSWORD" -C -i /mnt/schema.sql || true
	sqlcmd -S localhost -U sa -P "$$SA_PASSWORD" -C -Q "IF DB_ID('CourseMap') IS NULL CREATE DATABASE CourseMap;"
	sqlcmd -S localhost -U sa -P "$$SA_PASSWORD" -C -i sql/schema.sql
	sqlcmd -S localhost -U sa -P "$$SA_PASSWORD" -C -i sql/spatial_index.sql
	@echo "If schema.sql isn't mounted yet, use the scripts/init target below."

# We’ll mount schema.sql via a bind mount with this helper:
# (keeps it simple, works without editing container image)
scripts-init:
	@echo "Running schema via bind mount..."
	docker exec -i coursemap-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$$MSSQL_SA_PASSWORD" -C -i /var/opt/mssql/schema.sql

import:
	dotnet build CourseMap.sln
	DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet run --project src/CourseMap.Importer -- \
	  --file data/demo.geojson --course demo-course --replace true --validate false

validate:
	dotnet build CourseMap.sln
	DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet run --project src/CourseMap.Importer -- \
	  --file data/demo.geojson --course demo-course --replace true --validate true

api:
	DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet run --project src/CourseMap.Api -- \
	  --urls "http://localhost:$(PORT)"

demo: up init validate
	@echo "Starting API on http://localhost:$(PORT)"
	@echo "Press Ctrl+C to stop the API."
	$(MAKE) api

clean:
	rm -rf artifacts
