using System.Net;
using System.Net.Http.Json;
using Shouldly;
using simpleShare_backend.Contracts.Group;
using simpleShare_backend.Models.Enums;

namespace SimpleShare.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class GroupCoverTests(ApiFixture fixture)
{
    [Fact]
    public async Task CoverKeysRoundTripAndLegacyUpdatesPreserveSelection()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var response = await client.PostAsJsonAsync("/api/Group",
            new { Name = "Shared", Currency = "EUR", Type = GroupType.Trip, CoverImageKey = "paris-sunset" }, ct);
        response.EnsureSuccessStatusCode();
        var group = (await response.Content.ReadFromJsonAsync<GroupResponse>(ct))!;
        group.CoverImageKey.ShouldBe("paris-sunset");

        response = await client.PutAsJsonAsync("/api/Group", new { group.Id, CoverImageKey = "tokyo-sunset" }, ct);
        response.EnsureSuccessStatusCode();
        response = await client.PutAsJsonAsync("/api/Group", new { group.Id, Name = "Renamed" }, ct);
        response.EnsureSuccessStatusCode();

        var readBack = await client.GetFromJsonAsync<GroupResponse>($"/api/Group/{group.Id}", ct);
        readBack!.CoverImageKey.ShouldBe("tokyo-sunset");
        (await client.GetMyGroupsAsync(ct)).Data.Single().CoverImageKey.ShouldBe("tokyo-sunset");
        var recent = await client.GetFromJsonAsync<List<GroupResponse>>("/api/Group/recent", ct);
        recent!.Single().CoverImageKey.ShouldBe("tokyo-sunset");
        var overview = await client.GetFromJsonAsync<GroupOverviewResponse>($"/api/Group/{group.Id}/overview", ct);
        overview!.CoverImageKey.ShouldBe("tokyo-sunset");
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("couple-1")]
    public async Task CreateAndUpdateRejectInvalidCovers(string key)
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var response = await client.PostAsJsonAsync("/api/Group",
            new { Name = "Shared", Currency = "EUR", Type = GroupType.Trip, CoverImageKey = key }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var group = await client.CreateGroupAsync("Shared", ct);
        response = await client.PutAsJsonAsync("/api/Group",
            new { group.Id, Type = GroupType.Trip, CoverImageKey = key }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var saved = await client.GetFromJsonAsync<GroupResponse>($"/api/Group/{group.Id}", ct);
        saved!.Type.ShouldBe(GroupType.Other);
        saved.CoverImageKey.ShouldBe("other-1");
    }

    [Fact]
    public async Task LegacyClientsGetDefaultsWhenCreatingOrChangingType()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _) = await fixture.CreateAuthenticatedClientAsync(ct);
        var group = await client.CreateGroupAsync("Shared", ct);
        group.CoverImageKey.ShouldBe("other-1");

        var response = await client.PutAsJsonAsync("/api/Group", new { group.Id, Type = GroupType.Roommates }, ct);
        response.EnsureSuccessStatusCode();
        var saved = await response.Content.ReadFromJsonAsync<GroupResponse>(ct);
        saved!.CoverImageKey.ShouldBe("roommate-1");
    }
}
