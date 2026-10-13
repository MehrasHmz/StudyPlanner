using StudyPlanner.Application.Commands.Sessions;
using StudyPlanner.Application.DTOs;

namespace StudyPlanner.Web.Models;

public sealed class SessionCompletionViewModel
{
    public StudySessionDto Session { get; init; } = null!;
    public Guid SessionId => Session.Id;
    public List<SessionResultInput> Results { get; init; } = new();

    public static SessionCompletionViewModel Create(StudySessionDto session, List<ItemResultDto>? submitted = null) => new()
    {
        Session = session,
        Results = session.Items.OrderBy(i => i.Order).Select(i =>
        {
            var value = submitted?.FirstOrDefault(r => r != null && r.StudyItemId == i.StudyItemId);
            return new SessionResultInput { StudyItemId = i.StudyItemId, MinutesSpent = value?.MinutesSpent,
                Score = value?.Score, Notes = value?.Notes, Include = submitted == null || value != null };
        }).ToList()
    };
}

public sealed class SessionResultInput
{
    public Guid StudyItemId { get; init; }
    public int? MinutesSpent { get; init; }
    public double? Score { get; init; }
    public string? Notes { get; init; }
    public bool Include { get; init; }
}
