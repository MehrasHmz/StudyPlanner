using System.ComponentModel.DataAnnotations;

namespace StudyPlanner.Web.Models;

public sealed class PlanSessionViewModel
{
    [Range(1, 480, ErrorMessage = "Choose between 1 and 480 minutes.")]
    [Display(Name = "Available minutes")]
    public int AvailableMinutes { get; set; }
}
