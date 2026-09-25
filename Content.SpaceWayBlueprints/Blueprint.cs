using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Robust.Shared.Maths;

namespace Content.SpaceWayBlueprints;

public readonly record struct BlueprintEntry(Vector2i Offset, Direction Direction, string Recipe);

public sealed class Blueprint
{
    public const int MaxEntries = 4096;

    private const string Header = "# blueprint v1";

    private const string NamePrefix = "# name:";

    public readonly List<BlueprintEntry> Entries = new();

    public Vector2i Size
    {
        get
        {
            var size = Vector2i.Zero;
            foreach (var entry in Entries)
            {
                size = Vector2i.ComponentMax(size, entry.Offset + Vector2i.One);
            }

            return size;
        }
    }

    public Vector2i Center
    {
        get
        {
            var size = Size;
            return new Vector2i((size.X - 1) / 2, (size.Y - 1) / 2);
        }
    }

    public string Serialize(string? name = null)
    {
        var text = new StringBuilder();
        text.Append(Header).Append('\n');

        if (name != null)
            text.Append(NamePrefix).Append(' ').Append(name).Append('\n');

        foreach (var entry in Entries)
        {
            text.Append(entry.Recipe).Append(' ')
                .Append(entry.Offset.X).Append(' ')
                .Append(entry.Offset.Y).Append(' ')
                .Append((int) entry.Direction).Append('\n');
        }

        return text.ToString();
    }

    public static bool TryParse(string text, [NotNullWhen(true)] out Blueprint? blueprint, out string? name)
    {
        blueprint = null;
        name = null;

        var parsed = new Blueprint();
        var seen = new HashSet<BlueprintEntry>();

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();

            if (line.StartsWith(NamePrefix, StringComparison.Ordinal))
            {
                name = line[NamePrefix.Length..].Trim();
                continue;
            }

            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (!TryParseEntry(line, out var entry))
                return false;

            if (!seen.Add(entry))
                continue;

            if (seen.Count > MaxEntries)
                return false;

            parsed.Entries.Add(entry);
        }

        if (parsed.Entries.Count == 0)
            return false;

        parsed.Normalize();
        blueprint = parsed;
        return true;
    }

    public static Vector2i Rotate(Vector2i offset, int turns)
    {
        for (var i = 0; i < turns; i++)
        {
            offset = new Vector2i(-offset.Y, offset.X);
        }

        return offset;
    }

    public static Direction Rotate(Direction direction, int turns)
    {
        return new Angle(Math.PI / 2 * turns).RotateDir(direction);
    }

    private static bool TryParseEntry(string line, out BlueprintEntry entry)
    {
        entry = default;

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var direction)
            || direction is < 0 or > 7)
        {
            return false;
        }

        entry = new BlueprintEntry(new Vector2i(x, y), (Direction) direction, parts[0]);
        return true;
    }

    private void Normalize()
    {
        var min = Entries[0].Offset;
        foreach (var entry in Entries)
        {
            min = Vector2i.ComponentMin(min, entry.Offset);
        }

        if (min == Vector2i.Zero)
            return;

        for (var i = 0; i < Entries.Count; i++)
        {
            Entries[i] = Entries[i] with { Offset = Entries[i].Offset - min };
        }
    }
}
