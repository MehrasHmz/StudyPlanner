namespace StudyPlanner.Application.Commands.Sessions;

public sealed class SessionCompletionConflictException : InvalidOperationException
{
    public SessionCompletionConflictException(Exception innerException)
        : base("The session changed while completion was being saved. Reload the session.", innerException) { }
}
