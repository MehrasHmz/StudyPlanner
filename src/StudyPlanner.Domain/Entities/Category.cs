using StudyPlanner.Domain.Common;

namespace StudyPlanner.Domain.Entities;
public class Category : Entity
{
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public bool IsDeleted { get; private set; }
    private readonly List<StudyItem> _studyItems = new();
    public IReadOnlyCollection<StudyItem> StudyItems => _studyItems.AsReadOnly();

    private Category() : base() { }
    public Category(string name, string description = "") : base()
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Category name is required.");
        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
    }
    public Category(Guid id, string name, string description = "") : base(id)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Category name is required.");
        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
    }
    public void SetName(string n) { if (string.IsNullOrWhiteSpace(n)) throw new ArgumentException("Name required."); Name = n.Trim(); }
    public void SetDescription(string d) => Description = d?.Trim() ?? string.Empty;
    public void SoftDelete() => IsDeleted = true;
    public void Restore() => IsDeleted = false;
}
