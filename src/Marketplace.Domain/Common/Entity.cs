namespace Marketplace.Domain.Common;

public abstract class Entity : IEquatable<Entity>
{
    protected Entity()
        : this(Guid.CreateVersion7())
    {
    }

    protected Entity(Guid id) => Id = id;

    public Guid Id { get; private init; }

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);

    public bool Equals(Entity? other) =>
        other is not null && (ReferenceEquals(this, other) || (other.GetType() == GetType() && other.Id == Id));

    public override bool Equals(object? obj) => obj is Entity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
