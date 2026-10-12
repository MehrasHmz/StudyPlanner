using StudyPlanner.Domain.Entities;

namespace StudyPlanner.Application.Authentication;

public enum PasswordVerificationOutcome { Failed, Success, SuccessRehashNeeded }

public interface IPasswordHashService
{
    string HashPassword(User user, string password);
    PasswordVerificationOutcome VerifyPassword(User user, string password);
}
