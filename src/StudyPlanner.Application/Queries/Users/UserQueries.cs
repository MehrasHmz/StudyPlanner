using MediatR;
using StudyPlanner.Application.DTOs;

namespace StudyPlanner.Application.Queries.Users;
public record GetDashboardQuery(Guid UserId) : IRequest<DashboardDto>;
