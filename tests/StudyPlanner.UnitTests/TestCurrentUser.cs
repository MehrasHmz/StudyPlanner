using StudyPlanner.Application.Authorization;
namespace StudyPlanner.UnitTests;
internal sealed record TestCurrentUser(Guid? UserId) : ICurrentUser;
