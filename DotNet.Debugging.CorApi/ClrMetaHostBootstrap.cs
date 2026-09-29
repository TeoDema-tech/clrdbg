// Windows .NET Framework 4.x CLR MetaHost bootstrapping and interop.
// Based on SharpDbg / Lex Li netfx-support branch (MIT License), see ATTRIBUTIONs/ATTRIBUTIONS.md.
// Uses mscoree.dll (CLRCreateInstance) to discover installed Desktop CLR runtimes and obtain ICorDebug.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace DotNet.Debugging.CorApi;

/// <summary>
/// Well-known CLSIDs and IIDs for Windows Desktop CLR MetaHost and debugging.
/// </summary>
public static class ClrMetaHostGuids {
    /// <summary>
    /// CLSID_CLRMetaHost: {9280188D-0E8E-4867-B30C-7FA83884E8DE}
    /// Entry point for CLRCreateInstance to obtain ICLRMetaHost.
    /// </summary>
    public static readonly Guid CLSID_CLRMetaHost = new(0x9280188D, 0x0E8E, 0x4867, 0xB3, 0x0C, 0x7F, 0xA8, 0x38, 0x84, 0xE8, 0xDE);

    /// <summary>
    /// IID_ICLRMetaHost: {D332DB9E-B9B3-4125-8207-A14884F53216}
    /// </summary>
    public static readonly Guid IID_ICLRMetaHost = typeof(ICLRMetaHost).GUID;

    /// <summary>
    /// CLSID_CLRDebuggingLegacy: {DF8395B5-A4BA-450b-A77C-A9A47762C520}
    /// Passed to ICLRRuntimeInfo.GetInterface to obtain ICorDebug for Desktop CLR.
    /// </summary>
    public static readonly Guid CLSID_CLRDebuggingLegacy = new(0xDF8395B5, 0xA4BA, 0x450b, 0xA7, 0x7C, 0xA9, 0xA4, 0x77, 0x62, 0xC5, 0x20);

    /// <summary>
    /// IID_ICorDebug: {3D6F5F61-7538-11D3-8D5B-00104B35E7EF}
    /// </summary>
    public static readonly Guid IID_ICorDebug = typeof(ICorDebug).GUID;

    /// <summary>
    /// IID_ICLRRuntimeInfo: {BD39D1D2-BA2F-486A-89B0-B4B0CB466891}
    /// </summary>
    public static readonly Guid IID_ICLRRuntimeInfo = typeof(ICLRRuntimeInfo).GUID;
}

/// <summary>
/// Bootstrap helpers for Windows .NET Framework 4.x (Desktop CLR) debugging.
/// </summary>
public static partial class ClrMetaHostBootstrap {
    [LibraryImport("mscoree.dll")]
    public static partial int CLRCreateInstance(
        in Guid clsid,
        in Guid riid,
        [MarshalUsing(typeof(UniqueComInterfaceMarshaller<object>))] out object? ppInterface);

    /// <summary>
    /// Obtains an instance of the ICLRMetaHost interface from mscoree.dll on Windows.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static ICLRMetaHost CreateMetaHost() {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("CLRMetaHost is only available on Windows for .NET Framework debugging.");

        var clsid = ClrMetaHostGuids.CLSID_CLRMetaHost;
        var riid = ClrMetaHostGuids.IID_ICLRMetaHost;
        var hr = CLRCreateInstance(in clsid, in riid, out var obj);
        Marshal.ThrowExceptionForHR(hr);
        return (ICLRMetaHost)obj!;
    }

    /// <summary>
    /// Reads the required CLR version string directly from the target executable's PE header.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static string GetVersionFromFile(this ICLRMetaHost metaHost, string filePath) {
        uint bufferLength = 260;
        var buffer = new char[bufferLength];
        var hr = metaHost.TryGetVersionFromFile(filePath, buffer, ref bufferLength);
        if (hr != Cor.S_OK) {
            buffer = new char[bufferLength];
            hr = metaHost.TryGetVersionFromFile(filePath, buffer, ref bufferLength);
            Marshal.ThrowExceptionForHR(hr);
        }
        return new string(buffer, 0, (int)bufferLength).TrimEnd('\0');
    }

    /// <summary>
    /// Gets the ICLRRuntimeInfo interface corresponding to the specified CLR version string (e.g. "v4.0.30319").
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static ICLRRuntimeInfo GetRuntime(this ICLRMetaHost metaHost, string version) {
        var riid = ClrMetaHostGuids.IID_ICLRRuntimeInfo;
        var hr = metaHost.TryGetRuntime(version, ref riid, out var obj);
        Marshal.ThrowExceptionForHR(hr);
        return (ICLRRuntimeInfo)obj!;
    }

    /// <summary>
    /// Obtains an ICorDebug debugging interface from an ICLRRuntimeInfo instance.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static ICorDebug GetCorDebug(this ICLRRuntimeInfo runtimeInfo) {
        var clsid = ClrMetaHostGuids.CLSID_CLRDebuggingLegacy;
        var riid = ClrMetaHostGuids.IID_ICorDebug;
        var hr = runtimeInfo.TryGetInterface(ref clsid, ref riid, out var obj);
        Marshal.ThrowExceptionForHR(hr);
        return (ICorDebug)obj!;
    }

    /// <summary>
    /// Initializes an ICorDebug instance configured for a Desktop CLR executable on Windows.
    /// Reads the runtime version from the target executable and binds the matching runtime.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static ICorDebug CreateDesktopCorDebug(string executablePath) {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(".NET Framework (Desktop CLR) debugging is only supported on Windows.");

        var metaHost = CreateMetaHost();
        var version = metaHost.GetVersionFromFile(executablePath);
        var runtime = metaHost.GetRuntime(version);
        return runtime.GetCorDebug();
    }

    /// <summary>
    /// Connects to a running .NET Framework process and obtains its ICorDebug debugging interface.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static ICorDebug AttachToDesktopClr(int processId) {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(".NET Framework (Desktop CLR) debugging is only supported on Windows.");

        // First attempt: Enumerate CLRs via DbgShim
        try {
            var hr = DbgShim.EnumerateCLRs((uint)processId, out var handles, out var paths, out var count);
            if (hr == Cor.S_OK && count > 0) {
                try {
                    unsafe {
                        var strPtrs = (nint*)paths;
                        var runtimePath = Marshal.PtrToStringUni(*strPtrs);
                        if (!string.IsNullOrEmpty(runtimePath)) {
                            var versionBuffer = new char[260];
                            var vhr = DbgShim.CreateVersionStringFromModule((uint)processId, runtimePath, versionBuffer, (uint)versionBuffer.Length, out var actualLength);
                            if (vhr == Cor.S_OK) {
                                var versionStr = new string(versionBuffer, 0, (int)actualLength).TrimEnd('\0');
                                var dhr = DbgShim.CreateDebuggingInterfaceFromVersionEx((int)CorDebugInterfaceVersion.CorDebugVersion_4_0, versionStr, out var ppCordb);
                                if (dhr == Cor.S_OK && ppCordb != null)
                                    return ppCordb;
                            }
                        }
                    }
                }
                finally {
                    _ = DbgShim.CloseCLREnumeration(handles, paths, count);
                }
            }
        }
        catch {
            // Fall back to MetaHost below
        }

        // Fall back to MetaHost default v4.0.30319 runtime
        var metaHost = CreateMetaHost();
        var defaultRuntime = metaHost.GetRuntime("v4.0.30319");
        return defaultRuntime.GetCorDebug();
    }
}
