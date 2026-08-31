using Content.Server.Whiskey.Shuttles.Commands;
using NUnit.Framework;

namespace Content.Tests.Server.Whiskey.Shuttles;

[TestFixture]
public sealed class SaveShuttleCommandTest
{
    [TestCase("rescue", "/Maps/Shuttles/rescue.yml")]
    [TestCase("rescue.yml", "/Maps/Shuttles/rescue.yml")]
    [TestCase("rescue.YML", "/Maps/Shuttles/rescue.yml")]
    public void AcceptsYamlFileNames(string argument, string expected)
    {
        Assert.That(SaveShuttleCommand.TryGetTargetPath(argument, out var path, out var error), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(path.ToString(), Is.EqualTo(expected));
            Assert.That(error, Is.Empty);
        });
    }

    [TestCase("")]
    [TestCase("../rescue")]
    [TestCase("/preferences.db")]
    [TestCase("preferences.db")]
    [TestCase("preferences.db-wal")]
    [TestCase("rescue.yaml")]
    public void RejectsUnsafeOrNonYamlFileNames(string argument)
    {
        Assert.That(SaveShuttleCommand.TryGetTargetPath(argument, out _, out var error), Is.False);
        Assert.That(error, Is.Not.Empty);
    }
}
