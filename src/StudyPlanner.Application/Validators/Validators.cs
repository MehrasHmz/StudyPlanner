using FluentValidation;
using StudyPlanner.Application.Commands.Users;
using StudyPlanner.Application.Commands.StudyItems;
using StudyPlanner.Application.Commands.Categories;
using StudyPlanner.Application.Commands.Sessions;

namespace StudyPlanner.Application.Validators;
public class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6).MaximumLength(100);
    }
}
public class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
public class CreateStudyItemValidator : AbstractValidator<CreateStudyItemCommand>
{
    public CreateStudyItemValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.EstimatedDurationMinutes).GreaterThan(0);
        RuleFor(x => x.Priority).InclusiveBetween(1, 5);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}
public class UpdateStudyItemValidator : AbstractValidator<UpdateStudyItemCommand>
{
    public UpdateStudyItemValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EstimatedDurationMinutes).GreaterThan(0);
        RuleFor(x => x.Priority).InclusiveBetween(1, 5);
        RuleFor(x => x.CategoryId).NotEmpty();
    }
}
public class CreateCategoryValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
public class PlanSessionValidator : AbstractValidator<PlanSessionCommand>
{
    public PlanSessionValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.AvailableMinutes).GreaterThan(0).LessThanOrEqualTo(480);
    }
}
public class CompleteSessionValidator : AbstractValidator<CompleteSessionCommand>
{
    public CompleteSessionValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
    }
}
