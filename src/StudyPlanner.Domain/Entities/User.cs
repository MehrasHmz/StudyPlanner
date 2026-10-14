using StudyPlanner.Domain.Common;
using StudyPlanner.Domain.Enums;

namespace StudyPlanner.Domain.Entities;
public class User : AuditableEntity
{
    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public UserRole Role { get; private set; } = UserRole.Regular;

    public void SetRole(UserRole role)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        Role = role;
    }

    private readonly List<StudyItem> _studyItems = new();
    public IReadOnlyCollection<StudyItem> StudyItems => _studyItems.AsReadOnly();
    private readonly List<StudySession> _sessions = new();
    public IReadOnlyCollection<StudySession> Sessions => _sessions.AsReadOnly();
    private readonly List<ProgressRecord> _progressRecords = new();
    public IReadOnlyCollection<ProgressRecord> ProgressRecords => _progressRecords.AsReadOnly();

    private User() : base() { }
    public User(string fullName, string email, string passwordHash) : base()
    {
        SetFullName(fullName);
        SetEmail(email);
        PasswordHash = passwordHash;
    }
    public User(Guid id, string fullName, string email, string passwordHash) : base(id)
    {
        SetFullName(fullName);
        SetEmail(email);
        PasswordHash = passwordHash;
    }
    public void SetFullName(string n)
    {
        if (string.IsNullOrWhiteSpace(n)) throw new ArgumentException("Full name is required.");
        FullName = n.Trim();
    }
    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("Password hash is required.");
        PasswordHash = passwordHash;
    }
    public void SetEmail(string e)
    {
        if (string.IsNullOrWhiteSpace(e) || !e.Contains('@')) throw new ArgumentException("Valid email is required.");
        Email = e.Trim().ToLowerInvariant();
    }
}
