// Open-source IL decompilation service using ICSharpCode.Decompiler.
// Generates Portable PDBs on-the-fly and caches decompiled C# source for DAP source requests.

using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotNet.Debugging.Engine.Logging;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

namespace DotNet.Debugging.Engine.Decompilation;

/// <summary>
/// Manages on-the-fly decompilation of .NET assemblies when PDB symbols are missing,
/// producing Portable PDBs with embedded sequence points and serving decompiled C# source.
/// </summary>
public class DecompilationService {
    private static readonly ConcurrentDictionary<string, string> decompiledSourceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string cacheRoot;

    public DecompilationService(string? cacheRoot = null) {
        this.cacheRoot = cacheRoot ?? Path.Combine(AppContext.BaseDirectory, "DecompiledCache");
    }

    /// <summary>
    /// Attempts to generate or retrieve a cached Portable PDB for the specified assembly.
    /// Returns the absolute path to the generated PDB, or null if decompilation failed.
    /// </summary>
    public string? GetOrGeneratePdb(string assemblyPath, IEnumerable<string> allLoadedModulePaths) {
        if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
            return null;

        try {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return null;

            var metadataReader = peReader.GetMetadataReader();
            var assemblyDef = metadataReader.GetAssemblyDefinition();
            var assemblyName = metadataReader.GetString(assemblyDef.Name);
            var mvid = metadataReader.GetGuid(metadataReader.GetModuleDefinition().Mvid);

            var assemblyCacheDir = Path.Combine(cacheRoot, assemblyName, mvid.ToString("N"));
            var targetPdbPath = Path.Combine(assemblyCacheDir, $"{Path.GetFileNameWithoutExtension(assemblyPath)}.pdb");

            if (File.Exists(targetPdbPath)) {
                DebuggerLoggingService.LogMessage($"Found cached decompiled PDB for {assemblyName}: {targetPdbPath}");
                return targetPdbPath;
            }

            DebuggerLoggingService.LogMessage($"Decompiling {assemblyName} on-the-fly to generate PDB...");
            Directory.CreateDirectory(assemblyCacheDir);

            var resolver = new DebuggingAssemblyResolver(allLoadedModulePaths.ToList());
            using var peFile = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage);
            var settings = new DecompilerSettings();
            var typeSystem = new DecompilerTypeSystem(peFile, resolver, settings);

            var tempPdbPath = targetPdbPath + ".tmp";
            Dictionary<string, string> sources;
            using (var outputStream = File.Create(tempPdbPath)) {
                sources = PortablePdbWriter2.WritePdb(peFile, typeSystem, settings, outputStream, noLogo: false);
            }

            if (File.Exists(targetPdbPath))
                File.Delete(targetPdbPath);
            File.Move(tempPdbPath, targetPdbPath);

            // Populate source cache so SourceHandler can serve them by document path
            foreach (var (key, source) in sources) {
                decompiledSourceCache[key] = source;
            }

            DebuggerLoggingService.LogMessage($"Successfully generated decompiled PDB: {targetPdbPath} ({sources.Count} types)");
            return targetPdbPath;
        }
        catch (Exception ex) {
            DebuggerLoggingService.LogError($"Failed to decompile {assemblyPath}", ex);
            return null;
        }
    }

    /// <summary>
    /// Looks up decompiled source content by its document path key.
    /// </summary>
    public static string? TryGetSourceContent(string documentPath) {
        if (decompiledSourceCache.TryGetValue(documentPath, out var content))
            return content;
        return null;
    }
}
