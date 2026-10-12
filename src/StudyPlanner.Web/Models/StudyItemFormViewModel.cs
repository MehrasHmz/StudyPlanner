using StudyPlanner.Application.DTOs;
using StudyPlanner.Domain.Enums;

namespace StudyPlanner.Web.Models;

public record StudyItemFormViewModel(string Title, string? Description, StudyItemType Type, DifficultyLevel Difficulty,
    int EstimatedDurationMinutes, int Priority, Guid CategoryId, DateTime? NextReviewDate,
    IReadOnlyList<CategoryDto> Categories);
