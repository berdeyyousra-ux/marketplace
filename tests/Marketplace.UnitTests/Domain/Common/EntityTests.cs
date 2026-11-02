using Marketplace.Domain.Common;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Domain.Common;

public sealed class EntityTests
{
    [Fact]
    public void Constructor_NoId_AssignsVersion7Guid()
    {
        var entity = new SampleEntity();

        entity.Id.Version.ShouldBe(7);
    }

    [Fact]
    public void Equals_SameTypeAndId_AreEqual()
    {
        var id = Guid.CreateVersion7();

        var left = new SampleEntity(id);
        var right = new SampleEntity(id);

        left.Equals(right).ShouldBeTrue();
        (left == right).ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentIds_AreNotEqual()
    {
        var left = new SampleEntity();
        var right = new SampleEntity();

        (left != right).ShouldBeTrue();
    }

    [Fact]
    public void Equals_DifferentTypesWithSameId_AreNotEqual()
    {
        var id = Guid.CreateVersion7();

        Entity left = new SampleEntity(id);
        Entity right = new OtherEntity(id);

        left.Equals(right).ShouldBeFalse();
    }

    [Fact]
    public void Equals_Null_IsFalse()
    {
        var entity = new SampleEntity();

        entity.Equals(null).ShouldBeFalse();
        (entity == null).ShouldBeFalse();
    }

    private sealed class SampleEntity : AggregateRoot
    {
        public SampleEntity()
        {
        }

        public SampleEntity(Guid id)
            : base(id)
        {
        }
    }

    private sealed class OtherEntity(Guid id) : Entity(id);
}
