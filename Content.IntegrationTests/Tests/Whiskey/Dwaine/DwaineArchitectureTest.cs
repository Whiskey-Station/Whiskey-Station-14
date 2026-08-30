// SPDX-FileCopyrightText: 2026 Whiskey Station Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Whiskey.Dwaine;
using Content.Shared._Whiskey.Dwaine.Prototypes;
using Content.Shared._Whiskey.VodkaCode;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Whiskey.Dwaine;

[TestFixture]
public sealed class DwaineArchitectureTest : GameTest
{
    private static readonly ProtoId<DwaineArchitecturePrototype> Architecture = "WhiskeyDwaine";

    [Test]
    public async Task ArchitecturePrototypeMatchesCanonicalSpecification()
    {
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ProtoMan.Index(Architecture);

            Assert.Multiple(() =>
            {
                Assert.That(prototype.SpecificationVersion, Is.EqualTo(VodkaCodeSpecification.Version));
                Assert.That(prototype.VodkaCodeFileExtension, Is.EqualTo(VodkaCodeSpecification.FileExtension));
                Assert.That(prototype.VodkaCodeFileExtension, Does.StartWith("."));
            });
        });
    }

    [Test]
    public void SharedContractsDoNotReferencePresentationOrAuthorityAssemblies()
    {
        var references = typeof(DwaineMachineKind).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(references, Does.Not.Contain("Content.Client"));
            Assert.That(references, Does.Not.Contain("Content.Server"));
        });
    }
}
