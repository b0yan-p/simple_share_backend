using System.Net;
using System.Net.Http.Json;
using Shouldly;
using simpleShare_backend.Contracts.Member;
using simpleShare_backend.Models.Enums;

namespace SimpleShare.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class GroupMemberTests(ApiFixture fixture)
{
    [Fact] // MEM-01
    public async Task AddMembers_WithExistingMember_ShowsThemAsActiveInTheGroup()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await fixture.RegisterAsync(ct, firstName: "Ann", lastName: "Owner");
        var group = await owner.CreateGroupAsync("Shared", ct);

        // A registered user has no Member row until they own a group, so the
        // friend has to exist as a member somewhere before being added here.
        var (friend, _) = await fixture.RegisterAsync(ct, firstName: "Bo", lastName: "Friend");
        var friendMemberId = await friend.CreateMemberIdentityAsync(ct);

        // Act
        var response = await owner.AddMemberAsync(group.Id, new { MemberId = friendMemberId }, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));

        var members = await owner.GetMembersAsync(group.Id, ct);
        members.ShouldContain(m => m.MemberId == friendMemberId);

        var added = members.Single(m => m.MemberId == friendMemberId);
        added.DisplayName.ShouldBe("Bo Friend");
        added.GroupMemberStatus.ShouldBe(GroupMemberStatus.Active);
        added.GroupRole.ShouldBe(GroupRole.Member);
        added.UserId.ShouldNotBeNull();
    }

    [Fact] // MEM-02
    public async Task AddMembers_WithDisplayNameOnly_CreatesVirtualMemberWithNoUser()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.RegisterAsync(ct);
        var group = await client.CreateGroupAsync("Ski trip", ct);

        // Act
        var response = await client.AddMemberAsync(group.Id, new { DisplayName = "Ghost" }, ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));

        var members = await client.GetMembersAsync(group.Id, ct);
        members.ShouldContain(m => m.DisplayName == "Ghost");

        var ghost = members.Single(m => m.DisplayName == "Ghost");
        ghost.UserId.ShouldBeNull("a virtual member must not be attached to a user account");
        ghost.GroupMemberStatus.ShouldBe(GroupMemberStatus.Active);
    }

    [Fact] // MEM-03
    public async Task AddMembers_WithInviteUserAndNoEmail_ReportsTheFailureWithoutFailingTheRequest()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.RegisterAsync(ct);
        var group = await client.CreateGroupAsync("Invites", ct);
        var owner = await client.MyMemberIdAsync(group.Id, ct);
        var member = new { DisplayName = "Invitee", InviteUser = true };   // no Email

        // Act
        var response = await client.AddMemberAsync(group.Id, member, ct);

        // Assert
        // Deliberately pinning partial-success semantics rather than the 4xx you
        // would expect: members/bulk catches each item's ValidationAppException
        // and reports it in ErrorMessages, and the mobile app depends on the 200.
        // The endpoint is due a redesign - when that lands this test should fail,
        // which is the point of it. See docs/api-test-cases.md.
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));

        var body = await response.Content.ReadFromJsonAsync<AddGroupMembersResponse>(ct);
        body.ShouldNotBeNull();
        body.Members.ShouldBeEmpty("the invalid item must not produce a member");
        body.ErrorMessages.ShouldHaveSingleItem().ShouldNotBeNullOrWhiteSpace();

        // The per-item transaction rolled back, so nothing reached the group.
        var members = await client.GetMembersAsync(group.Id, ct);
        members.ShouldHaveSingleItem().MemberId.ShouldBe(owner);
    }

    [Fact] // MEM-04
    public async Task RemoveMember_ByNonOwner_ReturnsForbidden()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await fixture.RegisterAsync(ct);
        var group = await owner.CreateGroupAsync("Shared", ct);

        var (member, _) = await fixture.RegisterAsync(ct);
        var memberId = await member.CreateMemberIdentityAsync(ct);
        await owner.AddExistingMemberAsync(group.Id, memberId, ct);

        var target = await owner.AddVirtualMemberAsync(group.Id, "Target", ct);

        // Act
        var response = await member.DeleteAsync(
            $"/api/GroupMember/{group.Id}/members/{target}", ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "not_authorized", ct);

        var members = await owner.GetMembersAsync(group.Id, ct);
        members.ShouldContain(m => m.MemberId == target);
    }

    [Fact] // MEM-05
    public async Task RemoveMember_ByOwner_DropsThemFromTheActiveList()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await fixture.RegisterAsync(ct);
        var group = await owner.CreateGroupAsync("Shrinking", ct);
        var me = await owner.MyMemberIdAsync(group.Id, ct);
        var leaving = await owner.AddVirtualMemberAsync(group.Id, "Leaving", ct);

        // Act
        var response = await owner.DeleteAsync(
            $"/api/GroupMember/{group.Id}/members/{leaving}", ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));

        var members = await owner.GetMembersAsync(group.Id, ct);
        members.ShouldNotContain(m => m.MemberId == leaving);
        members.ShouldHaveSingleItem().MemberId.ShouldBe(me);
    }
}
