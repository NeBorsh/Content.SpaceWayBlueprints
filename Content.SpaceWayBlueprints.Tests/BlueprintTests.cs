using System.Text;
using Content.SpaceWayBlueprints;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.SpaceWayBlueprints.Tests;

[TestFixture]
[TestOf(typeof(Blueprint))]
public sealed class BlueprintTests
{
    [Test]
    public void SerializeParseRoundTrip()
    {
        var blueprint = new Blueprint();
        blueprint.Entries.Add(new BlueprintEntry(new Vector2i(0, 0), Direction.South, "GasPipeStraight"));
        blueprint.Entries.Add(new BlueprintEntry(new Vector2i(0, 0), Direction.West, "GasPipeStraightAlt1"));
        blueprint.Entries.Add(new BlueprintEntry(new Vector2i(2, 1), Direction.North, "Airlock"));

        Assert.That(Blueprint.TryParse(blueprint.Serialize("насосная"), out var parsed, out var name));

        Assert.Multiple(() =>
        {
            Assert.That(name, Is.EqualTo("насосная"));
            Assert.That(parsed!.Entries, Is.EqualTo(blueprint.Entries));
        });
    }

    [Test]
    public void ParseWithoutNameLine()
    {
        var blueprint = new Blueprint();
        blueprint.Entries.Add(new BlueprintEntry(new Vector2i(1, 1), Direction.East, "Grille"));

        Assert.That(Blueprint.TryParse(blueprint.Serialize(), out _, out var name));
        Assert.That(name, Is.Null);
    }

    [Test]
    public void ParseAcceptsOldModHeader()
    {
        Assert.That(Blueprint.TryParse("# SpaceWay blueprint 1\nGrille 0 0 0\n", out var parsed, out _));
        Assert.That(parsed!.Entries, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseAcceptsCommentsBlankLinesAndDuplicates()
    {
        const string text = "# blueprint v1\n"
                            + "\n"
                            + "Grille 0 0 0\r\n"
                            + "# comment\n"
                            + "Grille 0 0 0\n"
                            + "Grille 1 0 2\n";

        Assert.That(Blueprint.TryParse(text, out var parsed, out _));
        Assert.That(parsed!.Entries, Is.EqualTo(new[]
        {
            new BlueprintEntry(new Vector2i(0, 0), Direction.South, "Grille"),
            new BlueprintEntry(new Vector2i(1, 0), Direction.East, "Grille"),
        }));
    }

    [TestCase("")]
    [TestCase("# blueprint v1\n")]
    [TestCase("asdkjh qwe zxc\nlorem ipsum")]
    [TestCase("Grille 0 0 0\nGrille 0 zero 0\n")]
    [TestCase("Grille 0 0 0\nGrille 0 0 9\n")]
    [TestCase("Grille 0 0 0\nGrille 0 0\n")]
    [TestCase("Grille 0 0 0 1\n")]
    public void ParseRejectsBrokenText(string text)
    {
        Assert.That(Blueprint.TryParse(text, out var parsed, out _), Is.False);
        Assert.That(parsed, Is.Null);
    }

    [Test]
    public void ParseRejectsTooManyEntries()
    {
        var text = new StringBuilder();
        for (var i = 0; i <= Blueprint.MaxEntries; i++)
        {
            text.Append("Recipe").Append(i).Append(" 0 0 0\n");
        }

        Assert.That(Blueprint.TryParse(text.ToString(), out _, out _), Is.False);
    }

    [Test]
    public void ParseShiftsOffsetsToOrigin()
    {
        Assert.That(Blueprint.TryParse("Grille -2 3 0\nGrille 1 -1 0\n", out var parsed, out _));

        Assert.Multiple(() =>
        {
            Assert.That(parsed!.Entries[0].Offset, Is.EqualTo(new Vector2i(0, 4)));
            Assert.That(parsed.Entries[1].Offset, Is.EqualTo(new Vector2i(3, 0)));
            Assert.That(parsed.Size, Is.EqualTo(new Vector2i(4, 5)));
        });
    }

    [Test]
    public void SizeAndCenter()
    {
        var blueprint = new Blueprint();
        Assert.That(blueprint.Size, Is.EqualTo(Vector2i.Zero));

        blueprint.Entries.Add(new BlueprintEntry(new Vector2i(0, 0), Direction.South, "Grille"));
        blueprint.Entries.Add(new BlueprintEntry(new Vector2i(4, 2), Direction.South, "Grille"));

        Assert.Multiple(() =>
        {
            Assert.That(blueprint.Size, Is.EqualTo(new Vector2i(5, 3)));
            Assert.That(blueprint.Center, Is.EqualTo(new Vector2i(2, 1)));
        });
    }

    [Test]
    public void RotateOffset()
    {
        var offset = new Vector2i(2, 1);

        Assert.Multiple(() =>
        {
            Assert.That(Blueprint.Rotate(offset, 0), Is.EqualTo(offset));
            Assert.That(Blueprint.Rotate(offset, 1), Is.EqualTo(new Vector2i(-1, 2)));
            Assert.That(Blueprint.Rotate(offset, 2), Is.EqualTo(new Vector2i(-2, -1)));
            Assert.That(Blueprint.Rotate(offset, 4), Is.EqualTo(offset));
        });
    }

    [Test]
    public void RotateDirectionMatchesOffset()
    {
        foreach (var direction in new[] { Direction.South, Direction.East, Direction.North, Direction.West })
        {
            for (var turns = 0; turns < 4; turns++)
            {
                var rotated = Blueprint.Rotate(direction, turns);
                var expected = Blueprint.Rotate(direction.ToIntVec(), turns);

                Assert.That(rotated.ToIntVec(), Is.EqualTo(expected), $"{direction}, turns: {turns}");
            }
        }
    }
}
