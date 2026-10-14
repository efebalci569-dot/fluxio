using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Fluxio.Transfer;
using Fluxio.Transfer.Protocol;
using Fluxio.Transfer.Sessions;
using Fluxio.Transfer.Storage;
using Microsoft.Extensions.Time.Testing;

namespace Fluxio.Tests.Support;

/// <summary>
/// Runs a real session with the real Kestrel server on 127.0.0.1 and an HttpClient that
/// plays the phone. Time is controlled with <see cref="FakeTimeProvider"/>.
/// </summary>
internal sealed class TestHarness : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private TestHarness(string root, TransferOptions options, SessionManager manager, FakeTimeProvider time, RecordingHistory history)
    {
        Root = root;
        Options = options;
        Manager = manager;
        Time = time;
        History = history;
    }

    public string Root { get; }

    public string Downloads => Options.DownloadFolder;

    public TransferOptions Options { get; }

    public SessionManager Manager { get; }

    public FakeTimeProvider Time { get; }

    public RecordingHistory History { get; }

    public TransferSession Session => Manager.Current!;

    public HttpClient Phone { get; private set; } = null!;

    public static async Task<TestHarness> StartAsync(Func<TransferOptions, TransferOptions>? configure = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "fluxio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var options = new TransferOptions
        {
            DownloadFolder = Path.Combine(root, "downloads"),
            PreferredPort = 0,
            BindAddressOverride = IPAddress.Loopback,
            ComputerName = "TEST-PC",
            DiskReserveBytes = 0,
        };
        options = configure?.Invoke(options) ?? options;

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var history = new RecordingHistory();
        var manager = new SessionManager(options, history, null, time);
        var harness = new TestHarness(root, options, manager, time, history);
        await manager.StartSessionAsync();
        harness.Phone = harness.NewClient();
        return harness;
    }

    public HttpClient NewClient(string? token = null)
    {
        var client = new HttpClient { BaseAddress = new Uri(Session.BaseUrl), Timeout = TimeSpan.FromSeconds(30) };
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    public (string SessionId, string Secret) PairingParts(TransferSession? session = null)
    {
        var fragment = new Uri((session ?? Session).PairingUrl).Fragment.TrimStart('#');
        var parts = fragment.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        return (parts["s"], parts["k"]);
    }

    public Task<HttpResponseMessage> PostPairAsync(HttpClient client, string? sessionId = null, string? secret = null)
    {
        var parts = PairingParts();
        return client.PostAsJsonAsync("/api/v1/pair", new PairRequest(sessionId ?? parts.SessionId, secret ?? parts.Secret, "Test Phone"));
    }

    /// <summary>Pairs <see cref="Phone"/> and (optionally) approves it on the "computer".</summary>
    public async Task<string> PairAsync(bool approve = true)
    {
        var response = await PostPairAsync(Phone);
        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<PairResponse>(Json))!;
        Phone.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.Token);
        if (approve)
        {
            Manager.ApproveDevice();
        }

        return body.Token;
    }

    public async Task<PhoneState> GetStateAsync(HttpClient? client = null)
    {
        var response = await (client ?? Phone).GetAsync("/api/v1/state");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PhoneState>(Json))!;
    }

    public async Task<BatchDto> CreateUploadAsync(params (string Name, byte[] Data)[] files)
    {
        var response = await Phone.PostAsJsonAsync("/api/v1/uploads",
            new CreateUploadRequest(files.Select(f => new UploadFileSpec(f.Name, f.Data.Length, "application/octet-stream")).ToList()));
        await EnsureOk(response);
        return (await response.Content.ReadFromJsonAsync<BatchDto>(Json))!;
    }

    public Task<HttpResponseMessage> PutChunkAsync(string batchId, string fileId, long offset, byte[] data, HttpClient? client = null)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return (client ?? Phone).PutAsync($"/api/v1/uploads/{batchId}/files/{fileId}?offset={offset}", content);
    }

    /// <summary>Uploads all bytes in chunks and completes with the given (or the correct) hash.</summary>
    public async Task<HttpResponseMessage> UploadAsync(string batchId, string fileId, byte[] data, int chunkSize = 64 * 1024, string? sha256 = null)
    {
        for (var offset = 0; offset < data.Length; offset += chunkSize)
        {
            var chunk = data.AsSpan(offset, Math.Min(chunkSize, data.Length - offset)).ToArray();
            await EnsureOk(await PutChunkAsync(batchId, fileId, offset, chunk));
        }

        return await Phone.PostAsJsonAsync($"/api/v1/uploads/{batchId}/files/{fileId}/complete", new HashRequest(sha256 ?? Sha256(data)));
    }

    public string CreateSourceFile(string name, byte[] data)
    {
        var folder = Path.Combine(Root, "source");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    public static byte[] RandomBytes(int length)
    {
        var data = new byte[length];
        Random.Shared.NextBytes(data);
        return data;
    }

    public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public static async Task<ErrorInfo> ReadErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>(Json);
        return body!.Error;
    }

    public static async Task EnsureOk(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {text}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        Phone?.Dispose();
        await Manager.DisposeAsync();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed class RecordingHistory : IHistorySink
{
    private readonly List<HistoryEntry> _entries = [];

    public IReadOnlyList<HistoryEntry> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public void Record(HistoryEntry entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }
    }
}
