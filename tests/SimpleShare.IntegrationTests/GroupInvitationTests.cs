using System.Net;
using Shouldly;
using simpleShare_backend.Contracts.Invitation;
using simpleShare_backend.Models.Enums;

namespace SimpleShare.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class GroupInvitationTests(ApiFixture fixture)
{
    [Fact] // INV-01
    public async Task CreateGroup_MintsTheInviteLinkWithTheGroup()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await fixture.RegisterAsync(ct);

        // Act
        var group = await owner.CreateGroupAsync("Ski trip", ct);

        // Assert
        var invitation = await owner.GetInvitationAsync(group.Id, ct);
        invitation.GroupId.ShouldBe(group.Id);
        invitation.Token.Length.ShouldBe(22, "16 random bytes base64url-encoded");
        invitation.IsActive.ShouldBeTrue();
        invitation.UseCount.ShouldBe(0);

        // Reading it again is a read, not a mint: the same row comes back.
        var again = await owner.GetInvitationAsync(group.Id, ct);
        again.Id.ShouldBe(invitation.Id);
        again.Token.ShouldBe(invitation.Token);
    }

    [Fact] // INV-02
    public async Task GetInvitation_ByNonMember_IsForbidden()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await fixture.RegisterAsync(ct);
        var group = await owner.CreateGroupAsync("Private", ct);
        var (outsider, _) = await fixture.RegisterAsync(ct);

        // Act
        var response = await outsider.GetAsync($"/api/GroupInvitation/group/{group.Id}", ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.Forbidden, "not_authorized", ct);
    }

    [Fact] // INV-03
    public async Task GetPreview_WithoutAuthentication_ReturnsGroupNameAndMemberCount()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await fixture.RegisterAsync(ct);
        var group = await owner.CreateGroupAsync("Weekend trip", ct);
        await owner.AddVirtualMemberAsync(group.Id, "Ghost", ct);
        var invitation = await owner.GetInvitationAsync(group.Id, ct);

        // No bearer: this is the whole point of the endpoint, since an invited
        // person usually has no account when they open the link.
        var anonymous = fixture.CreateClient();

        // Act
        var response = await anonymous.GetInvitePreviewAsync(invitation.Token, ct);

        // Assert
        var preview = await response.ReadPreviewAsync(ct);
        preview.GroupId.ShouldBe(group.Id);
        preview.GroupName.ShouldBe("Weekend trip");
        preview.MemberCount.ShouldBe(2);
    }

    [Fact] // INV-04
    public async Task GetPreview_WithUnknownToken_ReturnsNotFound()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var anonymous = fixture.CreateClient();

        // Act
        var response = await anonymous.GetInvitePreviewAsync("nosuchtokenatall1234ab", ct);

        // Assert
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "invite_not_found", ct);
    }

    [Fact] // INV-05
    public async Task Join_WithValidToken_AddsTheCallerAndCountsTheUse()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await fixture.RegisterAsync(ct);
        var group = await owner.CreateGroupAsync("Shared", ct);
        var invitation = await owner.GetInvitationAsync(group.Id, ct);
        var (joiner, _) = await fixture.RegisterAsync(ct, firstName: "Bo", lastName: "Joiner");

        // Act
        var response = await joiner.JoinByTokenAsync(invitation.Token, ct);

        // Assert
        var body = await response.ReadJoinAsync(ct);
        body.GroupId.ShouldBe(group.Id);
        body.AlreadyMember.ShouldBeFalse();

        var members = await owner.GetMembersAsync(group.Id, ct);
        var joined = members.Where(m => m.MemberId == body.MemberId).ShouldHaveSingleItem();
        joined.DisplayName.ShouldBe("Bo Joiner");
        joined.GroupRole.ShouldBe(GroupRole.Member);
        joined.GroupMemberStatus.ShouldBe(GroupMemberStatus.Active);

        (await owner.GetInvitationAsync(group.Id, ct)).UseCount.ShouldBe(1);
    }

    [Fact] // INV-06
    public async Task Join_ByAnExistingMember_SucceedsWithoutCountingAnotherUse()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await fixture.RegisterAsync(ct);
        var group = await owner.CreateGroupAsync("Shared", ct);
        var invitation = await owner.GetInvitationAsync(group.Id, ct);
        var (joiner, _) = await fixture.RegisterAsync(ct);
        await joiner.JoinByTokenAsync(invitation.Token, ct);

        // Act
        var response = await joiner.JoinByTokenAsync(invitation.Token, ct);

        // Assert
        var body = await response.ReadJoinAsync(ct);
        body.AlreadyMember.ShouldBeTrue("a shared link routinely reaches someone already in");
        body.GroupId.ShouldBe(group.Id);

        var members = await owner.GetMembersAsync(group.Id, ct);
        members.Count(m => m.MemberId == body.MemberId).ShouldBe(1, "no duplicate membership row");

        (await owner.GetInvitationAsync(group.Id, ct)).UseCount.ShouldBe(1);
    }

    [Fact] // INV-07
    public async Task Join_AfterBeingRemoved_ReactivatesTheSameMembership()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await fixture.RegisterAsync(ct);
        var group = await owner.CreateGroupAsync("Shared", ct);
        var invitation = await owner.GetInvitationAsync(group.Id, ct);

        var (joiner, _) = await fixture.RegisterAsync(ct);
        var firstJoin = await (await joiner.JoinByTokenAsync(invitation.Token, ct)).ReadJoinAsync(
            ct
        );

        var removal = await owner.RemoveMemberAsync(group.Id, firstJoin.MemberId, ct);
        removal.StatusCode.ShouldBe(HttpStatusCode.OK, await removal.Content.ReadAsStringAsync(ct));

        // Act
        var response = await joiner.JoinByTokenAsync(invitation.Token, ct);

        // Assert
        var body = await response.ReadJoinAsync(ct);
        body.AlreadyMember.ShouldBeFalse();
        body.MemberId.ShouldBe(
            firstJoin.MemberId,
            "rejoining must revive the old row so expense history stays attached"
        );

        var members = await owner.GetMembersAsync(group.Id, ct);
        var rejoined = members.Where(m => m.MemberId == firstJoin.MemberId).ShouldHaveSingleItem();
        rejoined.GroupMemberStatus.ShouldBe(GroupMemberStatus.Active);
    }

    [Fact] // INV-08 (cross-tenant, rule 4)
    public async Task GetInvitation_ForAnotherTenantsGroup_NeverLeaksTheToken()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await fixture.RegisterAsync(ct);
        var group = await owner.CreateGroupAsync("Theirs", ct);
        await owner.GetInvitationAsync(group.Id, ct);

        var (outsider, _) = await fixture.RegisterAsync(ct);
        await outsider.CreateGroupAsync("Mine", ct);

        // Act
        var response = await outsider.GetAsync($"/api/GroupInvitation/group/{group.Id}", ct);

        // Assert
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(ct)).ShouldNotContain("\"token\"");
    }
}
