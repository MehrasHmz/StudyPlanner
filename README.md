# StudyPlanner - Enterprise Study Management Application

A production-ready ASP.NET Core MVC + Web API application for managing learning activities, built with Onion Architecture and .NET 8.

## Features

### MVC User Interface (Bootstrap 5)
- **Dashboard** - Key statistics, recent items, quick actions
- **Study Items** - Full CRUD with search, sort, pagination, filtering
- **Categories** - Category management with card-based layout
- **Session Planner** - Spaced Repetition algorithm for intelligent session planning
- **Authentication** - Login and registration views

### REST API
- Swagger/OpenAPI documentation
- JSON endpoints for programmatic access

### Core Architecture
- **Onion Architecture** with strict dependency inversion
- **CQRS** pattern using MediatR
- **FluentValidation** pipeline behavior
- **Entity Framework Core** with PostgreSQL
- **Serilog** structured logging

## Architecture

```
StudyPlanner.Domain        → Entities, Enums, Interfaces, Domain Services
StudyPlanner.Application   → DTOs, Commands/Queries, Validators, Mappings
StudyPlanner.Infrastructure → EF Core, Repositories, Seed Data
StudyPlanner.Web           → MVC Controllers, Razor Views, API Controllers
StudyPlanner.UnitTests     → Domain logic and algorithm tests
```

## Quick Start

### Option 1: Docker (Recommended)
```bash
docker-compose up -d
# Open http://localhost:8080
```

### Option 2: Local Development
```bash
# 1. Start PostgreSQL (using Docker or local)
docker run -d --name studyplanner-db -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=StudyPlanner -p 5432:5432 postgres:16-alpine

# 2. Run the application
dotnet run --project src/StudyPlanner.Web

# 3. Open http://localhost:5000
```

## Seed Data

The application includes seed data:
- **Demo User**: demo@studyplanner.com / password123
- **Categories**: Vocabulary, Programming, Algorithms
- **Study Items**: 4 sample items across different types

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/` | Dashboard |
| GET | `/StudyItems` | List all study items |
| GET | `/StudyItems/Create` | Create form |
| GET | `/Categories` | List categories |
| GET | `/Sessions` | Session planner |
| GET | `/Auth/Login` | Login page |
| GET | `/Auth/Register` | Registration page |
| GET | `/swagger` | API documentation |
| GET | `/api/categories/list` | Categories JSON API |

## Session Planning Algorithm

The Session Planner uses a **Spaced Repetition + Greedy** algorithm:
1. Scores items based on: priority, difficulty weight, and days overdue
2. Sorts by score descending
3. Greedily selects items that fit within available time
4. Ensures no items exceed the time budget

## Running Tests

```bash
dotnet test
```

## Docker Commands

```bash
make docker-up      # Start services
make docker-down    # Stop services
make build          # Build solution
make run            # Run locally
make test           # Run tests
make clean          # Clean artifacts
```

## Technology Stack

- .NET 8
- ASP.NET Core MVC
- Entity Framework Core 8
- PostgreSQL 16
- Bootstrap 5.3
- MediatR (CQRS)
- FluentValidation
- Serilog
- xUnit

## License

MIT License
