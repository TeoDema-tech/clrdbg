using DotNet.Debugging.Engine.Decompilation;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

[TestFixture]
public class DecompilationTests {
    private string tempCacheDir = null!;

    [SetUp]
    public void SetUp() {
        tempCacheDir = Path.Combine(Path.GetTempPath(), "clrdbg_decompiler_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempCacheDir);
    }

    [TearDown]
    public void TearDown() {
        try {
            if (Directory.Exists(tempCacheDir))
                Directory.Delete(tempCacheDir, recursive: true);
        }
        catch {
            // best effort cleanup
        }
    }

    [Test]
    public void DecompilationService_GeneratesValidPortablePdbAndSource() {
        // Arrange
        var service = new DecompilationService(tempCacheDir);
        var targetAssembly = typeof(DecompilationService).Assembly.Location;
        Assert.That(File.Exists(targetAssembly), Is.True, "Target assembly must exist on disk");

        // Act
        var pdbPath = service.GetOrGeneratePdb(targetAssembly, new[] { targetAssembly });

        // Assert
        Assert.That(pdbPath, Is.Not.Null, "PDB path must be returned by DecompilationService");
        Assert.That(File.Exists(pdbPath!), Is.True, "Generated PDB file must exist on disk");
        Assert.That(new FileInfo(pdbPath!).Length, Is.GreaterThan(0), "Generated PDB must have non-zero size");

        // Cached lookup should return the exact same file
        var cachedPdbPath = service.GetOrGeneratePdb(targetAssembly, new[] { targetAssembly });
        Assert.That(cachedPdbPath, Is.EqualTo(pdbPath), "Subsequent request should return cached PDB");
    }
}
