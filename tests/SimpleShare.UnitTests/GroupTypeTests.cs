using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using simpleShare_backend.Common.Extensions;
using simpleShare_backend.Contracts.Group;
using simpleShare_backend.Domain;
using simpleShare_backend.Models.Enums;
using Xunit;

namespace SimpleShare.UnitTests;

public class GroupTypeTests
{
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void RequestContractsValidateNumericType(short value, bool expected)
    {
        var type = (GroupType)value;
        var create = new CreateGroupRequest { Name = "Flat", Currency = "EUR", Type = type };
        var update = new UpdateGroupRequest { Id = Guid.NewGuid(), Type = type };
        foreach (var request in new object[] { create, update })
        {
            var valid = Validator.TryValidateObject(request, new ValidationContext(request), [], true);
            Assert.Equal(expected, valid);
        }
    }

    [Fact]
    public void LegacyRequestsSupportDefaultCreationAndPartialUpdates()
    {
        var create = JsonSerializer.Deserialize<CreateGroupRequest>("""{"Name":"Flat","Currency":"EUR"}""");
        var update = JsonSerializer.Deserialize<UpdateGroupRequest>("""{"Id":"00000000-0000-0000-0000-000000000001"}""");
        Assert.Equal(GroupType.Other, create!.Type);
        Assert.Null(update!.Type);
        Assert.Equal(GroupType.Roommates, new Group { Type = GroupType.Roommates }.ToResponse().Type);
    }
}
