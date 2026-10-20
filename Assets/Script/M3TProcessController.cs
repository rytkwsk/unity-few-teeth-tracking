using System.Collections;
using System.Collections.Generic;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

public class M3TProcessController : MonoBehaviour
{
    [Header("Python Tracker")]
    [Tooltip("空欄なら M3T_PYTHON_EXE、またはConda環境のpythonを使用します")]
    public string pythonExe = "";
    [Tooltip("tracker を OpenCV ウィンドウなしで起動する（M3T_HEADLESS=1）。表示系 50 ms/フレームを省く")]
    public bool headlessTracker = true;
    [Tooltip("相対パスはUnityプロジェクトのルートを基準に解決します")]
    public string workingDirectory = @"..\few_teeth_tracking";
    [Tooltip("相対パスはworkingDirectoryを基準に解決します")]
    public string scriptPath = "tracker.py";
    public bool useCondaActivate = true;
    [Tooltip("空欄なら M3T_CONDA_BAT、一般的なインストール先、PATH上のcondaの順で探索します")]
    public string condaBat = "";
    public string condaEnvironmentName = "pym3t";
    public bool startOnPlay = false;
    public bool showConsoleWindow = false;
    public bool startTrackingInPython = true;
    public string commandHost = "127.0.0.1";
    public int commandPort = 5006;

    Process process;
    string lastStatus = "Stopped";
    string lastError = "";
    string logPath = "";
    string resolvedPythonExe = "";
    string resolvedWorkingDirectory = "";
    string resolvedScriptPath = "";
    string resolvedCondaLauncher = "";
    readonly object logLock = new object();

    // Windows は cmd.exe + conda.bat activate で起動する。macOS / Linux は conda 環境の python を直接起動する
    // （activate 用のシェルが無く、GUI から起動した Unity にはシェルの環境変数も渡らないため）。
    static bool IsWindowsHost => Application.platform == RuntimePlatform.WindowsEditor
                                 || Application.platform == RuntimePlatform.WindowsPlayer;
    bool UseCondaActivate => useCondaActivate && IsWindowsHost;

    public bool IsRunning => process != null && !process.HasExited;
    public string LastStatus => lastStatus;
    public string LastError => lastError;
    public string LogPath => logPath;

    void Start()
    {
        if (startOnPlay)
            StartTracking();
    }

    public void StartTracking()
    {
        if (process != null && !process.HasExited)
            return;

        if (process != null)
        {
            process.Dispose();
            process = null;
        }

        lastError = "";
        lastStatus = "Starting";
        logPath = Path.Combine(Application.persistentDataPath, "m3t_tracker.log");

        ResolveRuntimePaths();

        if (UseCondaActivate && string.IsNullOrEmpty(resolvedCondaLauncher))
        {
            ReportError("Conda was not found. Set M3T_CONDA_BAT or condaBat in the Inspector.");
            return;
        }

        if (!UseCondaActivate && !File.Exists(resolvedPythonExe))
        {
            ReportError(IsWindowsHost
                ? $"M3T python executable was not found: {resolvedPythonExe}"
                : $"M3T python was not found: '{resolvedPythonExe}'. Run tools/setup_mac.sh in few_teeth_tracking, "
                  + "or set M3T_PYTHON_EXE in few_teeth_tracking/m3t_local.env");
            return;
        }

        if (!Directory.Exists(resolvedWorkingDirectory))
        {
            ReportError($"M3T working directory was not found: {resolvedWorkingDirectory}");
            return;
        }

        if (!File.Exists(resolvedScriptPath))
        {
            ReportError($"M3T tracker script was not found: {resolvedScriptPath}");
            return;
        }

        var info = BuildProcessStartInfo();

        var startupNote = UseCondaActivate
            ? $"conda env={condaEnvironmentName} ({resolvedCondaLauncher})"
            : $"python={resolvedPythonExe}";
        var pym3tDir = info.EnvironmentVariables["PYM3T_DIR"];
        if (string.IsNullOrEmpty(pym3tDir))
            pym3tDir = "(未設定: ../pym3t を使用)";
        File.WriteAllText(logPath,
            $"Starting M3T tracker at {DateTime.Now}\n{startupNote}\nPYM3T_DIR={pym3tDir}\nscript={resolvedScriptPath}\n");

        try
        {
            process = Process.Start(info);
            if (process == null)
            {
                ReportError("Failed to start M3T tracker process.");
                return;
            }

            process.EnableRaisingEvents = true;
            process.OutputDataReceived += OnTrackerOutput;
            process.ErrorDataReceived += OnTrackerError;
            process.Exited += OnTrackerExited;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            lastStatus = "Running";
            UnityEngine.Debug.Log($"M3T tracker process started ({startupNote}, PYM3T_DIR={pym3tDir}). Log: {logPath}");

            if (startTrackingInPython)
                StartCoroutine(SendCommandRepeatedly("start", 30, 0.1f));
        }
        catch (Win32Exception e)
        {
            ReportError($"Failed to start M3T tracker process: {e.Message}");
        }
        catch (InvalidOperationException e)
        {
            ReportError($"Failed to start M3T tracker process: {e.Message}");
        }
        catch (Exception e)
        {
            ReportError($"Failed to start M3T tracker process: {e.Message}");
        }
    }

    ProcessStartInfo BuildProcessStartInfo()
    {
        ProcessStartInfo info;

        if (UseCondaActivate)
        {
            info = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/d /c \"\"{resolvedCondaLauncher}\" activate {condaEnvironmentName} && python -u \"{resolvedScriptPath}\"\"",
                WorkingDirectory = resolvedWorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = !showConsoleWindow,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // tracker 側はパイプ出力時に UTF-8 で書く（tracker_four_group.py 先頭）。既定の cp932 で読むと文字化けする
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
        }
        else
        {
            info = new ProcessStartInfo
            {
                FileName = resolvedPythonExe,
                Arguments = $"-u \"{resolvedScriptPath}\"",
                WorkingDirectory = resolvedWorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = !showConsoleWindow,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // tracker 側はパイプ出力時に UTF-8 で書く（tracker_four_group.py 先頭）。既定の cp932 で読むと文字化けする
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            AddCondaEnvironmentPath(info);
        }

        // tracker 側は M3T_HEADLESS=1 で OpenCV ウィンドウ（waitKey 32 ms + HUD 描画 18 ms）を省略する。
        // Unity に映像と品質バーがあるので不要。デバッグで OpenCV 窓が欲しいときは headlessTracker を外す。
        if (headlessTracker)
            info.EnvironmentVariables["M3T_HEADLESS"] = "1";

        ForwardUserEnvironment(info);

        return info;
    }

    // Unity（と Unity Hub）は起動時点の環境変数しか持たない。setx などで後から登録した
    // M3T_* / PYM3T_DIR は、Unity Hub ごと再起動しないと子プロセスに渡らず、
    // 「conda 環境 pym3t が見つからない」「Debug 版 pyd を読んで重い」になる。
    // ここでレジストリ（ユーザー → マシン）の値を直接読んで補うことで、再起動なしで反映させる。
    static readonly string[] ForwardedPrefixes = { "M3T_", "PYM3T_" };

    static void ForwardUserEnvironment(ProcessStartInfo info)
    {
        // few_teeth_tracking/m3t_local.env（PC ごとの設定。Mac ではこれが唯一の手段）
        foreach (var pair in localEnv)
        {
            if (!IsForwardedKey(pair.Key) || string.IsNullOrEmpty(pair.Value))
                continue;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(pair.Key)))
                continue; // プロセス環境を優先
            info.EnvironmentVariables[pair.Key] = pair.Value;
        }
        if (!IsWindowsHost)
            return; // ユーザー／マシン環境変数（レジストリ）は Windows だけ
        foreach (var target in new[] { EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
        {
            IDictionary variables;
            try
            {
                variables = Environment.GetEnvironmentVariables(target);
            }
            catch (Exception)
            {
                continue;
            }
            foreach (DictionaryEntry entry in variables)
            {
                var key = entry.Key as string;
                var value = entry.Value as string;
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                    continue;
                if (!IsForwardedKey(key))
                    continue;
                if (!string.IsNullOrEmpty(info.EnvironmentVariables[key]))
                    continue; // プロセス環境（または上の優先度）に既にある
                info.EnvironmentVariables[key] = value;
            }
        }
    }

    static bool IsForwardedKey(string key)
    {
        foreach (var prefix in ForwardedPrefixes)
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    // プロセス環境 → m3t_local.env → ユーザー環境変数 → マシン環境変数 の順で探す（上記と同じ理由）。
    static string GetSetting(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
            return value;
        if (localEnv.TryGetValue(name, out value) && !string.IsNullOrWhiteSpace(value))
            return value;
        if (!IsWindowsHost)
            return null;
        foreach (var target in new[] { EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
        {
            try
            {
                value = Environment.GetEnvironmentVariable(name, target);
            }
            catch (Exception)
            {
                continue;
            }
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    // few_teeth_tracking/m3t_local.env（KEY=VALUE、# はコメント、gitignore）。tracker 側も同じファイルを読む（camera_io.py）。
    static Dictionary<string, string> localEnv = new Dictionary<string, string>();
    string localEnvPath = "";

    static Dictionary<string, string> LoadLocalEnv(string path)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return result;
        try
        {
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim().Trim('"', '\'');
                if (key.Length > 0)
                    result[key] = ExpandHome(value);
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"Failed to read {path}: {e.Message}");
        }
        return result;
    }

    static string ExpandHome(string value)
    {
        if (string.IsNullOrEmpty(value) || !(value == "~" || value.StartsWith("~/")))
            return value;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return value == "~" ? home : Path.Combine(home, value.Substring(2));
    }

    // macOS / Linux: conda 環境 envName の python を、よくあるインストール先から探す
    static string FindCondaEnvPython(string envName)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[]
        {
            Path.Combine(home, "miniforge3"), Path.Combine(home, "miniconda3"), Path.Combine(home, "anaconda3"),
            Path.Combine(home, "mambaforge"), "/opt/homebrew/Caskroom/miniforge/base", "/usr/local/Caskroom/miniforge/base",
            "/opt/homebrew/Caskroom/miniconda/base", "/opt/miniconda3", "/opt/anaconda3"
        };
        foreach (var root in roots)
        {
            var candidate = Path.Combine(root, "envs", envName, "bin", "python");
            if (File.Exists(candidate))
                return candidate;
        }
        return "";
    }

    public string ResolvedCondaEnvironment => condaEnvironmentName;
    public string ResolvedPym3tDir => GetSetting("PYM3T_DIR") ?? "(未設定: ../pym3t)";

    void ResolveRuntimePaths()
    {
        var projectRoot = Directory.GetParent(Application.dataPath).FullName;
        resolvedWorkingDirectory = ResolvePath(projectRoot, workingDirectory);
        resolvedScriptPath = ResolvePath(resolvedWorkingDirectory, scriptPath);
        localEnvPath = Path.Combine(resolvedWorkingDirectory, "m3t_local.env");
        localEnv = LoadLocalEnv(localEnvPath);

        var pythonFromEnvironment = GetSetting("M3T_PYTHON_EXE");
        resolvedPythonExe = ResolvePath(projectRoot,
            !string.IsNullOrWhiteSpace(pythonFromEnvironment) ? pythonFromEnvironment : pythonExe);

        var condaEnvironment = GetSetting("M3T_CONDA_ENV");
        if (!string.IsNullOrWhiteSpace(condaEnvironment))
            condaEnvironmentName = condaEnvironment.Trim();

        var condaFromEnvironment = GetSetting("M3T_CONDA_BAT");
        var configuredConda = !string.IsNullOrWhiteSpace(condaFromEnvironment)
            ? condaFromEnvironment : condaBat;
        resolvedCondaLauncher = IsWindowsHost ? FindCondaLauncher(configuredConda) : "";

        if (!IsWindowsHost && (string.IsNullOrEmpty(resolvedPythonExe) || !File.Exists(resolvedPythonExe)))
            resolvedPythonExe = FindCondaEnvPython(condaEnvironmentName);
    }

    static string ResolvePath(string baseDirectory, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var expanded = ExpandHome(Environment.ExpandEnvironmentVariables(value.Trim()));
        return Path.GetFullPath(Path.IsPathRooted(expanded)
            ? expanded : Path.Combine(baseDirectory, expanded));
    }

    static string FindCondaLauncher(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configured.Trim());
            if (File.Exists(expanded))
                return Path.GetFullPath(expanded);
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(userProfile, "anaconda3", "condabin", "conda.bat"),
            Path.Combine(userProfile, "miniconda3", "condabin", "conda.bat"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "anaconda3", "condabin", "conda.bat")
        };
        foreach (var candidate in candidates)
            if (File.Exists(candidate))
                return candidate;

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in pathValue.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;
            var candidate = Path.Combine(directory.Trim(), "conda.bat");
            if (File.Exists(candidate))
                return candidate;
        }
        return "";
    }

    public void StopTracking()
    {
        if (process == null)
            return;

        try
        {
            var processId = process.Id;
            SendCommandToPython("stop");
            if (!process.HasExited)
                KillProcessTree(processId);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception e)
        {
            UnityEngine.Debug.LogWarning($"Failed to stop M3T tracker process: {e.Message}");
        }
        finally
        {
            process.OutputDataReceived -= OnTrackerOutput;
            process.ErrorDataReceived -= OnTrackerError;
            process.Exited -= OnTrackerExited;
            process.Dispose();
            process = null;
            lastStatus = "Stopped";
            UnityEngine.Debug.Log("M3T tracker process stopped.");
        }
    }

    void KillProcessTree(int processId)
    {
        if (!IsWindowsHost)
        {
            try
            {
                if (process != null && !process.HasExited && !process.WaitForExit(1500))
                    process.Kill();   // 'stop' コマンドで終わらなければ強制終了（子プロセスは作らない）
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"Failed to kill M3T process: {e.Message}");
            }
            return;
        }
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/PID {processId} /T /F",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // tracker 側はパイプ出力時に UTF-8 で書く（tracker_four_group.py 先頭）。既定の cp932 で読むと文字化けする
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using (var killer = Process.Start(info))
            {
                killer?.WaitForExit(2000);
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"Failed to kill M3T process tree: {e.Message}");
            try
            {
                process?.Kill();
            }
            catch (Exception)
            {
            }
        }
    }

    IEnumerator SendCommandRepeatedly(string command, int count, float intervalSeconds)
    {
        for (var i = 0; i < count; i++)
        {
            if (process == null || process.HasExited)
                yield break;

            SendCommandToPython(command);
            yield return new WaitForSeconds(intervalSeconds);
        }
    }

    public void SendCommandToPython(string command)
    {
        try
        {
            using (var client = new UdpClient())
            {
                var data = Encoding.UTF8.GetBytes(command);
                client.Send(data, data.Length, commandHost, commandPort);
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning($"Failed to send M3T command '{command}': {e.Message}");
        }
    }

    void AddCondaEnvironmentPath(ProcessStartInfo info)
    {
        var envRoot = Directory.GetParent(resolvedPythonExe)?.FullName;
        if (string.IsNullOrEmpty(envRoot))
            return;

        var paths = new[]
        {
            envRoot,
            Path.Combine(envRoot, "Library", "mingw-w64", "bin"),
            Path.Combine(envRoot, "Library", "usr", "bin"),
            Path.Combine(envRoot, "Library", "bin"),
            Path.Combine(envRoot, "Scripts"),
            Path.Combine(envRoot, "DLLs"),
            Path.Combine(envRoot, "bin")
        };

        var currentPath = info.EnvironmentVariables["PATH"];
        info.EnvironmentVariables["PATH"] = string.Join(Path.PathSeparator.ToString(), paths) + Path.PathSeparator + currentPath;
        info.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
    }

    void OnTrackerOutput(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
            AppendLog(e.Data);
    }

    void OnTrackerError(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
            return;

        lastError = e.Data;
        AppendLog(e.Data);
    }

    void OnTrackerExited(object sender, EventArgs e)
    {
        var exitCode = "unknown";
        try
        {
            exitCode = process != null ? process.ExitCode.ToString() : "unknown";
        }
        catch (Exception)
        {
        }

        lastStatus = $"Exited ({exitCode})";
        AppendLog($"M3T tracker process exited with code {exitCode}.");
    }

    void AppendLog(string line)
    {
        // stdout and stderr are read on separate threads (OutputDataReceived /
        // ErrorDataReceived), so without a lock two callbacks can open the
        // same file for append at the same instant and hit a Windows sharing
        // violation.
        lock (logLock)
        {
            try
            {
                File.AppendAllText(logPath, line + "\n");
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"Failed to write M3T tracker log: {e.Message}");
            }
        }
    }

    void ReportError(string message)
    {
        lastStatus = "Error";
        lastError = message;
        UnityEngine.Debug.LogError(message);
    }

    void OnDestroy()
    {
        StopTracking();
    }

    void OnApplicationQuit()
    {
        StopTracking();
    }
}
