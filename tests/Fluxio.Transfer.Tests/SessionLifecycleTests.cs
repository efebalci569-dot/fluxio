using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Fluxio.Tests.Support;
using Fluxio.Transfer.Protocol;
using Fluxio.Transfer.Sessions;

namespace Fluxio.Transfer.Tests;

public class SessionLifecycleTests
{
    [Fact]
    public async Task New_session_waits_for_a_scan_and_serves_the_web_page()
    {
        await using var h = await TestHarness.StartAsync();

        Assert.Equal(SessionStatus.AwaitingScan, h.Session.Status);
        Assert.StartsWith("http://127.0.0.1:", h.Session.PairingUrl);
        Assert.Contains("#s=", h.Session.PairingUrl);

        var page = await h.Phone.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Pairing_requires_approval_before_transfers()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync(approve: false);

        var state = await h.GetStateAsync();
        Assert.Equal("awaitingApproval", state.Status);
        Assert.False(state.Approved);
        Assert.Matches("^[0-9]{4}$", state.MatchCode);
        Assert.Equal(h.Session.Device!.MatchCode, state.MatchCode);

        var upload = await h.Phone.PostAsJsonAsync("/api/v1/uploads", new CreateUploadRequest([new UploadFileSpec("a", 1, null)]));
        Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);
        Assert.Equal("deviceNotApproved", (await TestHarness.ReadErrorAsync(upload)).Code);

        h.Manager.ApproveDevice();
        state = await h.GetStateAsync();
        Assert.Equal("connected", state.Status);
        Assert.True(state.Approved);
    }

    [Fact]
    public async Task Rejected_phone_is_told_why()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync(approve: false);
        var version = (await h.GetStateAsync()).Version;

        // The phone is long-polling when the computer user refuses it: the poll returns the
        // final status before the listener shuts down.
        var poll = h.Phone.GetFromJsonAsync<PhoneState>($"/api/v1/state?since={version}&wait=20", TestHarness.Json);
        await Task.Delay(200);
        await h.Manager.RejectDeviceAsync();
        var state = await poll;

        Assert.Equal("closed", state!.Status);
        Assert.Equal("deviceRejected", state.CloseReason);
    }

    [Fact]
    public async Task Long_poll_returns_as_soon_as_something_changes()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync(approve: false);
        var version = (await h.GetStateAsync()).Version;

        var stopwatch = Stopwatch.StartNew();
        var poll = h.Phone.GetFromJsonAsync<PhoneState>($"/api/v1/state?since={version}&wait=20", TestHarness.Json);
        await Task.Delay(300);
        h.Manager.ApproveDevice();
        var state = await poll;

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.True(state!.Version > version);
        Assert.Equal("connected", state.Status);
    }

    [Fact]
    public async Task Idle_connected_session_closes_after_the_timeout()
    {
        await using var h = await TestHarness.StartAsync(o => o with { IdleTimeout = TimeSpan.FromMinutes(1) });
        await h.PairAsync();

        h.Time.Advance(TimeSpan.FromMinutes(2));
        await WaitUntil(() => h.Session.Status == SessionStatus.Closed);

        Assert.Equal(CloseReason.IdleTimeout, h.Session.CloseReason);
    }

    [Fact]
    public async Task Unapproved_phone_expires_after_the_approval_window()
    {
        await using var h = await TestHarness.StartAsync(o => o with { ApprovalWindow = TimeSpan.FromMinutes(1) });
        await h.PairAsync(approve: false);

        h.Time.Advance(TimeSpan.FromMinutes(2));
        await WaitUntil(() => h.Session.Status == SessionStatus.Expired);

        Assert.Equal(CloseReason.ApprovalExpired, h.Session.CloseReason);
    }

    [Fact]
    public async Task Closing_the_session_cancels_running_transfers_and_cleans_up()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var data = TestHarness.RandomBytes(1000);
        var batch = await h.CreateUploadAsync(("a.bin", data));
        h.Manager.AcceptIncoming(batch.Id, Transfers.ConflictPolicy.Rename);
        await TestHarness.EnsureOk(await h.PutChunkAsync(batch.Id, batch.Files[0].Id, 0, data[..100]));

        await h.Manager.CloseSessionAsync();

        Assert.Equal(SessionStatus.Closed, h.Session.Status);
        Assert.Equal(Transfers.BatchStatus.Cancelled, h.Session.Batches[0].Status);
        var temp = Path.Combine(h.Downloads, Storage.IncomingStore.TempFolderName);
        Assert.Empty(Directory.EnumerateFiles(temp));
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => h.Phone.GetAsync("/api/v1/state"));
    }

    internal static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(25);
        }
    }
}
