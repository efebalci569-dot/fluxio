using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Fluxio.Tests.Support;
using Fluxio.Transfer.Protocol;
using Fluxio.Transfer.Sessions;
using Fluxio.Transfer.Transfers;

namespace Fluxio.Security.Tests;

public class ApiSecurityTests
{
    [Fact]
    public async Task Requests_without_or_with_a_wrong_token_are_rejected()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();

        using var anonymous = h.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/state")).StatusCode);

        using var forged = h.NewClient(Fluxio.Transfer.Security.Tokens.NewToken());
        var response = await forged.GetAsync("/api/v1/state");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", (await TestHarness.ReadErrorAsync(response)).Code);
    }

    [Fact]
    public async Task Wrong_pairing_secret_is_rejected()
    {
        await using var h = await TestHarness.StartAsync();

        var response = await h.PostPairAsync(h.Phone, secret: Fluxio.Transfer.Security.Tokens.NewToken());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SessionStatus.AwaitingScan, h.Session.Status);
    }

    [Fact]
    public async Task Expired_qr_code_cannot_be_used()
    {
        await using var h = await TestHarness.StartAsync(o => o with { PairingWindow = TimeSpan.FromMinutes(1) });

        h.Time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));
        var response = await h.PostPairAsync(h.Phone);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("sessionExpired", (await TestHarness.ReadErrorAsync(response)).Code);
    }

    [Fact]
    public async Task A_qr_code_works_for_one_device_only()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync(approve: false);

        using var intruder = h.NewClient();
        var response = await h.PostPairAsync(intruder);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("alreadyPaired", (await TestHarness.ReadErrorAsync(response)).Code);
    }

    [Fact]
    public async Task Refreshing_the_qr_code_invalidates_the_old_code_and_old_tokens()
    {
        await using var h = await TestHarness.StartAsync();
        var oldParts = h.PairingParts();
        var oldToken = await h.PairAsync();
        var oldBaseUrl = h.Session.BaseUrl;

        await h.Manager.StartSessionAsync();
        using var phone = h.NewClient();

        var reuse = await h.PostPairAsync(phone, oldParts.SessionId, oldParts.Secret);
        Assert.Equal(HttpStatusCode.NotFound, reuse.StatusCode);
        Assert.Equal("sessionNotFound", (await TestHarness.ReadErrorAsync(reuse)).Code);

        using var oldClient = h.NewClient(oldToken);
        var state = await oldClient.GetAsync("/api/v1/state");
        Assert.Equal(HttpStatusCode.Gone, state.StatusCode);
        var error = await TestHarness.ReadErrorAsync(state);
        Assert.Equal("sessionClosed", error.Code);
        Assert.Equal("replaced", error.Reason);
        Assert.NotNull(oldBaseUrl);
    }

    [Fact]
    public async Task Closed_session_cannot_be_used()
    {
        await using var h = await TestHarness.StartAsync();
        var token = await h.PairAsync();
        var baseUrl = h.Session.BaseUrl;

        await h.Manager.CloseSessionAsync();

        // The listener is stopped, so nothing answers at all.
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync("/api/v1/state"));
        Assert.Equal(SessionStatus.Closed, h.Session.Status);
    }

    [Fact]
    public async Task Ids_from_another_session_are_useless()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var data = TestHarness.RandomBytes(100);
        var path = h.CreateSourceFile("secret.txt", data);
        var offer = h.Manager.OfferFiles([path]);
        var fileId = offer.Files[0].Id;

        // New session, new phone: the previous batch/file ids must not resolve.
        await h.Manager.StartSessionAsync();
        await using var other = h; // same harness, new session
        using var phone = h.NewClient();
        var pair = await h.PostPairAsync(phone);
        var token = (await pair.Content.ReadFromJsonAsync<PairResponse>(TestHarness.Json))!.Token;
        phone.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        h.Manager.ApproveDevice();

        Assert.Equal(HttpStatusCode.NotFound, (await phone.PostAsync($"/api/v1/offers/{offer.Id}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await phone.GetAsync($"/api/v1/offers/{offer.Id}/files/{fileId}/content")).StatusCode);
        Assert.Empty((await h.GetStateAsync(phone)).Offers);
    }

    [Fact]
    public async Task Unknown_file_ids_return_not_found()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var batch = await h.CreateUploadAsync(("a.txt", [1]));
        h.Manager.AcceptIncoming(batch.Id, ConflictPolicy.Rename);

        Assert.Equal(HttpStatusCode.NotFound, (await h.PutChunkAsync(batch.Id, "doesNotExist", 0, [1])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.PutChunkAsync(batch.Id, "..%2F..%2Fetc", 0, [1])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.PutChunkAsync("otherBatch", batch.Files[0].Id, 0, [1])).StatusCode);
    }

    [Fact]
    public async Task Traversal_names_are_stored_inside_the_download_folder()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var data = "pwned"u8.ToArray();
        var batch = await h.CreateUploadAsync((@"..\..\..\evil.bat", data));
        Assert.Equal("evil.bat", batch.Files[0].Name);
        h.Manager.AcceptIncoming(batch.Id, ConflictPolicy.Rename);

        await TestHarness.EnsureOk(await h.UploadAsync(batch.Id, batch.Files[0].Id, data));

        Assert.True(File.Exists(Path.Combine(h.Downloads, "evil.bat")));
        Assert.False(File.Exists(Path.Combine(h.Root, "evil.bat")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(h.Root)!, "evil.bat")));
    }

    [Fact]
    public async Task Sending_more_bytes_than_announced_is_rejected()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var batch = await h.CreateUploadAsync(("a.bin", new byte[10]));
        h.Manager.AcceptIncoming(batch.Id, ConflictPolicy.Rename);

        var response = await h.PutChunkAsync(batch.Id, batch.Files[0].Id, 0, new byte[11]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Oversized_chunks_are_rejected()
    {
        await using var h = await TestHarness.StartAsync(o => o with { MaxChunkSize = 1024 });
        await h.PairAsync();
        var batch = await h.CreateUploadAsync(("a.bin", new byte[4096]));
        h.Manager.AcceptIncoming(batch.Id, ConflictPolicy.Rename);

        var response = await h.PutChunkAsync(batch.Id, batch.Files[0].Id, 0, new byte[2048]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Oversized_json_is_rejected()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var huge = new StringContent("{\"files\":[" + string.Join(',', Enumerable.Repeat("{\"name\":\"" + new string('a', 1000) + "\",\"size\":1}", 400)) + "]}", Encoding.UTF8, "application/json");

        var response = await h.Phone.PostAsync("/api/v1/uploads", huge);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_host_header_is_rejected_against_dns_rebinding()
    {
        await using var h = await TestHarness.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ping");
        request.Headers.Host = "attacker.example";

        var response = await h.Phone.SendAsync(request);

        Assert.Equal(HttpStatusCode.MisdirectedRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cross_origin_writes_are_rejected()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/leave");
        request.Headers.Add("Origin", "http://evil.example");

        var response = await h.Phone.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(SessionStatus.Connected, h.Session.Status);
    }

    [Fact]
    public async Task Cross_site_fetches_are_rejected()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/state");
        request.Headers.Add("Sec-Fetch-Site", "cross-site");

        var response = await h.Phone.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pairing_attempts_are_rate_limited()
    {
        await using var h = await TestHarness.StartAsync();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++)
        {
            codes.Add((await h.PostPairAsync(h.Phone, secret: "wrong-secret-wrong-secret")).StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, codes);
        Assert.Equal(SessionStatus.AwaitingScan, h.Session.Status);
    }

    [Fact]
    public async Task Responses_carry_strict_security_headers()
    {
        await using var h = await TestHarness.StartAsync();

        var page = await h.Phone.GetAsync("/");
        var api = await h.Phone.GetAsync("/api/v1/ping");

        Assert.Contains("frame-ancestors 'none'", page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", page.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", page.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("no-store", api.Headers.CacheControl!.ToString());
        Assert.False(api.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Download_links_expire()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var offer = h.Manager.OfferFiles([h.CreateSourceFile("a.bin", [1, 2, 3])]);
        await TestHarness.EnsureOk(await h.Phone.PostAsync($"/api/v1/offers/{offer.Id}/accept", null));
        var ticket = await h.Phone.PostAsync($"/api/v1/offers/{offer.Id}/files/{offer.Files[0].Id}/ticket", null);
        var link = (await ticket.Content.ReadFromJsonAsync<TicketResponse>(TestHarness.Json))!.Url;

        h.Time.Advance(TimeSpan.FromMinutes(3));
        using var browser = h.NewClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(link)).StatusCode);
    }
}
