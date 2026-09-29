// Based on SharpDbg / Lex Li netfx-support branch (MIT License), see ATTRIBUTIONs/ATTRIBUTIONS.md.
// Resolves referenced assemblies for ICSharpCode.Decompiler using loaded debugger modules.

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler.Metadata;

namespace DotNet.Debugging.Engine.Decompilation;

/// <summary>
/// An assembly resolver for ICSharpCode.Decompiler that inspects currently loaded modules
/// in the debuggee process to resolve references during on-the-fly decompilation.
/// </summary>
internal sealed class DebuggingAssemblyResolver : IAssemblyResolver {
    private readonly List<string> modulePaths;
    private readonly record struct AssemblyIdentity(string Name, Version Version, ImmutableArray<byte> PublicKeyToken);

    public DebuggingAssemblyResolver(List<string> modulePaths) {
        this.modulePaths = modulePaths ?? new List<string>();
    }

    public Task<MetadataFile?> ResolveAsync(IAssemblyReference name) => Task.FromResult(Resolve(name));
    public Task<MetadataFile?> ResolveModuleAsync(MetadataFile mainModule, string moduleName) => Task.FromResult(ResolveModule(mainModule, moduleName));
    public IDisposable BeginSnapshot() => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }

    public MetadataFile? Resolve(IAssemblyReference name) {
        string? exactMatch = null;
        string? highestVersionMatch = null;
        Version? highestVersion = null;

        foreach (var path in modulePaths) {
            if (!File.Exists(path))
                continue;

            var identity = TryReadAssemblyIdentity(path);
            if (identity is null)
                continue;

            if (!string.Equals(identity.Value.Name, name.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            var requestedToken = name.PublicKeyToken != null ? ImmutableArray.Create(name.PublicKeyToken) : ImmutableArray<byte>.Empty;
            var identityToken = identity.Value.PublicKeyToken;

            if (identity.Value.Version == name.Version && identityToken.SequenceEqual(requestedToken)) {
                exactMatch = path;
                break;
            }

            if (highestVersion is null || identity.Value.Version > highestVersion) {
                highestVersion = identity.Value.Version;
                highestVersionMatch = path;
            }
        }

        var chosen = exactMatch ?? highestVersionMatch;
        if (chosen is null)
            return null;

        return new PEFile(chosen, PEStreamOptions.PrefetchMetadata);
    }

    public MetadataFile? ResolveModule(MetadataFile mainModule, string moduleName) {
        // Multi-module assemblies: look for the module file next to the main module.
        var baseDirectory = Path.GetDirectoryName(mainModule.FileName);
        if (baseDirectory is null)
            return null;

        var moduleFileName = Path.Combine(baseDirectory, moduleName);
        if (!File.Exists(moduleFileName))
            return null;

        return new PEFile(moduleFileName, PEStreamOptions.PrefetchMetadata);
    }

    private static AssemblyIdentity? TryReadAssemblyIdentity(string path) {
        try {
            using var peReader = new PEReader(File.OpenRead(path));
            if (!peReader.HasMetadata)
                return null;

            var metadataReader = peReader.GetMetadataReader();
            var assemblyDef = metadataReader.GetAssemblyDefinition();

            var name = metadataReader.GetString(assemblyDef.Name);
            var version = assemblyDef.Version;
            var publicKeyToken = ComputePublicKeyToken(metadataReader, assemblyDef);

            return new AssemblyIdentity(name, version, publicKeyToken);
        }
        catch {
            return null;
        }
    }

    private static ImmutableArray<byte> ComputePublicKeyToken(MetadataReader reader, AssemblyDefinition assemblyDef) {
        var publicKeyOrToken = reader.GetBlobBytes(assemblyDef.PublicKey);
        if (publicKeyOrToken.Length == 0)
            return ImmutableArray<byte>.Empty;

        // If this is a full public key (not already a token), hash it down to an 8-byte token.
        if ((assemblyDef.Flags & System.Reflection.AssemblyFlags.PublicKey) != 0 && publicKeyOrToken.Length > 8) {
#pragma warning disable CA5350 // SHA1 is standard for ECMA CLI public key token computation
            var hash = System.Security.Cryptography.SHA1.HashData(publicKeyOrToken);
#pragma warning restore CA5350
            var token = new byte[8];
            for (int i = 0; i < 8; i++)
                token[i] = hash[hash.Length - 1 - i];
            return ImmutableArray.Create(token);
        }

        return ImmutableArray.Create(publicKeyOrToken);
    }
}
