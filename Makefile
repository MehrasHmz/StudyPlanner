.PHONY: help build run test migrate clean docker-up docker-down
help:
	@echo "  make build          Build solution"
	@echo "  make run            Run the web app"
	@echo "  make test           Run all tests"
	@echo "  make docker-up      Start PostgreSQL + API"
	@echo "  make docker-down    Stop services"
	@echo "  make clean          Clean build artifacts"
build: dotnet build
run: dotnet run --project src/StudyPlanner.Web
test: dotnet test
docker-up: docker-compose up -d
docker-down: docker-compose down
clean: dotnet clean && rm -rf **/bin **/obj
