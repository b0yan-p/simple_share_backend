using simpleShare_backend.Common.Exceptions;
using simpleShare_backend.Common.Groups;
using simpleShare_backend.Models.Enums;
using Xunit;

namespace SimpleShare.UnitTests;

public class GroupCoverCatalogTests
{
    [Theory]
    [InlineData(GroupType.Trip, "alicante-sunset", "tokyo-sunset")]
    [InlineData(GroupType.Roommates, "roommate-1", "roommate-5")]
    [InlineData(GroupType.Couple, "couple-1", "couple-5")]
    [InlineData(GroupType.Other, "other-1", "other-5")]
    public void CatalogProvidesTheDefaultAndAcceptsKeysFromTheType(GroupType type, string first, string last)
    {
        Assert.Equal(first, GroupCoverCatalog.DefaultFor(type));
        Assert.Equal(first, GroupCoverCatalog.Validate(type, first));
        Assert.Equal(last, GroupCoverCatalog.Validate(type, last));
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("couple-1")]
    [InlineData("alicante-sunset.png")]
    [InlineData("ALICANTE-SUNSET")]
    public void CatalogRejectsInvalidTripKeys(string key)
    {
        Assert.Throws<ValidationAppException>(() => GroupCoverCatalog.Validate(GroupType.Trip, key));
    }
}
