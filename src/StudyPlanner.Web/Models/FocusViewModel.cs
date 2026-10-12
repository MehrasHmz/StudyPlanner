using StudyPlanner.Application.DTOs;

namespace StudyPlanner.Web.Models;

public sealed record FocusViewModel(IReadOnlyList<StudyItemDto> Items, string? SelectedTask, Guid? SelectedItemId);
