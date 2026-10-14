using System.Buffers.Text;
using System.Net;
using Fluxio.Transfer;
using Fluxio.Transfer.Security;

namespace Fluxio.Security.Tests;

public class FileNameSanitizerTests
{
    [Theory]
    [InlineData("../../Windows/System32/evil.dll", "evil.dll")]
    [InlineData(@"..\..\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\run.bat", "run.bat")]
    [InlineData("C:\\Windows\\win.ini", "win.ini")]
    [InlineData("/etc/passwd", "passwd")]
    [InlineData("..", "file")]
    [InlineData(".", "file")]
    [InlineData("   ", "file")]
    [InlineData("", "file")]
    [InlineData(null, "file")]
    [InlineData("report.pdf:Zone.Identifier", "report.pdf_Zone.Identifier")]
    [InlineData("a<b>c|d?e*f\"g.txt", "a_b_c_d_e_f_g.txt")]
    [InlineData("trailing dots...", "trailing dots")]
    [InlineData("name.txt.  ", "name.txt")]
    [InlineData("tab\there.txt", "tab_here.txt")]
    [InlineData("CON", "_CON")]
    [InlineData("con.txt", "_con.txt")]
    [InlineData("LPT1.log", "_LPT1.log")]
    [InlineData("COM9", "_COM9")]
    [InlineData("CONSOLE.txt", "CONSOLE.txt")]
    [InlineData("fotoğraf_çiçek.jpg", "fotoğraf_çiçek.jpg")]
    [InlineData("📷 photo.heic", "📷 photo.heic")]
    public void Produces_a_single_safe_windows_file_name(string? input, string expected)
    {
        Assert.Equal(expected, FileNameSanitizer.Sanitize(input));
    }

    [Fact]
    public void Removes_bidirectional_overrides_that_disguise_extensions()
    {
        // "invoice\u202Efdp.exe" renders as "invoiceexe.pdf" but is an .exe.
        var result = FileNameSanitizer.Sanitize("invoice\u202Efdp.exe");

        Assert.Equal("invoice_fdp.exe", result);
        Assert.EndsWith(".exe", result);
    }

    [Fact]
    public void Normalizes_to_nfc()
    {
        var decomposed = "c\u0327ic\u0327ek.txt"; // "çiçek" with combining cedillas
        Assert.Equal("çiçek.txt", FileNameSanitizer.Sanitize(decomposed));
    }

    [Fact]
    public void Truncates_long_names_but_keeps_the_extension()
    {
        var result = FileNameSanitizer.Sanitize(new string('a', 400) + ".docx");

        Assert.Equal(FileNameSanitizer.MaxLength, result.Length);
        Assert.EndsWith(".docx", result);
    }

    [Fact]
    public void Never_splits_a_surrogate_pair_when_truncating()
    {
        var result = FileNameSanitizer.Sanitize(string.Concat(Enumerable.Repeat("😀", 200)));

        Assert.True(result.Length <= FileNameSanitizer.MaxLength);
        Assert.False(char.IsHighSurrogate(result[^1]));
    }

    [Fact]
    public void Lone_surrogates_are_replaced()
    {
        Assert.Equal("a_b", FileNameSanitizer.Sanitize("a\uD800b"));
    }
}

public sealed class SafePathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fluxio-sec", Guid.NewGuid().ToString("N"));

    public SafePathTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("../x.txt")]
    [InlineData(@"..\x.txt")]
    [InlineData("sub/x.txt")]
    [InlineData(@"C:\x.txt")]
    [InlineData("x.txt:stream")]
    [InlineData("")]
    public void Rejects_names_that_could_leave_the_folder(string name)
    {
        var error = Assert.Throws<FluxioException>(() => SafePath.CombineInside(_root, name));
        Assert.Equal(FluxioErrorCode.BadRequest, error.Code);
    }

    [Fact]
    public void Accepts_plain_names_inside_the_folder()
    {
        Assert.Equal(Path.Combine(_root, "photo.jpg"), SafePath.CombineInside(_root, "photo.jpg"));
    }

    [Fact]
    public void Finds_the_next_free_name()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "");
        File.WriteAllText(Path.Combine(_root, "a (1).txt"), "");

        Assert.Equal(Path.Combine(_root, "a (2).txt"), SafePath.NextFreeName(_root, "a.txt"));
    }
}

public class FolderPolicyTests
{
    public static TheoryData<string> ForbiddenFolders()
    {
        var data = new TheoryData<string>();
        foreach (var folder in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            AppContext.BaseDirectory,
        })
        {
            if (!string.IsNullOrEmpty(folder))
            {
                data.Add(folder);
                data.Add(Path.Combine(folder, "Sub"));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ForbiddenFolders))]
    public void Refuses_system_and_program_folders(string folder)
    {
        Assert.False(FolderPolicy.IsAllowed(folder));
    }

    [Theory]
    [InlineData("relative\\folder")]
    [InlineData(@"\\?\C:\Users")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData("")]
    public void Refuses_relative_and_device_paths(string folder)
    {
        Assert.False(FolderPolicy.IsAllowed(folder));
    }

    [Fact]
    public void Refuses_the_root_of_the_system_drive()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.False(FolderPolicy.IsAllowed(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))));
    }

    [Fact]
    public void Accepts_a_normal_user_folder()
    {
        Assert.True(FolderPolicy.IsAllowed(Path.Combine(Path.GetTempPath(), "fluxio-ok")));
    }
}

public class TokenTests
{
    [Fact]
    public void Tokens_carry_256_bits_and_do_not_repeat()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => Tokens.NewToken()).ToList();

        Assert.All(tokens, t => Assert.Equal(32, Base64Url.DecodeFromChars(t).Length));
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Fact]
    public void Comparison_and_hash_validation_behave()
    {
        Assert.True(Tokens.FixedTimeEquals("abc", "abc"));
        Assert.False(Tokens.FixedTimeEquals("abc", "abd"));
        Assert.False(Tokens.FixedTimeEquals("abc", null));
        Assert.True(Tokens.IsSha256Hex(new string('f', 64)));
        Assert.False(Tokens.IsSha256Hex(new string('g', 64)));
        Assert.False(Tokens.IsSha256Hex("abc"));
        Assert.Matches("^[0-9]{4}$", Tokens.NewMatchCode());
    }
}

public class NetworkRulesTests
{
    [Theory]
    [InlineData("192.168.1.20", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("26.164.32.211", false)]
    [InlineData("169.254.10.10", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::ffff:192.168.1.5", true)]
    [InlineData("::ffff:1.1.1.1", false)]
    [InlineData("2001:db8::1", false)]
    public void Only_local_network_clients_are_allowed(string address, bool allowed)
    {
        Assert.Equal(allowed, NetworkRules.IsAllowedRemote(IPAddress.Parse(address)));
    }
}
