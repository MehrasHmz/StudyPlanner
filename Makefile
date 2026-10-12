.PHONY: help build run test docker-up docker-down docker-logs docker-check clean
help:
	@echo "make build        Build solution"
	@echo "make test         Run .NET tests (set STUDYPLANNER_TEST_POSTGRES for real PostgreSQL)"
	@echo "make docker-up    Build and start the local Compose stack"
	@echo "make docker-down  Stop the stack without deleting volumes"
	@echo "make docker-logs  Follow application logs"
	@echo "make docker-check Validate Compose configuration"
build:
	dotnet build StudyPlanner.sln
run:
	dotnet run --project src/StudyPlanner.Web
test:
	dotnet test StudyPlanner.sln
docker-up:
	docker compose up --build -d --wait
docker-down:
	docker compose down
docker-logs:
	docker compose logs -f api
docker-check:
	docker compose config --quiet
clean:
	dotnet clean StudyPlanner.sln
