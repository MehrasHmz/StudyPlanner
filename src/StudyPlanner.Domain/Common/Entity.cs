namespace StudyPlanner.Domain.Common;
public abstract class Entity
{
    public Guid Id { get; protected set; }
    protected Entity() => Id = Guid.NewGuid();
    protected Entity(Guid id) => Id = id;
    public override bool Equals(object? obj) => obj is Entity e && Id == e.Id;
    public override int GetHashCode() => Id.GetHashCode();
}
public abstract class AuditableEntity : Entity
{
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    protected AuditableEntity() { CreatedAt = DateTime.UtcNow; }
    protected AuditableEntity(Guid id) : base(id) { CreatedAt = DateTime.UtcNow; }
    public void Touch() => UpdatedAt = DateTime.UtcNow;
}
