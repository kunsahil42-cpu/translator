using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.TextToSpeech;

public enum PiperProcessState
{
    NotStarted,
    Starting,
    Running,
    Failed,
    Stopped
}

/// <summary>
/// Manages the lifecycle of the bundled offline Piper-plus TTS microservice (tts_server.py).
/// Binds strictly to loopback (127.0.0.1) with dynamic port selection and Win32 Job Object
/// orphan process prevention.
/// </summary>
public class PiperProcessManager : IDisposable
{
    private const int MaxRestartAttempts = 3;
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(4) };

    private readonly object _lock = new();
    private Process? _process;
    private ChildProcessJob? _job;
    private bool _isExpectedRunning;
    private int _crashCount;
    private bool _disposed;

    public int Port { get; private set; }
    public PiperProcessState State { get; private set; } = PiperProcessState.NotStarted;
    public string StatusMessage { get; private set; } = "Not started";

    public event Action<PiperProcessState, string>? StateChanged;

    public bool IsRunning => State == PiperProcessState.Running && _process != null && !_process.HasExited;

    public async Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (IsRunning)
                return true;

            if (State == PiperProcessState.Starting)
                return false;
        }

        return await StartAsync(cancellationToken);
    }

    private string _lastErrorOutput = string.Empty;

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        Process? oldProcess = null;
        lock (_lock)
        {
            if (IsRunning)
                return true;

            if (State == PiperProcessState.Starting && _process != null && !_process.HasExited)
                return false;

            _isExpectedRunning = false;
            oldProcess = _process;
            _process = null;

            SetState(PiperProcessState.Starting, "Starting offline Piper TTS engine...");
        }

        try
        {
            if (oldProcess != null)
            {
                try
                {
                    oldProcess.Exited -= OnProcessExited;
                    if (!oldProcess.HasExited)
                    {
                        oldProcess.Kill(entireProcessTree: true);
                        await oldProcess.WaitForExitAsync(cancellationToken);
                    }
                }
                catch { }
                finally
                {
                    oldProcess.Dispose();
                }
            }

            var pythonPath = ResolvePythonPath();
            if (string.IsNullOrEmpty(pythonPath) || !File.Exists(pythonPath))
            {
                SetState(PiperProcessState.Failed, "Bundled Python runtime not found at expected application path.");
                return false;
            }

            var scriptPath = ResolveScriptPath();
            if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
            {
                SetState(PiperProcessState.Failed, "tts_server.py not found in application resources.");
                return false;
            }

            Port = GetFreeLoopbackPort();
            var modelsDir = ResolveModelsDir();

            _job?.Dispose();
            _job = new ChildProcessJob();

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{scriptPath}\" {Port} \"{modelsDir}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? AppContext.BaseDirectory
            };
            startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            _lastErrorOutput = string.Empty;
            var proc = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            proc.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    Logger.Info($"[Piper stdout] {e.Data}");
                }
            };
            proc.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    _lastErrorOutput = e.Data;
                    Logger.Error($"[Piper stderr] {e.Data}");
                }
            };

            proc.Exited += OnProcessExited;

            lock (_lock)
            {
                _process = proc;
                _isExpectedRunning = true;
            }

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            _job.AddProcess(proc);

            Logger.Info($"Piper TTS child process started (PID: {proc.Id}) on port {Port}.");

            var isReady = await PollHealthAsync(cancellationToken);
            if (isReady)
            {
                lock (_lock)
                {
                    _crashCount = 0;
                    SetState(PiperProcessState.Running, $"Running on port {Port}");
                }
                return true;
            }
            else
            {
                var errDetail = !string.IsNullOrEmpty(_lastErrorOutput) ? $": {_lastErrorOutput}" : string.Empty;
                SetState(PiperProcessState.Failed, $"Health check timed out waiting for Piper TTS engine{errDetail}.");
                await StopAsync();
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Error starting Piper TTS child process.", ex);
            SetState(PiperProcessState.Failed, $"Startup failed: {ex.Message}");
            return false;
        }
    }

    public async Task StopAsync()
    {
        lock (_lock)
        {
            _isExpectedRunning = false;
        }

        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Error stopping Piper TTS process.", ex);
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _job?.Dispose();
            _job = null;
            SetState(PiperProcessState.Stopped, "Engine stopped.");
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync();
        await StartAsync(cancellationToken);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        bool shouldRestart = false;
        int currentCrashes = 0;

        lock (_lock)
        {
            if (!_isExpectedRunning)
                return;

            _crashCount++;
            currentCrashes = _crashCount;

            var detail = !string.IsNullOrEmpty(_lastErrorOutput)
                ? $" ({_lastErrorOutput})"
                : (_process != null ? $" (Exit code {_process.ExitCode})" : string.Empty);

            if (_crashCount <= MaxRestartAttempts)
            {
                shouldRestart = true;
                SetState(PiperProcessState.Starting, $"Piper engine stopped{detail}. Auto-restarting (attempt {_crashCount}/{MaxRestartAttempts})...");
            }
            else
            {
                _isExpectedRunning = false;
                SetState(PiperProcessState.Failed, $"Piper TTS engine failed after {MaxRestartAttempts} attempts{detail}.");
            }
        }

        if (shouldRestart)
        {
            _ = Task.Run(async () =>
            {
                var delayMs = (int)(Math.Pow(2, currentCrashes - 1) * 1000);
                await Task.Delay(delayMs);
                await StartAsync();
            });
        }
    }

    private async Task<bool> PollHealthAsync(CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(15);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var response = await _httpClient.GetAsync($"http://127.0.0.1:{Port}/health", cancellationToken);
                if (response.IsSuccessStatusCode)
                    return true;
            }
            catch
            {
                // Expected while python server boots
            }

            await Task.Delay(250, cancellationToken);
        }

        return false;
    }

    private void SetState(PiperProcessState state, string message)
    {
        State = state;
        StatusMessage = message;
        StateChanged?.Invoke(state, message);
    }

    private static int GetFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static string ResolveModelsDir()
    {
        var baseDir = AppContext.BaseDirectory;

        // 1. Check companion models directories alongside application
        var bundled1 = Path.Combine(baseDir, "Resources", "models", "piper");
        if (Directory.Exists(bundled1)) return bundled1;

        var bundled2 = Path.Combine(baseDir, "Resources", "PythonRuntime", "models");
        if (Directory.Exists(bundled2)) return bundled2;

        var bundled3 = Path.Combine(baseDir, "models", "piper");
        if (Directory.Exists(bundled3)) return bundled3;

        // 2. Fall back to local app data
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(localAppData, "GamingLiveTranslator", "models", "piper");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    private static string ResolvePythonPath()
    {
        var baseDir = AppContext.BaseDirectory;

        // 1. Check companion Resources/PythonRuntime folder (prefer venv Scripts\python.exe)
        var resPythonScripts = Path.Combine(baseDir, "Resources", "PythonRuntime", "Scripts", "python.exe");
        if (File.Exists(resPythonScripts)) return resPythonScripts;

        var resPython = Path.Combine(baseDir, "Resources", "PythonRuntime", "python.exe");
        if (File.Exists(resPython)) return resPython;

        // 2. Bundled application PythonRuntime (prefer venv Scripts\python.exe)
        var bundledScripts = Path.Combine(baseDir, "PythonRuntime", "Scripts", "python.exe");
        if (File.Exists(bundledScripts)) return bundledScripts;

        var bundled = Path.Combine(baseDir, "PythonRuntime", "python.exe");
        if (File.Exists(bundled)) return bundled;

        // 3. Local workspace virtual environment (test_env)
        var testEnv = Path.Combine(baseDir, "test_env", "Scripts", "python.exe");
        if (File.Exists(testEnv)) return testEnv;

        var parentTestEnv = Path.Combine(baseDir, "..", "..", "..", "test_env", "Scripts", "python.exe");
        if (File.Exists(parentTestEnv)) return Path.GetFullPath(parentTestEnv);

        var publishParentTestEnv = Path.Combine(baseDir, "..", "..", "..", "..", "test_env", "Scripts", "python.exe");
        if (File.Exists(publishParentTestEnv)) return Path.GetFullPath(publishParentTestEnv);

        // 4. Fallback: Check known workspace / publish paths on developer machine
        var devPublishEnv = @"D:\pubgpc ch\GamingLiveTranslator\bin\Release\net10.0-windows\win-x64\publish\Resources\PythonRuntime\Scripts\python.exe";
        if (File.Exists(devPublishEnv)) return devPublishEnv;

        var devTestEnv = @"D:\pubgpc ch\GamingLiveTranslator\test_env\Scripts\python.exe";
        if (File.Exists(devTestEnv)) return devTestEnv;

        return string.Empty;
    }

    private static string ResolveScriptPath()
    {
        var baseDir = AppContext.BaseDirectory;

        var script1 = Path.Combine(baseDir, "Resources", "PythonRuntime", "tts_server.py");
        if (File.Exists(script1)) return script1;

        var script2 = Path.Combine(baseDir, "..", "..", "..", "Resources", "PythonRuntime", "tts_server.py");
        if (File.Exists(script2)) return Path.GetFullPath(script2);

        var script3 = Path.Combine(baseDir, "..", "..", "..", "..", "Resources", "PythonRuntime", "tts_server.py");
        if (File.Exists(script3)) return Path.GetFullPath(script3);

        var script4 = Path.Combine(baseDir, "tts_server.py");
        if (File.Exists(script4)) return script4;

        // Fallback: Check known workspace paths
        var devPublishScript = @"D:\pubgpc ch\GamingLiveTranslator\bin\Release\net10.0-windows\win-x64\publish\Resources\PythonRuntime\tts_server.py";
        if (File.Exists(devPublishScript)) return devPublishScript;

        var devScript = @"D:\pubgpc ch\GamingLiveTranslator\Resources\PythonRuntime\tts_server.py";
        if (File.Exists(devScript)) return devScript;

        return string.Empty;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _ = StopAsync();
        }
        GC.SuppressFinalize(this);
    }
}
