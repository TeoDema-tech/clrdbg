using DotNet.Debugging.Adapter;
using DotNet.Debugging.Engine.Models;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DotNet.Debugging.Tests;

[TestFixture]
public class NetFxConfigurationTests {
    [Test]
    public void LaunchConfiguration_WithClr_DefaultsToDesktopClrFlavor() {
        var properties = new Dictionary<string, JToken> {
            ["type"] = "clr",
            ["program"] = "/test/app.exe"
        };

        var config = new LaunchConfiguration(properties);

        Assert.That(config.RuntimeFlavor, Is.EqualTo("desktopclr"));
        var launchRequest = config.GetLaunchRequest();
        Assert.That(launchRequest.IsDesktopClr, Is.True);
        Assert.That(launchRequest.RuntimeFlavor, Is.EqualTo("desktopclr"));
    }

    [Test]
    public void LaunchConfiguration_WithExplicitRuntimeFlavor_PreservesDesktopClr() {
        var properties = new Dictionary<string, JToken> {
            ["type"] = "coreclr",
            ["program"] = "/test/app.exe",
            ["runtimeFlavor"] = "desktopclr"
        };

        var config = new LaunchConfiguration(properties);

        Assert.That(config.RuntimeFlavor, Is.EqualTo("desktopclr"));
        var launchRequest = config.GetLaunchRequest();
        Assert.That(launchRequest.IsDesktopClr, Is.True);
    }

    [Test]
    public void LaunchConfiguration_WithPipeTransport_ParsesOptions() {
        var pipeToken = JToken.FromObject(new {
            pipeProgram = "ssh",
            pipeArgs = new[] { "user@host" },
            debuggerPath = "/usr/bin/clrdbg"
        });

        var properties = new Dictionary<string, JToken> {
            ["type"] = "coreclr",
            ["program"] = "/test/app.dll",
            ["pipeTransport"] = pipeToken
        };

        var config = new LaunchConfiguration(properties);

        Assert.That(config.PipeTransport, Is.Not.Null);
        Assert.That(config.PipeTransport!.PipeProgram, Is.EqualTo("ssh"));
        Assert.That(config.PipeTransport.PipeArgs, Contains.Item("user@host"));
        Assert.That(config.PipeTransport.DebuggerPath, Is.EqualTo("/usr/bin/clrdbg"));
    }

    [Test]
    public void AttachConfiguration_WithClr_DefaultsToDesktopClrFlavor() {
        var properties = new Dictionary<string, JToken> {
            ["type"] = "clr",
            ["processId"] = 1234
        };

        var config = new AttachConfiguration(properties);

        Assert.That(config.RuntimeFlavor, Is.EqualTo("desktopclr"));
        Assert.That(config.IsDesktopClr, Is.True);
    }
}
