using MediatR;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Domain.Entities;
using StudyPlanner.Domain.Interfaces;

namespace StudyPlanner.Application.Commands.Users;
public class RegisterHandler : IRequestHandler<RegisterCommand, AuthDto>
{
    private readonly IUserRepository _users;
    public RegisterHandler(IUserRepository users) => _users = users;
    public async Task<AuthDto> Handle(RegisterCommand r, CancellationToken ct)
    {
        if (await _users.ExistsByEmailAsync(r.Email, ct))
            throw new InvalidOperationException("Email already registered.");
        var hash = BCryptHelper(r.Password);
        var user = new User(r.FullName, r.Email, hash);
        await _users.AddAsync(user, ct);
        await _users.SaveChangesAsync(ct);
        return new AuthDto("token-" + user.Id, new UserDto(user.Id, user.FullName, user.Email, user.CreatedAt));
    }
    private static string BCryptHelper(string password)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password + "StudyPlannerSalt2024"));
        return Convert.ToBase64String(bytes);
    }
}
public class LoginHandler : IRequestHandler<LoginCommand, AuthDto>
{
    private readonly IUserRepository _users;
    public LoginHandler(IUserRepository users) => _users = users;
    public async Task<AuthDto> Handle(LoginCommand r, CancellationToken ct)
    {
        var user = await _users.GetByEmailAsync(r.Email, ct)
            ?? throw new UnauthorizedAccessException("Invalid credentials.");
        var hash = BCryptHelper(r.Password);
        if (user.PasswordHash != hash) throw new UnauthorizedAccessException("Invalid credentials.");
        return new AuthDto("token-" + user.Id, new UserDto(user.Id, user.FullName, user.Email, user.CreatedAt));
    }
    private static string BCryptHelper(string password)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password + "StudyPlannerSalt2024"));
        return Convert.ToBase64String(bytes);
    }
}
