using MediatR;
using StudyPlanner.Application.DTOs;
using StudyPlanner.Domain.Enums;

namespace StudyPlanner.Application.Commands.Users;
public record RegisterCommand(string FullName, string Email, string Password) : IRequest<AuthDto>;
public record LoginCommand(string Email, string Password) : IRequest<AuthDto>;
