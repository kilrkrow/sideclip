using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace Sideclip.Cli;

internal sealed class PickResult
{
    public string Kind { get; init; } = "cancel";
    public string? Text { get; init; }
    public string? Path { get; init; }
}

internal sealed class IpcServer : IDisposable
{
    public const string PipeName = "Sideclip.cmd";
    public const string MutexName = @"Local\Sideclip.single";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly Dispatcher _dispatcher;
    private readonly Action _openPicker;
    private readonly Func<Task<PickResult>> _pick;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;

    public IpcServer(Dispatcher dispatcher, Action openPicker, Func<Task<PickResult>> pick)
    {
        _dispatcher = dispatcher;
        _openPicker = openPicker;
        _pick = pick;
    }

    public void Start()
    {
        _thread = new Thread(Listen) { IsBackground = true, Name = "SideclipIpc" };
        _thread.Start();
    }

    private void Listen()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var pipe = CreateServer();
                pipe.WaitForConnection();
                if (_cts.IsCancellationRequested)
                    return;
                Handle(pipe);
            }
            catch (Exception)
            {
                if (_cts.IsCancellationRequested)
                    return;
                Thread.Sleep(50);
            }
        }
    }

    private void Handle(NamedPipeServerStream pipe)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
        var line = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
        {
            writer.WriteLine(JsonSerializer.Serialize(new IpcResponse { Ok = false, Error = "empty request" }, JsonOpts));
            return;
        }

        IpcRequest? req;
        try
        {
            req = JsonSerializer.Deserialize<IpcRequest>(line, JsonOpts);
        }
        catch (Exception ex)
        {
            writer.WriteLine(JsonSerializer.Serialize(new IpcResponse { Ok = false, Error = ex.Message }, JsonOpts));
            return;
        }

        var cmd = (req?.Cmd ?? "").Trim().ToLowerInvariant();
        IpcResponse reply;
        try
        {
            reply = cmd switch
            {
                "picker" => RunOpenPicker(),
                "pick" => RunPick(),
                _ => new IpcResponse { Ok = false, Error = "unknown command " + cmd }
            };
        }
        catch (Exception ex)
        {
            reply = new IpcResponse { Ok = false, Error = ex.Message };
        }

        writer.WriteLine(JsonSerializer.Serialize(reply, JsonOpts));
    }

    private IpcResponse RunOpenPicker()
    {
        _dispatcher.BeginInvoke(_openPicker);
        return new IpcResponse { Ok = true, Kind = "ok" };
    }

    private IpcResponse RunPick()
    {
        var result = _pick().GetAwaiter().GetResult();
        return new IpcResponse
        {
            Ok = true,
            Kind = result.Kind,
            Text = result.Text,
            Path = result.Path
        };
    }

    private static NamedPipeServerStream CreateServer()
    {
        var user = WindowsIdentity.GetCurrent().User;
        var security = new PipeSecurity();
        if (user is not null)
        {
            security.AddAccessRule(new PipeAccessRule(
                user,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
        }

        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.None,
            4096,
            4096,
            security);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
            client.Connect(50);
        }
        catch
        {
        }

        _cts.Dispose();
    }

    public static IpcResponse? TrySend(string cmd, int timeoutMs)
    {
        NamedPipeClientStream? client = null;
        try
        {
            client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
            try
            {
                client.Connect(1500);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                return new IpcResponse { Ok = false, Error = "Sideclip tray is not running. Start this build of Sideclip.exe first." };
            }

            using var writer = new StreamWriter(client, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            writer.WriteLine(JsonSerializer.Serialize(new IpcRequest { Cmd = cmd }, JsonOpts));

            string? line;
            if (timeoutMs <= 0)
                line = reader.ReadLine();
            else
            {
                var read = Task.Run(reader.ReadLine);
                if (!read.Wait(timeoutMs))
                    return new IpcResponse { Ok = false, Error = "Sideclip did not answer in time. Close or use the picker if it is still open." };
                line = read.Result;
            }

            if (string.IsNullOrWhiteSpace(line))
                return new IpcResponse { Ok = false, Error = "empty reply" };
            return JsonSerializer.Deserialize<IpcResponse>(line, JsonOpts);
        }
        catch (Exception ex)
        {
            return new IpcResponse { Ok = false, Error = ex.Message };
        }
        finally
        {
            client?.Dispose();
        }
    }

    private sealed class IpcRequest
    {
        [JsonPropertyName("cmd")]
        public string Cmd { get; set; } = "";
    }
}

internal sealed class IpcResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }
}
