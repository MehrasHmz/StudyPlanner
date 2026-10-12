namespace StudyPlanner.Application.Commands.Sessions;

public class InvalidSessionCompletionException : InvalidOperationException
{
    public InvalidSessionCompletionException(string message) : base(message) { }
}
