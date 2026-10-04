using DeckForge.CodeGen.Generation;
using NUnit.Framework;

namespace DeckForge.Tests;

/// <summary>
/// Tests for the resx operations the Localization page performs.
/// </summary>
/// <remarks>
/// These cover the merger itself rather than the view model, because the merger is what writes the
/// files. The faults this fixes were a save that wrote twice, a generated key name that collided
/// after a deletion, and no way to rename at all.
/// </remarks>
[TestFixture]
public sealed class ResxMergerTests
{
    private TempDirectory _temp = null!;

    private string _dir = "";
    private string _resx = "";

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory("ResxMergerTests");
        _dir = _temp.Root;
        _resx = Path.Combine(_dir, "Strings.resx");
        File.WriteAllText(
            _resx,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
              <resheader name="version"><value>2.0</value></resheader>
              <resheader name="reader"><value>System.Resources.ResXResourceReader</value></resheader>
              <resheader name="writer"><value>System.Resources.ResXResourceWriter</value></resheader>
              <data name="Zebra.Last" xml:space="preserve"><value>last</value></data>
              <data name="Alpha.First" xml:space="preserve"><value>first</value></data>
            </root>
            """);
    }

    [TearDown]
    public void TearDown() => _temp.Dispose();

    [Test]
    public void Setting_a_key_writes_once_and_reports_no_change_the_second_time()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ResxMerger.SetKey(_resx, "Alpha.First", "changed"), Is.True);
            Assert.That(ResxMerger.SetKey(_resx, "Alpha.First", "changed"), Is.False,
                "An unchanged value rewrote the file.");
            Assert.That(ResxMerger.ReadKeys(_resx)["Alpha.First"], Is.EqualTo("changed"));
        });
    }

    [Test]
    public void Setting_a_missing_key_creates_it()
    {
        ResxMerger.SetKey(_resx, "New.Key", "value");

        Assert.That(ResxMerger.ReadKeys(_resx), Does.ContainKey("New.Key"));
    }

    [Test]
    public void Adding_keys_does_not_overwrite_what_is_already_there()
    {
        // This is why the page used to call both AddKeys and SetKey: AddKeys alone will not update
        // an existing entry, which is correct for a merge and wrong for a save.
        ResxMerger.AddKeys(_resx, new Dictionary<string, string> { ["Alpha.First"] = "IGNORED" });

        Assert.That(ResxMerger.ReadKeys(_resx)["Alpha.First"], Is.EqualTo("first"));
    }

    [Test]
    public void Renaming_keeps_the_value_and_the_position_in_the_file()
    {
        ResxMerger.RenameKey(_resx, "Zebra.Last", "Zebra.Final");

        var text = File.ReadAllText(_resx);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("Zebra.Final"));
            Assert.That(text, Does.Not.Contain("Zebra.Last"));
            Assert.That(ResxMerger.ReadKeys(_resx)["Zebra.Final"], Is.EqualTo("last"));

            // Position matters: resx files are hand-ordered, and appending would scatter the group.
            Assert.That(text.IndexOf("Zebra.Final", StringComparison.Ordinal),
                Is.LessThan(text.IndexOf("Alpha.First", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void Renaming_onto_an_existing_key_is_refused_rather_than_discarding_one()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ResxMerger.RenameKey(_resx, "Zebra.Last", "Alpha.First"));
    }

    [Test]
    public void Renaming_a_key_that_is_not_there_changes_nothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ResxMerger.RenameKey(_resx, "Not.Here", "Somewhere"), Is.False);
            Assert.That(ResxMerger.ReadKeys(_resx), Does.Not.ContainKey("Somewhere"));
        });
    }

    [Test]
    public void Removing_a_key_takes_it_out()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ResxMerger.RemoveKey(_resx, "Zebra.Last"), Is.True);
            Assert.That(ResxMerger.ReadKeys(_resx), Does.Not.ContainKey("Zebra.Last"));
            Assert.That(ResxMerger.RemoveKey(_resx, "Zebra.Last"), Is.False);
        });
    }

    [Test]
    public void Every_key_survives_a_round_trip_through_the_merger()
    {
        ResxMerger.AddKeys(_resx, new Dictionary<string, string>
        {
            ["A.One"] = "1",
            ["A.Two"] = "2",
        });
        ResxMerger.RenameKey(_resx, "A.One", "A.renamed");

        var keys = ResxMerger.ReadKeys(_resx);

        Assert.Multiple(() =>
        {
            Assert.That(keys, Has.Count.EqualTo(4));
            Assert.That(keys["A.renamed"], Is.EqualTo("1"));
            Assert.That(keys["A.Two"], Is.EqualTo("2"));
            Assert.That(keys["Alpha.First"], Is.EqualTo("first"));
            Assert.That(keys["Zebra.Last"], Is.EqualTo("last"));
        });
    }
}
