using FluentValidation;
using StudyPlanner.Application.Commands.Users;
using StudyPlanner.Application.Commands.StudyItems;
using StudyPlanner.Application.Commands.Categories;
using StudyPlanner.Application.Commands.Sessions;
using StudyPlanner.Domain.Services;

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
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Difficulty).IsInEnum();
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
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Difficulty).IsInEnum();
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
public class UpdateCategoryValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
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
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Results).NotEmpty();
        RuleFor(x => x.Results)
            .Must(results => results == null || results.Where(r => r != null).Select(r => r.StudyItemId).Distinct().Count() == results.Count)
            .WithMessage("Duplicate or invalid session results.");
        RuleForEach(x => x.Results).NotNull().ChildRules(result =>
        {
            result.RuleFor(x => x.StudyItemId).NotEmpty();
            result.RuleFor(x => x.MinutesSpent).InclusiveBetween(1, StudyProgressPolicy.MaximumMinutes)
                .WithMessage("Enter actual minutes between 1 and 480.");
            result.RuleFor(x => x.Score).Must(StudyProgressPolicy.IsValidScore)
                .WithMessage("Score must be between 0 and 1.");
            result.RuleFor(x => x.Notes).MaximumLength(StudyProgressPolicy.MaximumNotesLength);
        });
    }
}
