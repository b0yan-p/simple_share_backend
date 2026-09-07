using System.Net;
using Shouldly;

namespace SimpleShare.IntegrationTests;

/// <summary>
/// The three root-level routes that make invite links work. All are public by
/// design, so none of them belongs in AuthorizationMatrixTests.
/// </summary>
[Collection(nameof(ApiCollection))]
public sealed class AppLinkTests(ApiFixture fixture)
{
    [Fact] // LNK-01
    public async Task AssetLinks_IsServedAsJsonWithThePackageAndFingerprint()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var anonymous = fixture.CreateClient();

        // Act
        var response = await anonymous.GetAsync("/.well-known/assetlinks.json", ct);

        // Assert
        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        // Google's fetcher rejects anything that is not application/json.
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json", body);

        body.ShouldContain("delegate_permission/common.handle_all_urls");
        body.ShouldContain("\"namespace\":\"android_app\"");
        body.ShouldContain("com.choxster.simpleshare");
        body.ShouldContain("sha256_cert_fingerprints");
    }

    [Fact] // LNK-02
    public async Task AppleSiteAssociation_IsServedAsJsonScopedToTheJoinPath()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var anonymous = fixture.CreateClient();

        // Act
        var response = await anonymous.GetAsync("/.well-known/apple-app-site-association", ct);

        // Assert
        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json", body);

        body.ShouldContain("applinks");
        body.ShouldContain("appIDs");
        // The component key is literally "/", written around C#'s identifier
        // rules — assert it survived the rewrite.
        body.ShouldContain("\"/\":\"/join/*\"");
        body.ShouldNotContain("_slash");
    }

    [Fact] // LNK-03
    public async Task JoinLanding_WithAValidToken_ShowsTheGroup()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await fixture.RegisterAsync(ct);
        var group = await owner.CreateGroupAsync("Ski trip", ct);
        var invitation = await owner.GetInvitationAsync(group.Id, ct);
        var anonymous = fixture.CreateClient();

        // Act
        var response = await anonymous.GetAsync($"/join/{invitation.Token}", ct);

        // Assert
        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html", body);

        body.ShouldContain("Ski trip");
        body.ShouldContain("1 member");
    }

    [Fact] // LNK-04
    public async Task JoinLanding_WithAnUnknownToken_StillRendersAPage()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var anonymous = fixture.CreateClient();

        // Act
        var response = await anonymous.GetAsync("/join/nosuchtokenatall1234ab", ct);

        // Assert
        var body = await response.Content.ReadAsStringAsync(ct);

        // A page, not a 404: whoever followed the link deserves an explanation
        // rather than the browser's error screen.
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html", body);
        body.ShouldContain("no longer valid");
    }

    [Fact] // LNK-05
    public async Task JoinLanding_WithMarkupInTheGroupName_EncodesIt()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var (owner, _) = await fixture.RegisterAsync(ct);
        var group = await owner.CreateGroupAsync("<script>alert(1)</script>", ct);
        var invitation = await owner.GetInvitationAsync(group.Id, ct);
        var anonymous = fixture.CreateClient();

        // Act
        var response = await anonymous.GetAsync($"/join/{invitation.Token}", ct);

        // Assert
        var body = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);

        // Group names are user input on a public, unauthenticated page.
        body.ShouldNotContain("<script>alert(1)</script>");
        body.ShouldContain("&lt;script&gt;");
    }
}
