using System.IO;
using System.Runtime.InteropServices;

namespace DSPlayer.Services.Mpv;

/// <summary>
/// Minimal P/Invoke surface for libmpv client API (client.h).
/// Supports both modern libmpv-2.dll and legacy mpv-1.dll names.
/// </summary>
internal static class MpvNative
{
    private static IntPtr _libraryHandle;
    private static string? _loadedPath;

    public static bool IsLoaded => _libraryHandle != IntPtr.Zero;
    public static string? LoadedPath => _loadedPath;

    public static CreateDelegate Create = null!;
    public static InitializeDelegate Initialize = null!;
    public static TerminateDestroyDelegate TerminateDestroy = null!;
    public static CommandDelegate Command = null!;
    public static CommandStringDelegate CommandString = null!;
    public static SetOptionStringDelegate SetOptionString = null!;
    public static SetPropertyStringDelegate SetPropertyString = null!;
    public static GetPropertyStringDelegate GetPropertyString = null!;
    public static FreeDelegate Free = null!;
    public static ErrorStringDelegate ErrorString = null!;
    public static SetWakeupCallbackDelegate SetWakeupCallback = null!;
    public static WaitEventDelegate WaitEvent = null!;
    public static ObservePropertyDelegate ObserveProperty = null!;
    public static RequestLogMessagesDelegate RequestLogMessages = null!;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr CreateDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int InitializeDelegate(IntPtr mpv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void TerminateDestroyDelegate(IntPtr mpv);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int CommandDelegate(IntPtr mpv, IntPtr args);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int CommandStringDelegate(IntPtr mpv, [MarshalAs(UnmanagedType.LPUTF8Str)] string args);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int SetOptionStringDelegate(
        IntPtr mpv,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int SetPropertyStringDelegate(
        IntPtr mpv,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr GetPropertyStringDelegate(
        IntPtr mpv,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void FreeDelegate(IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr ErrorStringDelegate(int error);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void WakeupCallback(IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void SetWakeupCallbackDelegate(IntPtr mpv, WakeupCallback cb, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr WaitEventDelegate(IntPtr mpv, double timeout);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int ObservePropertyDelegate(
        IntPtr mpv,
        ulong replyUserdata,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int format);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int RequestLogMessagesDelegate(
        IntPtr mpv,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string minLevel);

    public static void Load(string? preferredPath = null)
    {
        if (IsLoaded)
            return;

        var candidates = BuildCandidatePaths(preferredPath);
        Exception? lastError = null;

        foreach (var path in candidates)
        {
            if (!File.Exists(path) && !IsBareFileName(path))
                continue;

            try
            {
                var handle = NativeLibrary.Load(path);
                Bind(handle);
                _libraryHandle = handle;
                _loadedPath = path;
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        foreach (var name in new[] { "libmpv-2.dll", "mpv-2.dll", "mpv-1.dll", "libmpv-1.dll" })
        {
            try
            {
                var handle = NativeLibrary.Load(name);
                Bind(handle);
                _libraryHandle = handle;
                _loadedPath = name;
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new FileNotFoundException(
            "libmpv が見つかりません。scripts/fetch-libmpv.ps1 を実行するか、" +
            "libmpv-2.dll (または mpv-1.dll) を出力先の lib フォルダへ配置してください。",
            lastError);
    }

    private static bool IsBareFileName(string path) =>
        path.IndexOf(Path.DirectorySeparatorChar) < 0 &&
        path.IndexOf(Path.AltDirectorySeparatorChar) < 0;

    private static IEnumerable<string> BuildCandidatePaths(string? preferredPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath))
            yield return preferredPath;

        var baseDir = AppContext.BaseDirectory;
        var names = new[] { "libmpv-2.dll", "mpv-2.dll", "mpv-1.dll", "libmpv-1.dll" };

        foreach (var name in names)
        {
            yield return Path.Combine(baseDir, name);
            yield return Path.Combine(baseDir, "lib", name);
            yield return Path.Combine(baseDir, "runtimes", "win-x64", "native", name);
        }

        var repoLib = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "lib"));
        foreach (var name in names)
            yield return Path.Combine(repoLib, name);
    }

    private static void Bind(IntPtr handle)
    {
        Create = Get<CreateDelegate>(handle, "mpv_create");
        Initialize = Get<InitializeDelegate>(handle, "mpv_initialize");
        TerminateDestroy = Get<TerminateDestroyDelegate>(handle, "mpv_terminate_destroy");
        Command = Get<CommandDelegate>(handle, "mpv_command");
        CommandString = Get<CommandStringDelegate>(handle, "mpv_command_string");
        SetOptionString = Get<SetOptionStringDelegate>(handle, "mpv_set_option_string");
        SetPropertyString = Get<SetPropertyStringDelegate>(handle, "mpv_set_property_string");
        GetPropertyString = Get<GetPropertyStringDelegate>(handle, "mpv_get_property_string");
        Free = Get<FreeDelegate>(handle, "mpv_free");
        ErrorString = Get<ErrorStringDelegate>(handle, "mpv_error_string");
        SetWakeupCallback = Get<SetWakeupCallbackDelegate>(handle, "mpv_set_wakeup_callback");
        WaitEvent = Get<WaitEventDelegate>(handle, "mpv_wait_event");
        ObserveProperty = Get<ObservePropertyDelegate>(handle, "mpv_observe_property");
        RequestLogMessages = Get<RequestLogMessagesDelegate>(handle, "mpv_request_log_messages");
    }

    private static T Get<T>(IntPtr handle, string name) where T : Delegate
    {
        var ptr = NativeLibrary.GetExport(handle, name);
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    public static string GetError(int error)
    {
        if (!IsLoaded)
            return $"mpv error {error}";

        var ptr = ErrorString(error);
        return ptr == IntPtr.Zero ? $"mpv error {error}" : Marshal.PtrToStringUTF8(ptr) ?? $"mpv error {error}";
    }

    public static void Check(int error, string operation)
    {
        if (error < 0)
            throw new InvalidOperationException($"{operation} failed: {GetError(error)} ({error})");
    }

    public const int MpvFormatNone = 0;
    public const int MpvFormatString = 1;
    public const int MpvFormatFlag = 3;
    public const int MpvFormatInt64 = 4;
    public const int MpvFormatDouble = 5;

    public const int MpvEventNone = 0;
    public const int MpvEventShutdown = 1;
    public const int MpvEventLogMessage = 2;
    public const int MpvEventGetPropertyReply = 3;
    public const int MpvEventSetPropertyReply = 4;
    public const int MpvEventCommandReply = 5;
    public const int MpvEventStartFile = 6;
    public const int MpvEventEndFile = 7;
    public const int MpvEventFileLoaded = 8;
    public const int MpvEventIdle = 11;
    public const int MpvEventTick = 14;
    public const int MpvEventPropertyChange = 22;

    [StructLayout(LayoutKind.Sequential)]
    public struct MpvEvent
    {
        public int EventId;
        public int Error;
        public ulong ReplyUserdata;
        public IntPtr Data;
    }
}
