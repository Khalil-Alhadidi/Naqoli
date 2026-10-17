using System.Net;
using System.Net.Http.Json;
using Wallet.MockApi;
using Wallet.MockApi.Contracts;

namespace Agent.Core.Tests.Wallet;

/// <summary>
/// The alias write routes (milestone 9a): <c>POST /users/{id}/aliases</c> and <c>DELETE /users/{id}/aliases/{aliasId}</c>.
/// Every test starts from a reset (<see cref="InitializeAsync"/>), so a saved alias never leaks into another test.
/// </summary>
[Collection(WalletApiCollection.Name)]
public sealed class AliasEndpointsTests(WalletApiFixture fixture) : IAsyncLifetime
{
    private const int DemoUserId = SeedFacts.DemoUserId;
    private const int TightUserId = SeedFacts.TightUserId;

    /// <summary>The wallet column's length; the route refuses a longer text.</summary>
    private const int MaxAliasLength = 100;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        if (fixture.DatabaseAvailable)
            await fixture.ResetAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task PostCreatesTheAliasAndContactsListsIt()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        var before = await ContactsAsync(DemoUserId);
        var s02 = Assert.Single(before.Contacts, c => c.Code == "S02");
        var text = s02.SavedName + " x";

        var response = await fixture.Client.PostAsJsonAsync(
            $"/users/{DemoUserId}/aliases", new AliasRequest(text, s02.TargetAccountId), Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/users/{DemoUserId}/contacts", response.Headers.Location?.OriginalString);
        var created = await response.Content.ReadFromJsonAsync<AliasItem>(Token);
        Assert.NotNull(created);
        Assert.Equal(text, created.Text);
        Assert.Equal(s02.TargetAccountId, created.TargetAccountId);
        Assert.Equal(s02.Phone, created.Phone);
        Assert.Equal(s02.RegisteredName, created.RegisteredName);
        Assert.Equal(s02.AccountStatus, created.AccountStatus);

        var after = await ContactsAsync(DemoUserId);
        Assert.Equal(before.Aliases.Count + 1, after.Aliases.Count);
        Assert.Equal(created, after.Aliases[^1]);
        Assert.Equal(created, after.Aliases.MaxBy(a => a.AliasId));
    }

    [Fact]
    public async Task PostStoresTheTextTrimmed()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        var s02 = Assert.Single((await ContactsAsync(DemoUserId)).Contacts, c => c.Code == "S02");
        var text = s02.SavedName + " x";

        var response = await fixture.Client.PostAsJsonAsync(
            $"/users/{DemoUserId}/aliases", new AliasRequest("  " + text + " ", s02.TargetAccountId), Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(text, (await response.Content.ReadFromJsonAsync<AliasItem>(Token))!.Text);
    }

    [Fact]
    public async Task PostTheSameTextTwiceIs409AliasExists()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        var contacts = await ContactsAsync(DemoUserId);
        var s02 = Assert.Single(contacts.Contacts, c => c.Code == "S02");
        var s03 = Assert.Single(contacts.Contacts, c => c.Code == "S03");
        var text = s02.SavedName + " x";

        var first = await fixture.Client.PostAsJsonAsync(
            $"/users/{DemoUserId}/aliases", new AliasRequest(text, s02.TargetAccountId), Token);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // The same text for another account is refused too: one text, one account.
        var second = await fixture.Client.PostAsJsonAsync(
            $"/users/{DemoUserId}/aliases", new AliasRequest(text, s03.TargetAccountId), Token);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("alias_exists", (await second.ReadProblemAsync(Token)).Code());
        Assert.Equal(contacts.Aliases.Count + 1, (await ContactsAsync(DemoUserId)).Aliases.Count);

        // A seeded alias is refused the same way.
        var seeded = contacts.Aliases[0];
        var third = await fixture.Client.PostAsJsonAsync(
            $"/users/{DemoUserId}/aliases", new AliasRequest(seeded.Text, seeded.TargetAccountId), Token);
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal("alias_exists", (await third.ReadProblemAsync(Token)).Code());
    }

    [Fact]
    public async Task ConcurrentPostsOfTheSameTextSaveOnce()
    {
        // Security review R5: the unique index on (OwnerUserId, Text) is the last guard against a racing save; eight
        // simultaneous posts of one text give exactly one 201, the rest 409 alias_exists.
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        var before = await ContactsAsync(DemoUserId);
        var s02 = Assert.Single(before.Contacts, c => c.Code == "S02");
        var request = new AliasRequest(s02.SavedName + " x", s02.TargetAccountId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => fixture.Client.PostAsJsonAsync($"/users/{DemoUserId}/aliases", request, Token)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        var conflicts = responses.Where(r => r.StatusCode != HttpStatusCode.Created).ToList();
        Assert.Equal(7, conflicts.Count);
        foreach (var conflict in conflicts)
        {
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal("alias_exists", (await conflict.ReadProblemAsync(Token)).Code());
        }

        Assert.Equal(before.Aliases.Count + 1, (await ContactsAsync(DemoUserId)).Aliases.Count);
    }

    [Fact]
    public async Task PostABlankTextIs422AliasTextInvalid()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        var before = await ContactsAsync(DemoUserId);
        var target = before.Contacts[0].TargetAccountId;

        foreach (var text in new[] { string.Empty, "   ", new string('x', MaxAliasLength + 1) })
        {
            var response = await fixture.Client.PostAsJsonAsync(
                $"/users/{DemoUserId}/aliases", new AliasRequest(text, target), Token);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal("alias_text_invalid", (await response.ReadProblemAsync(Token)).Code());
        }

        // A body with no text at all is refused the same way.
        var noText = await fixture.Client.PostAsJsonAsync(
            $"/users/{DemoUserId}/aliases", new { targetAccountId = target }, Token);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noText.StatusCode);
        Assert.Equal("alias_text_invalid", (await noText.ReadProblemAsync(Token)).Code());

        Assert.Equal(before.Aliases, (await ContactsAsync(DemoUserId)).Aliases);
    }

    [Fact]
    public async Task PostToAnUnknownAccountIs404UnknownAccount()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        var s02 = Assert.Single((await ContactsAsync(DemoUserId)).Contacts, c => c.Code == "S02");

        var response = await fixture.Client.PostAsJsonAsync(
            $"/users/{DemoUserId}/aliases", new AliasRequest(s02.SavedName + " x", 99_999), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("unknown_account", (await response.ReadProblemAsync(Token)).Code());
    }

    [Fact]
    public async Task PostForAnUnknownUserIs404UnknownUser()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        var s02 = Assert.Single((await ContactsAsync(DemoUserId)).Contacts, c => c.Code == "S02");

        var response = await fixture.Client.PostAsJsonAsync(
            "/users/999/aliases", new AliasRequest(s02.SavedName + " x", s02.TargetAccountId), Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("unknown_user", (await response.ReadProblemAsync(Token)).Code());
    }

    [Fact]
    public async Task DeleteRemovesTheAliasAndTheSecondDeleteIs404UnknownAlias()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        var before = await ContactsAsync(DemoUserId);
        var created = await CreateAsync(DemoUserId, before);

        var first = await fixture.Client.DeleteAsync($"/users/{DemoUserId}/aliases/{created.AliasId}", Token);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(before.Aliases, (await ContactsAsync(DemoUserId)).Aliases);

        var second = await fixture.Client.DeleteAsync($"/users/{DemoUserId}/aliases/{created.AliasId}", Token);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal("unknown_alias", (await second.ReadProblemAsync(Token)).Code());

        var unknownUser = await fixture.Client.DeleteAsync($"/users/999/aliases/{created.AliasId}", Token);
        Assert.Equal(HttpStatusCode.NotFound, unknownUser.StatusCode);
        Assert.Equal("unknown_user", (await unknownUser.ReadProblemAsync(Token)).Code());
    }

    [Fact]
    public async Task DeleteAnotherUsersAliasIs404UnknownAlias()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        var before = await ContactsAsync(DemoUserId);
        var created = await CreateAsync(DemoUserId, before);

        var response = await fixture.Client.DeleteAsync($"/users/{TightUserId}/aliases/{created.AliasId}", Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("unknown_alias", (await response.ReadProblemAsync(Token)).Code());
        Assert.Contains(created, (await ContactsAsync(DemoUserId)).Aliases);
    }

    [Fact]
    public async Task TheRoutesNeedTheClientKey()
    {
        Assert.SkipUnless(fixture.DatabaseAvailable, fixture.SkipReason);
        var before = await ContactsAsync(DemoUserId);
        var s02 = Assert.Single(before.Contacts, c => c.Code == "S02");
        var seeded = before.Aliases[0];

        using var bare = fixture.CreateClient();
        bare.DefaultRequestHeaders.Remove(ClientKeyFilter.HeaderName);

        var post = await bare.PostAsJsonAsync(
            $"/users/{DemoUserId}/aliases", new AliasRequest(s02.SavedName + " x", s02.TargetAccountId), Token);
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal("client_key_required", (await post.ReadProblemAsync(Token)).Code());

        var delete = await bare.DeleteAsync($"/users/{DemoUserId}/aliases/{seeded.AliasId}", Token);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal("client_key_required", (await delete.ReadProblemAsync(Token)).Code());

        Assert.Equal(before.Aliases, (await ContactsAsync(DemoUserId)).Aliases);
    }

    private async Task<ContactsResponse> ContactsAsync(int userId)
    {
        var contacts = await fixture.Client.GetFromJsonAsync<ContactsResponse>($"/users/{userId}/contacts", Token);
        Assert.NotNull(contacts);
        return contacts;
    }

    /// <summary>Saves S02's saved name plus " x" as the user's alias of S02's account and returns it.</summary>
    private async Task<AliasItem> CreateAsync(int userId, ContactsResponse contacts)
    {
        var s02 = Assert.Single(contacts.Contacts, c => c.Code == "S02");
        var response = await fixture.Client.PostAsJsonAsync(
            $"/users/{userId}/aliases", new AliasRequest(s02.SavedName + " x", s02.TargetAccountId), Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<AliasItem>(Token);
        Assert.NotNull(created);
        return created;
    }
}
