// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._Whiskey.Dwaine.FileSystem;
using Content.Shared._Whiskey.Dwaine.FileSystem;
using NUnit.Framework;

namespace Content.Tests.Server.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineFileSystemPrimitivesTest
{
    private static readonly TimeSpan InitialTime = TimeSpan.FromSeconds(10);

    [Test]
    public void BootstrapAndCanonicalizationAreDeterministicAndRootConfined()
    {
        var fileSystem = Create();

        Assert.That(fileSystem.TryList("/", fileSystem.Root, out var root), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(root.Select(entry => entry.Name), Does.Contain("sys"));
        Assert.That(root.Select(entry => entry.Name), Does.Contain("proc"));
        Assert.That(fileSystem.TryResolve("/sys/drvr", fileSystem.Root, out _), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryResolve("/etc/mail", fileSystem.Root, out _), Is.EqualTo(DwaineVfsResult.Success));

        Assert.Multiple(() =>
        {
            Assert.That(
                fileSystem.TryCanonicalize("/home//./ada/../bob", fileSystem.Root, out var canonical),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(canonical, Is.EqualTo("/home/bob"));
            Assert.That(
                fileSystem.TryCanonicalize("../../escape", fileSystem.Root, out _),
                Is.EqualTo(DwaineVfsResult.RootEscape));
            Assert.That(
                fileSystem.TryCanonicalize(string.Empty, fileSystem.Root, out _),
                Is.EqualTo(DwaineVfsResult.InvalidPath));
            Assert.That(
                fileSystem.TryCanonicalize("/tmp/bad\\name", fileSystem.Root, out _),
                Is.EqualTo(DwaineVfsResult.InvalidName));
            Assert.That(
                fileSystem.TryCanonicalize("/tmp/bad\nname", fileSystem.Root, out _),
                Is.EqualTo(DwaineVfsResult.InvalidPath));
        });
    }

    [Test]
    public void RelativePathsCrudAndStableHandlesWorkAcrossRenameAndMove()
    {
        var fileSystem = Create();
        Assert.That(
            fileSystem.TryCreateDirectory("/home/ada", fileSystem.Root, InitialTime, out var home),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(
            fileSystem.TryCreate(
                "note",
                home,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text, Text = "hello" },
                InitialTime,
                out var note),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(
            fileSystem.TryWriteText("./note", home, " world", true, InitialTime + TimeSpan.FromSeconds(1)),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryReadText("note", home, out var text), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(text, Is.EqualTo("hello world"));

        Assert.That(fileSystem.TryRename("note", "journal", home, InitialTime), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryResolve("journal", home, out var renamed), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(renamed, Is.EqualTo(note));
        Assert.That(fileSystem.TryRename("journal", "JOURNAL", home, InitialTime),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryResolve("journal", home, out renamed), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(renamed, Is.EqualTo(note));
        Assert.That(fileSystem.TryCreateDirectory("/var/log", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(
            fileSystem.TryMove("journal", "/var/log/ada", home, InitialTime),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryResolve("/var/log/ada", fileSystem.Root, out var moved),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(moved, Is.EqualTo(note));
        Assert.That(fileSystem.TryGetPath(note, out var path), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(path, Is.EqualTo("/var/log/ada"));
        Assert.That(fileSystem.TryDelete(path, fileSystem.Root, false, InitialTime), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryGetSnapshot(note, out _), Is.EqualTo(DwaineVfsResult.InvalidHandle));
    }

    [Test]
    public void DirectoryDeletionRequiresExplicitRecursiveCleanup()
    {
        var fileSystem = Create();
        Assert.That(fileSystem.TryCreateDirectory("/tmp/tree/branch", fileSystem.Root, InitialTime, out _, true),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(
            fileSystem.TryCreate(
                "/tmp/tree/branch/leaf",
                fileSystem.Root,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text, Text = "data" },
                InitialTime,
                out var leaf),
            Is.EqualTo(DwaineVfsResult.Success));

        Assert.That(
            fileSystem.TryDelete("/tmp/tree", fileSystem.Root, false, InitialTime),
            Is.EqualTo(DwaineVfsResult.DirectoryNotEmpty));
        Assert.That(
            fileSystem.TryDelete("/tmp/tree", fileSystem.Root, true, InitialTime),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryGetSnapshot(leaf, out _), Is.EqualTo(DwaineVfsResult.InvalidHandle));
        Assert.That(fileSystem.TryDelete("/", fileSystem.Root, true, InitialTime),
            Is.EqualTo(DwaineVfsResult.RootProtected));
    }

    [Test]
    public void LinksDetectCyclesDepthAndBrokenTargetsWithoutFollowingFinalWhenRequested()
    {
        var fileSystem = Create(new DwaineFileSystemComponent { MaxLinkDepth = 2 });
        Assert.That(fileSystem.TryCreateDirectory("/tmp/a", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryCreateDirectory("/tmp/b", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryCreateLink("/tmp/a/to-b", "/tmp/b", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryCreateLink("/tmp/b/to-a", "/tmp/a", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(
            fileSystem.TryResolve("/tmp/a/to-b/to-a/to-b", fileSystem.Root, out _),
            Is.AnyOf(DwaineVfsResult.LinkCycle, DwaineVfsResult.LinkDepthLimit));

        Assert.That(
            fileSystem.TryCreate(
                "/tmp/target",
                fileSystem.Root,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text, Text = "target" },
                InitialTime,
                out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryCreateLink("/tmp/link", "/tmp/target", fileSystem.Root, InitialTime, out var link),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryDelete("/tmp/target", fileSystem.Root, false, InitialTime),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryResolve("/tmp/link", fileSystem.Root, out _),
            Is.EqualTo(DwaineVfsResult.BrokenLink));
        Assert.That(fileSystem.TryResolve("/tmp/link", fileSystem.Root, out var rawLink, false),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(rawLink, Is.EqualTo(link));
        Assert.That(fileSystem.TryGetSnapshot(rawLink, out var linkSnapshot), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(linkSnapshot.Kind, Is.EqualTo(DwaineVfsNodeKind.SymbolicLink));
    }

    [Test]
    public void CopyIsIndependentAndMoveRejectsDescendantDestinations()
    {
        var fileSystem = Create();
        Assert.That(fileSystem.TryCreateDirectory("/tmp/source/sub", fileSystem.Root, InitialTime, out _, true),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(
            fileSystem.TryCreate(
                "/tmp/source/sub/file",
                fileSystem.Root,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text, Text = "payload" },
                InitialTime,
                out _),
            Is.EqualTo(DwaineVfsResult.Success));

        Assert.That(
            fileSystem.TryCopy("/tmp/source", "/home/copy", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryDelete("/tmp/source", fileSystem.Root, true, InitialTime),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryReadText("/home/copy/sub/file", fileSystem.Root, out var copied),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(copied, Is.EqualTo("payload"));
        Assert.That(
            fileSystem.TryMove("/home/copy", "/home/copy/sub/nested", fileSystem.Root, InitialTime),
            Is.EqualTo(DwaineVfsResult.DestinationInsideSource));
        Assert.That(
            fileSystem.TryCopy("/home/copy", "/home/copy/sub/nested", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.DestinationInsideSource));
    }

    [Test]
    public void StructuredFileTypesRoundTripWithoutLeakingMutableInputs()
    {
        var fileSystem = Create();
        var recordFields = new Dictionary<string, string?> { ["alpha"] = "1", ["empty"] = null };
        var signalFields = new Dictionary<string, string?> { ["command"] = "status" };
        var access = new[] { "engineering", "command" };

        Assert.That(CreateNode("/tmp/record", DwaineVfsNodeKind.Record, fields: recordFields),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(CreateNode("/tmp/user", DwaineVfsNodeKind.UserData,
            userData: new DwaineVfsUserData("Ada", "Engineer", access)),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(CreateNode("/tmp/signal", DwaineVfsNodeKind.Signal,
            signal: new DwaineVfsSignalData(signalFields, "station")),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(CreateNode("/tmp/image", DwaineVfsNodeKind.ImageMetadata,
            image: new DwaineVfsImageMetadata("scan", "metadata only", ".+@")),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(CreateNode("/tmp/tool.vodka", DwaineVfsNodeKind.Program,
            program: new DwaineVfsProgramData("tool", "print(1)", true, false)),
            Is.EqualTo(DwaineVfsResult.Success));

        recordFields["alpha"] = "mutated";
        signalFields["command"] = "mutated";
        access[0] = "mutated";

        Assert.That(fileSystem.TryGetFields("/tmp/record", fileSystem.Root, out var record), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(record["alpha"], Is.EqualTo("1"));
        Assert.That(fileSystem.TryGetUserData("/tmp/user", fileSystem.Root, out var user), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(user.AccessTags[0], Is.EqualTo("engineering"));
        Assert.That(fileSystem.TryGetSignal("/tmp/signal", fileSystem.Root, out var signal), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(signal.Fields["command"], Is.EqualTo("status"));
        Assert.That(fileSystem.TryGetImageMetadata("/tmp/image", fileSystem.Root, out var image), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(image.TextPreview, Is.EqualTo(".+@"));
        Assert.That(fileSystem.TryGetProgram("/tmp/tool.vodka", fileSystem.Root, out var program), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(program.Source, Is.EqualTo("print(1)"));
        Assert.That(fileSystem.TryWriteText("/tmp/tool.vodka", fileSystem.Root, "\nexit", true, InitialTime),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryReadText("/tmp/tool.vodka", fileSystem.Root, out var source),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(source, Is.EqualTo("print(1)\nexit"));

        DwaineVfsResult CreateNode(
            string path,
            DwaineVfsNodeKind kind,
            IReadOnlyDictionary<string, string?>? fields = null,
            DwaineVfsUserData userData = default,
            DwaineVfsSignalData signal = default,
            DwaineVfsImageMetadata image = default,
            DwaineVfsProgramData program = default)
        {
            return fileSystem.TryCreate(
                path,
                fileSystem.Root,
                new DwaineVfsCreateRequest
                {
                    Kind = kind,
                    Fields = fields,
                    UserData = kind == DwaineVfsNodeKind.UserData ? userData : DwaineVfsUserData.Empty,
                    Signal = kind == DwaineVfsNodeKind.Signal ? signal : DwaineVfsSignalData.Empty,
                    Image = kind == DwaineVfsNodeKind.ImageMetadata ? image : DwaineVfsImageMetadata.Empty,
                    Program = kind == DwaineVfsNodeKind.Program ? program : DwaineVfsProgramData.Empty,
                },
                InitialTime,
                out _);
        }
    }

    [Test]
    public void RecordAndTextMutationsAreBoundedAndAtomic()
    {
        var fileSystem = Create(new DwaineFileSystemComponent
        {
            MaxRecordEntries = 2,
            MaxRecordCharacters = 16,
            MaxTextCharacters = 8,
        });
        Assert.That(
            fileSystem.TryCreate(
                "/tmp/record",
                fileSystem.Root,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Record },
                InitialTime,
                out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TrySetField("/tmp/record", fileSystem.Root, "a", "1", InitialTime),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TrySetField("/tmp/record", fileSystem.Root, "b", "2", InitialTime),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TrySetField("/tmp/record", fileSystem.Root, "c", "3", InitialTime),
            Is.EqualTo(DwaineVfsResult.DataLimit));
        Assert.That(fileSystem.TryGetFields("/tmp/record", fileSystem.Root, out var fields),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fields.Keys, Is.EqualTo(new[] { "a", "b" }));

        Assert.That(
            fileSystem.TryCreate(
                "/tmp/text",
                fileSystem.Root,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text, Text = "12345678" },
                InitialTime,
                out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryWriteText("/tmp/text", fileSystem.Root, "9", true, InitialTime),
            Is.EqualTo(DwaineVfsResult.DataLimit));
        Assert.That(fileSystem.TryReadText("/tmp/text", fileSystem.Root, out var text),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(text, Is.EqualTo("12345678"));
    }

    [Test]
    public void ArchivesRoundTripAndCannotContainTheirOwnDestination()
    {
        var fileSystem = Create();
        Assert.That(fileSystem.TryCreateDirectory("/tmp/tree/sub", fileSystem.Root, InitialTime, out _, true),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(
            fileSystem.TryCreate(
                "/tmp/tree/sub/file",
                fileSystem.Root,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text, Text = "archived" },
                InitialTime,
                out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(
            fileSystem.TryCreateArchive("/tmp/tree", "/tmp/tree/self.far", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.DestinationInsideSource));
        Assert.That(
            fileSystem.TryCreateArchive("/tmp/tree", "/tmp/tree.far", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryGetArchiveEntries("/tmp/tree.far", fileSystem.Root, out var entries),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(entries.Single().Name, Is.EqualTo("tree"));
        Assert.That(fileSystem.TryExtractArchive("/tmp/tree.far", "/home", fileSystem.Root, InitialTime),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryReadText("/home/tree/sub/file", fileSystem.Root, out var text),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(text, Is.EqualTo("archived"));
    }

    [Test]
    public void MetadataHooksPreserveOwnershipAndReadOnlyNodesRejectMutation()
    {
        var fileSystem = Create();
        Assert.That(
            fileSystem.TryCreate(
                "/tmp/owned",
                fileSystem.Root,
                new DwaineVfsCreateRequest
                {
                    Kind = DwaineVfsNodeKind.Text,
                    Text = "data",
                    Owner = 42,
                    Group = 7,
                    Mode = DwaineVfsMode.OwnerAll,
                },
                InitialTime,
                out var handle),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryGetSnapshot(handle, out var snapshot), Is.EqualTo(DwaineVfsResult.Success));
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Metadata.Owner, Is.EqualTo(42));
            Assert.That(snapshot.Metadata.Group, Is.EqualTo(7));
            Assert.That(snapshot.Metadata.Mode, Is.EqualTo(DwaineVfsMode.OwnerAll));
        });
        Assert.That(
            fileSystem.TrySetMetadata(handle, 99, 3, DwaineVfsMode.ReadOnlyFile, InitialTime + TimeSpan.FromSeconds(1)),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryCreateDirectory("/proc/forbidden", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.ReadOnly));
        Assert.That(fileSystem.TryDelete("/proc", fileSystem.Root, true, InitialTime),
            Is.EqualTo(DwaineVfsResult.ReadOnly));
    }

    [Test]
    public void NodeAndDepthLimitsContainMassCreation()
    {
        const int maxNodes = 20;
        var fileSystem = Create(new DwaineFileSystemComponent
        {
            MaxNodes = maxNodes,
            MaxDepth = 2,
        });
        Assert.That(fileSystem.TryCreateDirectory("/tmp/a", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryCreateDirectory("/tmp/a/b", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.DepthLimit));

        var suffix = 0;
        while (fileSystem.TryCreate(
                   $"/tmp/file-{suffix++}",
                   fileSystem.Root,
                   new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text },
                   InitialTime,
                   out _) == DwaineVfsResult.Success)
        {
        }

        Assert.That(fileSystem.NodeCount, Is.EqualTo(maxNodes));
        Assert.That(
            fileSystem.TryCreate(
                "/tmp/overflow",
                fileSystem.Root,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text },
                InitialTime,
                out _),
            Is.EqualTo(DwaineVfsResult.NodeLimit));
    }

    [Test]
    public void StructuralLimitClampsAlwaysPreserveTheCanonicalSystemTree()
    {
        var fileSystem = Create(new DwaineFileSystemComponent
        {
            MaxNodes = 1,
            MaxDepth = 1,
            MaxNameLength = 1,
            MaxPathLength = 1,
            MaxChildrenPerDirectory = 1,
        });

        Assert.Multiple(() =>
        {
            Assert.That(fileSystem.NodeCount, Is.EqualTo(DwaineFileSystemComponent.MinimumSystemNodes));
            Assert.That(fileSystem.TryResolve("/sys/drvr", fileSystem.Root, out _),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(fileSystem.TryResolve("/etc/mail", fileSystem.Root, out _),
                Is.EqualTo(DwaineVfsResult.Success));
            Assert.That(fileSystem.TryCreateDirectory("/tmp/x", fileSystem.Root, InitialTime, out _),
                Is.EqualTo(DwaineVfsResult.NodeLimit));
        });
    }

    [Test]
    public void MountedVolumesSupportRelativePathsCrossVolumeCopiesAndStableHandles()
    {
        var fileSystem = Create();
        var volume = CreateVolume(2);
        Assert.That(fileSystem.TryCreateDirectory("/mnt/disk", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryAttachVolume("/mnt/disk", fileSystem.Root, volume),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.AttachedVolumeCount, Is.EqualTo(1));
        Assert.That(fileSystem.TryResolve("/mnt/disk", fileSystem.Root, out var mountedRoot),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(mountedRoot, Is.EqualTo(new DwaineVfsNodeHandle(volume.Id, DwaineVfsNodeId.Root)));

        Assert.That(fileSystem.TryCreateDirectory("work", mountedRoot, InitialTime, out var work),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(
            fileSystem.TryCreate(
                "note",
                work,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text, Text = "portable" },
                InitialTime,
                out var note),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryGetPath(note, out var mountedPath), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(mountedPath, Is.EqualTo("/mnt/disk/work/note"));
        Assert.That(fileSystem.TryReadText("./note", work, out var text), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(text, Is.EqualTo("portable"));

        Assert.That(fileSystem.TryCopy("./note", "/home/copied", work, InitialTime, out var copy),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(copy.Volume, Is.EqualTo(DwaineVfsVolumeId.System));
        Assert.That(fileSystem.TryMove("./note", "/home/moved", work, InitialTime),
            Is.EqualTo(DwaineVfsResult.CrossVolumeMoveDenied));
        Assert.That(fileSystem.TryDelete("/mnt/disk", fileSystem.Root, true, InitialTime),
            Is.EqualTo(DwaineVfsResult.RootProtected));
        Assert.That(fileSystem.TryAttachVolume("/mnt/disk", fileSystem.Root, CreateVolume(3)),
            Is.EqualTo(DwaineVfsResult.MountPointBusy));
    }

    [Test]
    public void DetachInvalidatesHandlesAndReattachPreservesMediaState()
    {
        var fileSystem = Create();
        var volume = CreateVolume(2);
        Assert.That(fileSystem.TryCreateDirectory("/mnt/a", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryCreateDirectory("/mnt/b", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryAttachVolume("/mnt/a", fileSystem.Root, volume),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(
            fileSystem.TryCreate(
                "/mnt/a/persistent",
                fileSystem.Root,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text, Text = "survives" },
                InitialTime,
                out var file),
            Is.EqualTo(DwaineVfsResult.Success));

        Assert.That(fileSystem.TryDetachVolume(volume.Id, out var detached), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(detached, Is.SameAs(volume));
        Assert.That(fileSystem.TryGetSnapshot(file, out _), Is.EqualTo(DwaineVfsResult.InvalidHandle));
        Assert.That(fileSystem.TryResolve("/mnt/a/persistent", fileSystem.Root, out _),
            Is.EqualTo(DwaineVfsResult.NotFound));
        Assert.That(fileSystem.TryAttachVolume("/mnt/b", fileSystem.Root, detached),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryReadText("/mnt/b/persistent", fileSystem.Root, out var text),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(text, Is.EqualTo("survives"));
        Assert.That(fileSystem.TryGetPath(file, out var path), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(path, Is.EqualTo("/mnt/b/persistent"));
    }

    [Test]
    public void MultipleMountedVolumesRemainIsolatedAndRejectDuplicateAttachment()
    {
        var fileSystem = Create();
        var first = CreateVolume(2);
        var second = CreateVolume(3);
        Assert.That(fileSystem.TryCreateDirectory("/mnt/first", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryCreateDirectory("/mnt/second", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryAttachVolume("/mnt/first", fileSystem.Root, first),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryAttachVolume("/mnt/second", fileSystem.Root, second),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryAttachVolume("/home", fileSystem.Root, first),
            Is.EqualTo(DwaineVfsResult.VolumeAlreadyAttached));

        Assert.That(
            fileSystem.TryCreate(
                "/mnt/first/same",
                fileSystem.Root,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text, Text = "one" },
                InitialTime,
                out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(
            fileSystem.TryCreate(
                "/mnt/second/same",
                fileSystem.Root,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text, Text = "two" },
                InitialTime,
                out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryDetachVolume(first.Id, out _), Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryReadText("/mnt/second/same", fileSystem.Root, out var remaining),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(remaining, Is.EqualTo("two"));
        Assert.That(fileSystem.AttachedVolumeCount, Is.EqualTo(1));
    }

    [Test]
    public void MountedVolumeReadOnlyAndPerVolumeLimitsAreEnforced()
    {
        var fileSystem = Create();
        var volume = CreateVolume(2, maxTextCharacters: 4);
        Assert.That(fileSystem.TryCreateDirectory("/mnt/media", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryAttachVolume("/mnt/media", fileSystem.Root, volume),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(
            fileSystem.TryCreate(
                "/mnt/media/too-large",
                fileSystem.Root,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text, Text = "12345" },
                InitialTime,
                out _),
            Is.EqualTo(DwaineVfsResult.DataLimit));
        Assert.That(
            fileSystem.TryCreate(
                "/mnt/media/ok",
                fileSystem.Root,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text, Text = "1234" },
                InitialTime,
                out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(
            fileSystem.TryCreate(
                $"/mnt/media/{new string('a', 33)}",
                fileSystem.Root,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text },
                InitialTime,
                out _),
            Is.EqualTo(DwaineVfsResult.InvalidName));

        volume.ReadOnly = true;
        Assert.That(fileSystem.TryWriteText("/mnt/media/ok", fileSystem.Root, "x", false, InitialTime),
            Is.EqualTo(DwaineVfsResult.ReadOnly));
        Assert.That(fileSystem.TryDelete("/mnt/media/ok", fileSystem.Root, false, InitialTime),
            Is.EqualTo(DwaineVfsResult.ReadOnly));
    }

    [Test]
    public void MountedVolumeStructuralLimitsCannotBeBypassedByRenameMoveOrCopy()
    {
        var fileSystem = Create();
        var nameLimited = CreateVolume(2, maxNameLength: 4);
        var pathLimited = CreateVolume(3, maxPathLength: 10);
        var childLimited = CreateVolume(4, maxChildrenPerDirectory: 2);
        foreach (var path in new[] { "/mnt/name", "/mnt/path", "/mnt/children" })
        {
            Assert.That(fileSystem.TryCreateDirectory(path, fileSystem.Root, InitialTime, out _),
                Is.EqualTo(DwaineVfsResult.Success));
        }

        Assert.That(fileSystem.TryAttachVolume("/mnt/name", fileSystem.Root, nameLimited),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryAttachVolume("/mnt/path", fileSystem.Root, pathLimited),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryAttachVolume("/mnt/children", fileSystem.Root, childLimited),
            Is.EqualTo(DwaineVfsResult.Success));

        Assert.That(fileSystem.TryCreateDirectory("/mnt/name/ok", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryRename("/mnt/name/ok", "longer", fileSystem.Root, InitialTime),
            Is.EqualTo(DwaineVfsResult.InvalidName));

        Assert.That(fileSystem.TryCreateDirectory("/mnt/path/a", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryCreateDirectory("/mnt/path/a/123456", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryRename("/mnt/path/a", "long", fileSystem.Root, InitialTime),
            Is.EqualTo(DwaineVfsResult.InvalidPath));
        Assert.That(fileSystem.TryCreateDirectory("/home/tree", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryCreateDirectory("/home/tree/12345678", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryCopy("/home/tree", "/mnt/path/x", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.InvalidPath));

        Assert.That(fileSystem.TryCreateDirectory("/mnt/children/src", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryCreateDirectory("/mnt/children/dest", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryCreateDirectory("/mnt/children/src/move", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryCreateDirectory("/mnt/children/dest/a", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryCreateDirectory("/mnt/children/dest/b", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryMove(
                "/mnt/children/src/move",
                "/mnt/children/dest/move",
                fileSystem.Root,
                InitialTime),
            Is.EqualTo(DwaineVfsResult.ChildLimit));
    }

    [Test]
    public void ArchivesNestedInsideArchivesPreserveEmbeddedPayload()
    {
        var fileSystem = Create();
        Assert.That(
            fileSystem.TryCreate(
                "/tmp/source",
                fileSystem.Root,
                new DwaineVfsCreateRequest { Kind = DwaineVfsNodeKind.Text, Text = "nested" },
                InitialTime,
                out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryCreateArchive("/tmp/source", "/tmp/inner.arc", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryCreateArchive("/tmp/inner.arc", "/tmp/outer.arc", fileSystem.Root, InitialTime, out _),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryExtractArchive("/tmp/outer.arc", "/home", fileSystem.Root, InitialTime),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(fileSystem.TryGetArchiveEntries("/home/inner.arc", fileSystem.Root, out var embedded),
            Is.EqualTo(DwaineVfsResult.Success));
        Assert.That(embedded.Single().Text, Is.EqualTo("nested"));
    }

    private static DwaineVirtualFileSystem Create(DwaineFileSystemComponent? component = null)
    {
        return new DwaineVirtualFileSystem(component ?? new DwaineFileSystemComponent(), InitialTime);
    }

    private static DwaineVfsVolume CreateVolume(
        ulong id,
        int maxTextCharacters = 128,
        int maxNameLength = 32,
        int maxPathLength = 256,
        int maxChildrenPerDirectory = 32)
    {
        var limits = DwaineVfsLimits.FromValues(
            128,
            12,
            maxNameLength,
            maxPathLength,
            maxChildrenPerDirectory,
            8,
            maxTextCharacters,
            16,
            128,
            64,
            8);
        return new DwaineVfsVolume(new DwaineVfsVolumeId(id), limits, false, InitialTime);
    }
}
