using System.Net;
using System.Net.Http.Json;
using Fluxio.Tests.Support;
using Fluxio.Transfer.Protocol;
using Fluxio.Transfer.Storage;
using Fluxio.Transfer.Transfers;

namespace Fluxio.Transfer.Tests;

public class PhoneToComputerTests
{
    [Fact]
    public async Task Uploaded_file_is_verified_and_saved_with_identical_content()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var data = TestHarness.RandomBytes(300_000);

        var batch = await h.CreateUploadAsync(("holiday.jpg", data));
        Assert.Equal("awaitingApproval", batch.Status);
        h.Manager.AcceptIncoming(batch.Id, ConflictPolicy.Rename);

        var response = await h.UploadAsync(batch.Id, batch.Files[0].Id, data, chunkSize: 70_000);
        await TestHarness.EnsureOk(response);
        var file = (await response.Content.ReadFromJsonAsync<FileDto>(TestHarness.Json))!;

        Assert.Equal("completed", file.State);
        Assert.True(file.Verified);
        Assert.Equal("holiday.jpg", file.SavedName);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(h.Downloads, "holiday.jpg")));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(h.Downloads, IncomingStore.TempFolderName)));

        var entry = Assert.Single(h.History.Entries);
        Assert.Equal(HistoryOutcome.Completed, entry.Outcome);
        Assert.Equal(TransferDirection.Incoming, entry.Direction);
        Assert.True(entry.Verified);

        var state = await h.GetStateAsync();
        Assert.Equal("finished", Assert.Single(state.Uploads).Status);
    }

    [Fact]
    public async Task Hash_mismatch_fails_the_file_and_removes_partial_data()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var data = TestHarness.RandomBytes(10_000);
        var batch = await h.CreateUploadAsync(("doc.pdf", data));
        h.Manager.AcceptIncoming(batch.Id, ConflictPolicy.Rename);

        var response = await h.UploadAsync(batch.Id, batch.Files[0].Id, data, sha256: new string('0', 64));

        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal("integrityFailed", (await TestHarness.ReadErrorAsync(response)).Code);
        Assert.False(File.Exists(Path.Combine(h.Downloads, "doc.pdf")));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(h.Downloads, IncomingStore.TempFolderName)));
        Assert.Equal(HistoryOutcome.Failed, Assert.Single(h.History.Entries).Outcome);
    }

    [Fact]
    public async Task Wrong_offset_is_rejected_with_the_server_position_for_resuming()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var data = TestHarness.RandomBytes(100);
        var batch = await h.CreateUploadAsync(("a.bin", data));
        h.Manager.AcceptIncoming(batch.Id, ConflictPolicy.Rename);
        var fileId = batch.Files[0].Id;

        await TestHarness.EnsureOk(await h.PutChunkAsync(batch.Id, fileId, 0, data[..40]));
        var mismatch = await h.PutChunkAsync(batch.Id, fileId, 10, data[10..]);

        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        var error = await TestHarness.ReadErrorAsync(mismatch);
        Assert.Equal("offsetMismatch", error.Code);
        Assert.Equal(40, error.Received);

        // Resume from the reported position.
        var status = await h.Phone.GetFromJsonAsync<ChunkResult>($"/api/v1/uploads/{batch.Id}/files/{fileId}", TestHarness.Json);
        Assert.Equal(40, status!.Received);
        await TestHarness.EnsureOk(await h.PutChunkAsync(batch.Id, fileId, 40, data[40..]));
        var done = await h.Phone.PostAsJsonAsync($"/api/v1/uploads/{batch.Id}/files/{fileId}/complete", new HashRequest(TestHarness.Sha256(data)));
        await TestHarness.EnsureOk(done);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(h.Downloads, "a.bin")));
    }

    [Fact]
    public async Task Completing_before_all_data_arrived_is_refused()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var data = TestHarness.RandomBytes(1000);
        var batch = await h.CreateUploadAsync(("a.bin", data));
        h.Manager.AcceptIncoming(batch.Id, ConflictPolicy.Rename);
        await TestHarness.EnsureOk(await h.PutChunkAsync(batch.Id, batch.Files[0].Id, 0, data[..500]));

        var response = await h.Phone.PostAsJsonAsync($"/api/v1/uploads/{batch.Id}/files/{batch.Files[0].Id}/complete", new HashRequest(TestHarness.Sha256(data)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("incomplete", (await TestHarness.ReadErrorAsync(response)).Code);
    }

    [Theory]
    [InlineData(ConflictPolicy.Rename, "report (1).txt", "old")]
    [InlineData(ConflictPolicy.Overwrite, "report.txt", "new")]
    [InlineData(ConflictPolicy.Skip, null, "old")]
    public async Task Name_conflicts_follow_the_chosen_policy(ConflictPolicy policy, string? expectedName, string expectedOriginal)
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        Directory.CreateDirectory(h.Downloads);
        File.WriteAllText(Path.Combine(h.Downloads, "report.txt"), "old");
        var data = "new"u8.ToArray();

        var batch = await h.CreateUploadAsync(("report.txt", data));
        Assert.Equal(["report.txt"], h.Manager.FindConflicts((IncomingBatch)h.Session.Batches[0], h.Downloads));
        h.Manager.AcceptIncoming(batch.Id, policy);
        var response = await h.UploadAsync(batch.Id, batch.Files[0].Id, data);
        await TestHarness.EnsureOk(response);
        var file = (await response.Content.ReadFromJsonAsync<FileDto>(TestHarness.Json))!;

        Assert.Equal(expectedName, file.SavedName);
        Assert.Equal(policy == ConflictPolicy.Skip ? "skipped" : "completed", file.State);
        Assert.Equal(expectedOriginal, File.ReadAllText(Path.Combine(h.Downloads, "report.txt")));
        if (policy == ConflictPolicy.Rename)
        {
            Assert.Equal("new", File.ReadAllText(Path.Combine(h.Downloads, expectedName!)));
        }
    }

    [Fact]
    public async Task Declined_request_rejects_uploads()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var batch = await h.CreateUploadAsync(("a.txt", "hi"u8.ToArray()));
        h.Manager.DeclineIncoming(batch.Id);

        var response = await h.PutChunkAsync(batch.Id, batch.Files[0].Id, 0, "hi"u8.ToArray());

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("declined", (await TestHarness.ReadErrorAsync(response)).Code);
        Assert.Equal(HistoryOutcome.Declined, Assert.Single(h.History.Entries).Outcome);
        Assert.Equal("declined", (await h.GetStateAsync()).Uploads[0].Status);
    }

    [Fact]
    public async Task Upload_before_acceptance_is_refused()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var batch = await h.CreateUploadAsync(("a.txt", "hi"u8.ToArray()));

        var response = await h.PutChunkAsync(batch.Id, batch.Files[0].Id, 0, "hi"u8.ToArray());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("invalidState", (await TestHarness.ReadErrorAsync(response)).Code);
    }

    [Fact]
    public async Task Cancelling_one_file_keeps_the_others_and_deletes_partial_data()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var first = TestHarness.RandomBytes(5000);
        var second = TestHarness.RandomBytes(5000);
        var batch = await h.CreateUploadAsync(("one.bin", first), ("two.bin", second));
        h.Manager.AcceptIncoming(batch.Id, ConflictPolicy.Rename);

        await TestHarness.EnsureOk(await h.PutChunkAsync(batch.Id, batch.Files[0].Id, 0, first[..1000]));
        h.Manager.CancelFile(batch.Id, batch.Files[0].Id);

        var rejected = await h.PutChunkAsync(batch.Id, batch.Files[0].Id, 1000, first[1000..]);
        Assert.Equal(HttpStatusCode.Gone, rejected.StatusCode);
        Assert.Equal("cancelled", (await TestHarness.ReadErrorAsync(rejected)).Code);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(h.Downloads, IncomingStore.TempFolderName)));

        await TestHarness.EnsureOk(await h.UploadAsync(batch.Id, batch.Files[1].Id, second));
        Assert.Equal(second, File.ReadAllBytes(Path.Combine(h.Downloads, "two.bin")));

        var state = await h.GetStateAsync();
        Assert.Equal("finished", state.Uploads[0].Status);
        Assert.Equal(["cancelled", "completed"], state.Uploads[0].Files.Select(f => f.State));
    }

    [Fact]
    public async Task Phone_can_cancel_the_whole_request()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var batch = await h.CreateUploadAsync(("a.bin", new byte[10]), ("b.bin", new byte[10]));
        h.Manager.AcceptIncoming(batch.Id, ConflictPolicy.Rename);

        var response = await h.Phone.DeleteAsync($"/api/v1/uploads/{batch.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("cancelled", (await h.GetStateAsync()).Uploads[0].Status);
        Assert.All(h.History.Entries, e => Assert.Equal(HistoryOutcome.Cancelled, e.Outcome));
    }

    [Fact]
    public async Task Without_incoming_approval_requests_start_immediately()
    {
        await using var h = await TestHarness.StartAsync(o => o with { RequireIncomingApproval = false });
        await h.PairAsync();
        var data = TestHarness.RandomBytes(2048);

        var batch = await h.CreateUploadAsync(("auto.bin", data));

        Assert.Equal("inProgress", batch.Status);
        await TestHarness.EnsureOk(await h.UploadAsync(batch.Id, batch.Files[0].Id, data));
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(h.Downloads, "auto.bin")));
    }

    [Fact]
    public async Task Empty_files_are_supported()
    {
        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var batch = await h.CreateUploadAsync(("empty.txt", []));
        h.Manager.AcceptIncoming(batch.Id, ConflictPolicy.Rename);

        await TestHarness.EnsureOk(await h.UploadAsync(batch.Id, batch.Files[0].Id, []));

        Assert.Equal(0, new FileInfo(Path.Combine(h.Downloads, "empty.txt")).Length);
    }

    [Fact]
    public async Task Limits_on_size_and_count_are_enforced()
    {
        await using var h = await TestHarness.StartAsync(o => o with { MaxFileSize = 1000, MaxFilesPerBatch = 2 });
        await h.PairAsync();

        var tooLarge = await h.Phone.PostAsJsonAsync("/api/v1/uploads", new CreateUploadRequest([new UploadFileSpec("big.bin", 1001, null)]));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Equal("tooLarge", (await TestHarness.ReadErrorAsync(tooLarge)).Code);

        var tooMany = await h.Phone.PostAsJsonAsync("/api/v1/uploads", new CreateUploadRequest(
            [new UploadFileSpec("1", 1, null), new UploadFileSpec("2", 1, null), new UploadFileSpec("3", 1, null)]));
        Assert.Equal("tooManyFiles", (await TestHarness.ReadErrorAsync(tooMany)).Code);
    }

    [Fact]
    public async Task Received_files_get_the_mark_of_the_web_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // Alternate data streams are a Windows/NTFS feature.
        }

        await using var h = await TestHarness.StartAsync();
        await h.PairAsync();
        var batch = await h.CreateUploadAsync(("setup.exe", "MZ"u8.ToArray()));
        h.Manager.AcceptIncoming(batch.Id, ConflictPolicy.Rename);
        await TestHarness.EnsureOk(await h.UploadAsync(batch.Id, batch.Files[0].Id, "MZ"u8.ToArray()));

        var zone = File.ReadAllText(Path.Combine(h.Downloads, "setup.exe") + ":Zone.Identifier");

        Assert.Contains("ZoneId=3", zone);
    }
}
