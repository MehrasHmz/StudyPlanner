using MediatR;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Interfaces;
using StudyPlanner.Application.Authentication;

namespace StudyPlanner.Application.Commands.Users;
public class RegisterHandler : IRequestHandler<RegisterCommand, AuthDto>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHashService _passwords;
    public RegisterHandler(IUserRepository users, IPasswordHashService passwords)
    { _users = users; _passwords = passwords; }
    public async Task<AuthDto> Handle(RegisterCommand r, CancellationToken ct)
    {
        if (await _users.ExistsByEmailAsync(r.Email, ct))
            throw new InvalidOperationException("Email already registered.");
        var user = new User(r.FullName, r.Email, string.Empty);
        user.SetPasswordHash(_passwords.HashPassword(user, r.Password));
        await _users.AddAsync(user, ct);
        await _users.SaveChangesAsync(ct);
        return new AuthDto("token-" + user.Id, new UserDto(user.Id, user.FullName, user.Email, user.CreatedAt));
    }
}
public class LoginHandler : IRequestHandler<LoginCommand, AuthDto>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHashService _passwords;
    public LoginHandler(IUserRepository users, IPasswordHashService passwords)
    { _users = users; _passwords = passwords; }
    public async Task<AuthDto> Handle(LoginCommand r, CancellationToken ct)
    {
        var user = await _users.GetByEmailAsync(r.Email, ct)
            ?? throw new UnauthorizedAccessException("Invalid credentials.");
        if (!user.IsActive) throw new UnauthorizedAccessException("Invalid credentials.");
        var verification = _passwords.VerifyPassword(user, r.Password);
        if (verification == PasswordVerificationOutcome.Failed)
            throw new UnauthorizedAccessException("Invalid credentials.");
        if (verification == PasswordVerificationOutcome.SuccessRehashNeeded)
        {
            user.SetPasswordHash(_passwords.HashPassword(user, r.Password));
            user.Touch();
            _users.Update(user);
            await _users.SaveChangesAsync(ct);
        }
        return new AuthDto("token-" + user.Id, new UserDto(user.Id, user.FullName, user.Email, user.CreatedAt));
    }
}
