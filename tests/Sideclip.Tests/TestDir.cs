namespace Sideclip.Tests;

/// <summary>Throwaway folder under the system temp dir, deleted on dispose.</summary>
internal sealed class TestDir : IDisposable
{
    public string Path { get; }

    public TestDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sideclip-tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path);
    }

    public string Sub(string name)
    {
        var p = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(p);
        return p;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch
        {
        }
    }
}
