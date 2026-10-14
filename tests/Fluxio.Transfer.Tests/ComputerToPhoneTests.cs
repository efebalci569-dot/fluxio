using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fluxio.Tests.Support;
using Fluxio.Transfer.Protocol;
using Fluxio.Transfer.Storage;

namespace Fluxio.Transfer.Tests;

public class ComputerToPhoneTests
{
    private static async Task<(TestHarness H, BatchDto Offer, byte[] Data)> OfferAsync(int size = 200_000, string name = "video.mp4")
    {
        var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var data = TestHarness.RandomBytes(size);
        var path = h.CreateSourceFile(name, data);
        var batch = h.Manager.OfferFiles([path]);
        var state = await h.GetStateAsync();
        var offer = Assert.Single(state.Offers);
        Assert.Equal(batch.Id, offer.Id);
        return (h, offer, data);
    }

    [Fact]
    public async Task Phone_downloads_and_confirms_a_verified_file()
    {
        var (h, offer, data) = await OfferAsync();
        await using var _ = h;
        Assert.Equal("awaitingApproval", offer.Status);
        Assert.Equal("video.mp4", offer.Files[0].Name);

        await TestHarness.EnsureOk(await h.Phone.PostAsync($"/api/v1/offers/{offer.Id}/accept", null));
        var url = $"/api/v1/offers/{offer.Id}/files/{offer.Files[0].Id}";
        var response = await h.Phone.GetAsync(url + "/content");
        await TestHarness.EnsureOk(response);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType!.MediaType);
        var received = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(data, received);

        var hash = await h.Phone.GetFromJsonAsync<HashResponse>(url + "/sha256?wait=10", TestHarness.Json);
        Assert.True(hash!.Ready);
        Assert.Equal(TestHarness.Sha256(data), hash.Sha256);

        var confirm = await h.Phone.PostAsJsonAsync(url + "/confirm", new HashRequest(TestHarness.Sha256(received)));
        await TestHarness.EnsureOk(confirm);
        var file = (await confirm.Content.ReadFromJsonAsync<FileDto>(TestHarness.Json))!;
        Assert.Equal("completed", file.State);
        Assert.True(file.Verified);

        Assert.Equal("finished", (await h.GetStateAsync()).Offers[0].Status);
        var entry = Assert.Single(h.History.Entries);
        Assert.Equal(HistoryOutcome.Completed, entry.Outcome);
        Assert.True(entry.Verified);
    }

    [Fact]
    public async Task Interrupted_download_resumes_with_a_range_request()
    {
        var (h, offer, data) = await OfferAsync();
        await using var _ = h;
        await TestHarness.EnsureOk(await h.Phone.PostAsync($"/api/v1/offers/{offer.Id}/accept", null));
        var url = $"/api/v1/offers/{offer.Id}/files/{offer.Files[0].Id}/content";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(150_000, null);
        var response = await h.Phone.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(150_000, response.Content.Headers.ContentRange!.From);
        Assert.Equal(data[150_000..], await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Wrong_hash_from_the_phone_fails_the_file()
    {
        var (h, offer, _) = await OfferAsync(1000);
        await using var _1 = h;
        await TestHarness.EnsureOk(await h.Phone.PostAsync($"/api/v1/offers/{offer.Id}/accept", null));
        var url = $"/api/v1/offers/{offer.Id}/files/{offer.Files[0].Id}";
        await (await h.Phone.GetAsync(url + "/content")).Content.ReadAsByteArrayAsync();

        var confirm = await h.Phone.PostAsJsonAsync(url + "/confirm", new HashRequest(new string('a', 64)));

        Assert.Equal((HttpStatusCode)422, confirm.StatusCode);
        Assert.Equal("failed", (await h.GetStateAsync()).Offers[0].Files[0].State);
    }

    [Fact]
    public async Task Large_files_can_use_a_single_use_browser_download_link()
    {
        var (h, offer, data) = await OfferAsync(50_000);
        await using var _ = h;
        await TestHarness.EnsureOk(await h.Phone.PostAsync($"/api/v1/offers/{offer.Id}/accept", null));

        var ticket = await h.Phone.PostAsync($"/api/v1/offers/{offer.Id}/files/{offer.Files[0].Id}/ticket", null);
        var link = (await ticket.Content.ReadFromJsonAsync<TicketResponse>(TestHarness.Json))!.Url;
        using var browser = h.NewClient(); // no Authorization header, like a native download

        var download = await browser.GetAsync(link);
        await TestHarness.EnsureOk(download);
        Assert.Equal(data, await download.Content.ReadAsByteArrayAsync());

        var reuse = await browser.GetAsync(link);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var file = (await h.GetStateAsync()).Offers[0].Files[0];
        Assert.Equal("completed", file.State);
        Assert.False(file.Verified); // honest: the page could not check the content
    }

    [Fact]
    public async Task Declined_offer_cannot_be_downloaded()
    {
        var (h, offer, _) = await OfferAsync(100);
        await using var _1 = h;

        await TestHarness.EnsureOk(await h.Phone.PostAsync($"/api/v1/offers/{offer.Id}/decline", null));
        var response = await h.Phone.GetAsync($"/api/v1/offers/{offer.Id}/files/{offer.Files[0].Id}/content");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal(HistoryOutcome.Declined, Assert.Single(h.History.Entries).Outcome);
    }

    [Fact]
    public async Task Download_before_acceptance_is_refused()
    {
        var (h, offer, _) = await OfferAsync(100);
        await using var _1 = h;

        var response = await h.Phone.GetAsync($"/api/v1/offers/{offer.Id}/files/{offer.Files[0].Id}/content");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_file_modified_after_offering_is_not_sent()
    {
        var (h, offer, _) = await OfferAsync(100, "notes.txt");
        await using var _1 = h;
        await TestHarness.EnsureOk(await h.Phone.PostAsync($"/api/v1/offers/{offer.Id}/accept", null));
        var path = Path.Combine(h.Root, "source", "notes.txt");
        File.WriteAllText(path, "changed content of a different length");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        var response = await h.Phone.GetAsync($"/api/v1/offers/{offer.Id}/files/{offer.Files[0].Id}/content");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("sourceChanged", (await TestHarness.ReadErrorAsync(response)).Code);
        Assert.Equal("sourceChanged", (await h.GetStateAsync()).Offers[0].Files[0].Failure);
    }

    [Fact]
    public async Task Computer_can_cancel_an_offer()
    {
        var (h, offer, _) = await OfferAsync(100);
        await using var _1 = h;
        await TestHarness.EnsureOk(await h.Phone.PostAsync($"/api/v1/offers/{offer.Id}/accept", null));

        h.Manager.CancelBatch(offer.Id);
        var response = await h.Phone.GetAsync($"/api/v1/offers/{offer.Id}/files/{offer.Files[0].Id}/content");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("cancelled", (await h.GetStateAsync()).Offers[0].Status);
    }

    [Fact]
    public async Task Offering_requires_an_approved_phone()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync(approve: false);
        var path = h.CreateSourceFile("a.txt", [1, 2, 3]);

        var error = Assert.Throws<FluxioException>(() => h.Manager.OfferFiles([path]));

        Assert.Equal(FluxioErrorCode.InvalidState, error.Code);
    }
}
