using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using Robust.Shared.ContentPack;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.SpaceWayBlueprints;

public sealed partial class BlueprintSystem
{
    private const int MaxNameLength = 64;
    private const string Extension = ".txt";
    private const string DefaultName = "чертёж";

    private static readonly ResPath Folder = new("/blueprints");

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private Dictionary<string, Blueprint?>? _library;
    private readonly List<string> _names = new();

    public IReadOnlyList<string> Names
    {
        get
        {
            Library();
            return _names;
        }
    }

    public bool TryLoad(string name, [NotNullWhen(true)] out Blueprint? blueprint)
    {
        blueprint = Library().GetValueOrDefault(name);
        return blueprint != null;
    }

    public void Rescan()
    {
        _library = null;
        ListChanged?.Invoke();
    }

    public void Delete(string name)
    {
        try
        {
            var path = PathOf(name);
            if (_resources.UserData.Exists(path))
                _resources.UserData.Delete(path);
        }
        catch (IOException e)
        {
            Log.Warning($"Не удалось удалить чертёж {name}: {e.Message}");
            _popup.PopupCursor($"Не удалось удалить чертёж «{name}»");
            return;
        }

        if (Library().Remove(name))
            _names.Remove(name);

        ListChanged?.Invoke();
    }

    public string? Export(string name)
    {
        return TryLoad(name, out var blueprint) ? blueprint.Serialize(name) : null;
    }

    public string Import(string text)
    {
        if (!Blueprint.TryParse(text, out var blueprint, out var name))
            return "В буфере нет чертежа";

        if (IsTooBig(new Box2i(Vector2i.Zero, blueprint.Size)))
            return $"Чертёж в буфере больше {MaxSize}×{MaxSize}";

        name = UniqueName(name ?? string.Empty);
        return TrySave(name, blueprint)
            ? $"Добавлен чертёж «{name}»"
            : $"Не удалось сохранить чертёж «{name}»";
    }

    public void OpenFolder()
    {
        try
        {
            _resources.UserData.CreateDir(Folder);
            _resources.UserData.OpenOsWindow(Folder);
        }
        catch (IOException e)
        {
            Log.Warning($"Не удалось открыть папку чертежей: {e.Message}");
        }
    }

    private bool TrySave(string name, Blueprint blueprint)
    {
        try
        {
            _resources.UserData.CreateDir(Folder);
            _resources.UserData.WriteAllText(PathOf(name), blueprint.Serialize());
        }
        catch (IOException e)
        {
            Log.Warning($"Не удалось сохранить чертёж {name}: {e.Message}");
            return false;
        }

        var library = Library();
        if (!library.ContainsKey(name))
        {
            _names.Add(name);
            _names.Sort(StringComparer.CurrentCultureIgnoreCase);
        }

        library[name] = blueprint;
        ListChanged?.Invoke();
        return true;
    }

    private string UniqueName(string raw)
    {
        var name = Sanitize(raw);
        if (name.Length == 0)
            name = DefaultName;

        var library = Library();
        if (!library.ContainsKey(name))
            return name;

        for (var i = 2; ; i++)
        {
            var numbered = $"{name}-{i}";
            if (!library.ContainsKey(numbered))
                return numbered;
        }
    }

    private static string Sanitize(string raw)
    {
        var name = new StringBuilder();
        foreach (var c in raw.Trim())
        {
            if (char.IsLetterOrDigit(c) || c is '_' or '-')
                name.Append(c);
            else if (char.IsWhiteSpace(c))
                name.Append('-');

            if (name.Length == MaxNameLength)
                break;
        }

        if (ReservedNames.Contains(name.ToString()))
            name.Append('_');

        return name.ToString();
    }

    private Dictionary<string, Blueprint?> Library()
    {
        if (_library != null)
            return _library;

        _library = new Dictionary<string, Blueprint?>(StringComparer.OrdinalIgnoreCase);
        _names.Clear();

        try
        {
            if (!_resources.UserData.IsDir(Folder))
                return _library;

            foreach (var file in _resources.UserData.DirectoryEntries(Folder))
            {
                if (!file.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                    continue;

                var name = file[..^Extension.Length];
                if (_library.TryAdd(name, Read(name)))
                    _names.Add(name);
            }
        }
        catch (IOException e)
        {
            Log.Warning($"Не удалось прочитать папку чертежей: {e.Message}");
        }

        _names.Sort(StringComparer.CurrentCultureIgnoreCase);
        return _library;
    }

    private Blueprint? Read(string name)
    {
        return _resources.UserData.TryReadAllText(PathOf(name), out var text)
               && Blueprint.TryParse(text, out var blueprint, out _)
            ? blueprint
            : null;
    }

    private static ResPath PathOf(string name)
    {
        return Folder / (name + Extension);
    }
}
