using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Content.Client.Popups;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared.Input;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Content.SpaceWayBlueprints;

public sealed class BlueprintWindow : FancyWindow
{
    private const float PreviewSize = 72;

    private const int MaterialsInline = 3;

    private static readonly Color CardBackground = Color.FromHex("#2A2A33");
    private static readonly Color CardBorder = Color.FromHex("#3A3A46");
    private static readonly Color Accent = Color.FromHex("#4F8FD0");
    private static readonly Color StatusBackground = Color.FromHex("#1E2B3A");

    private static BlueprintWindow? _open;

    private readonly BlueprintSystem _blueprints;
    private readonly IClipboardManager _clipboard;
    private readonly IInputManager _input;
    private readonly PopupSystem _popup;

    private readonly LineEdit _name;
    private readonly PanelContainer _statusPanel;
    private readonly Label _statusTitle;
    private readonly BoxContainer _statusHints;
    private readonly Label _count;
    private readonly Placeholder _empty;
    private readonly ScrollContainer _scroll;
    private readonly BoxContainer _list;
    private readonly Label _ghosts;
    private readonly Button _clear;

    private readonly Dictionary<string, Card> _cards = new();

    public static bool AnyOpen => _open != null;

    private BlueprintWindow(BlueprintSystem blueprints)
    {
        _blueprints = blueprints;
        _clipboard = IoCManager.Resolve<IClipboardManager>();
        _input = IoCManager.Resolve<IInputManager>();
        _popup = IoCManager.Resolve<IEntityManager>().System<PopupSystem>();

        Title = "Чертежи";
        MinSize = new Vector2(440, 420);
        SetSize = new Vector2(500, 600);

        var root = Vertical(10);
        root.Margin = new Thickness(10);

        root.AddChild(Heading("Новый чертёж"));

        _name = new LineEdit { HorizontalExpand = true, PlaceHolder = "Имя, можно не писать" };
        _name.OnTextEntered += _ => Select();

        var select = new Button { Text = "Выделить участок" };
        select.AddStyleClass(StyleClass.Positive);
        select.OnPressed += _ => Select();

        var import = new Button { Text = "Из буфера", ToolTip = "Добавить чертёж, который прислали текстом" };
        import.OnPressed += _ => Import();

        var selectRow = Horizontal(6);
        selectRow.AddChild(_name);
        selectRow.AddChild(select);
        selectRow.AddChild(import);
        root.AddChild(selectRow);

        _statusTitle = new Label { FontColorOverride = Accent };
        _statusHints = Vertical(0);

        var statusText = Vertical(2);
        statusText.HorizontalExpand = true;
        statusText.AddChild(_statusTitle);
        statusText.AddChild(_statusHints);

        var cancel = new Button { Text = "Отмена", VerticalAlignment = VAlignment.Center };
        cancel.AddStyleClass(StyleClass.Negative);
        cancel.OnPressed += _ => _blueprints.Cancel();

        var statusRow = Horizontal(8);
        statusRow.Margin = new Thickness(10, 6);
        statusRow.AddChild(statusText);
        statusRow.AddChild(cancel);

        _statusPanel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = StatusBackground,
                BorderColor = Accent,
                BorderThickness = new Thickness(1),
            },
        };
        _statusPanel.AddChild(statusRow);
        root.AddChild(_statusPanel);

        _count = new Label { StyleClasses = { StyleClass.LabelWeak }, VerticalAlignment = VAlignment.Bottom };

        var listHeader = Horizontal(8);
        listHeader.AddChild(Heading("Сохранённые"));
        listHeader.AddChild(_count);
        root.AddChild(listHeader);

        _empty = new Placeholder
        {
            VerticalExpand = true,
            PlaceholderText = "Чертежей пока нет: выдели участок на станции",
        };
        root.AddChild(_empty);

        _list = Vertical(6);
        _list.HorizontalExpand = true;

        _scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _scroll.AddChild(_list);
        root.AddChild(_scroll);

        _ghosts = new Label
        {
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            StyleClasses = { StyleClass.LabelSubText },
        };

        _clear = new Button { Text = "Убрать все" };
        _clear.AddStyleClass(StyleClass.Negative);
        _clear.OnPressed += _ => _blueprints.ClearGhosts();

        var folder = new Button { Text = "Папка", ToolTip = "Чертежи хранятся там текстовыми файлами" };
        folder.OnPressed += _ => _blueprints.OpenFolder();

        var footer = Horizontal(6);
        footer.AddChild(_ghosts);
        footer.AddChild(_clear);
        footer.AddChild(folder);
        root.AddChild(footer);

        ContentsContainer.AddChild(root);

        _blueprints.ListChanged += RebuildCards;
        _blueprints.StateChanged += RefreshState;
        _blueprints.GhostsChanged += RefreshGhosts;
        OnClose += () =>
        {
            _blueprints.ListChanged -= RebuildCards;
            _blueprints.StateChanged -= RefreshState;
            _blueprints.GhostsChanged -= RefreshGhosts;
            if (_open == this)
                _open = null;
        };

        _blueprints.Rescan();
        RefreshState();
        RefreshGhosts();
    }

    public static void Toggle(BlueprintSystem blueprints)
    {
        if (_open != null)
        {
            _open.Close();
            return;
        }

        _open = new BlueprintWindow(blueprints);
        _open.OpenCentered();
    }

    private void Select()
    {
        var name = _name.Text;
        _name.Text = string.Empty;
        _blueprints.BeginSelect(name);
    }

    private async void Import()
    {
        var text = await _clipboard.GetText();
        _popup.PopupCursor(_blueprints.Import(text));
    }

    private void Export(string name)
    {
        if (_blueprints.Export(name) is not { } text)
            return;

        _clipboard.SetText(text);
        _popup.PopupCursor($"Чертёж «{name}» скопирован в буфер");
    }

    private void RebuildCards()
    {
        _list.RemoveAllChildren();
        _cards.Clear();

        var names = _blueprints.Names;
        _count.Text = names.Count == 0 ? string.Empty : names.Count.ToString();
        _empty.Visible = names.Count == 0;
        _scroll.Visible = names.Count != 0;

        foreach (var name in names)
        {
            var card = new Card(this, name);
            _cards[name] = card;
            _list.AddChild(card.Root);
        }

        RefreshActiveCard();
    }

    private void RefreshState()
    {
        _statusHints.RemoveAllChildren();

        switch (_blueprints.Mode)
        {
            case BlueprintMode.Select:
                _statusTitle.Text = $"Выделяешь «{_blueprints.ActiveName}»";
                _statusHints.AddChild(Hint($"{Key(EngineKeyFunctions.Use)}: первый угол, затем второй"));
                _statusHints.AddChild(Hint($"{Key(EngineKeyFunctions.UseSecondary)}: отмена. Не больше {BlueprintSystem.MaxSize}×{BlueprintSystem.MaxSize}"));
                break;
            case BlueprintMode.Paste:
                _statusTitle.Text = $"Ставишь «{_blueprints.ActiveName}»";
                _statusHints.AddChild(Hint($"{Key(EngineKeyFunctions.Use)}: поставить, можно несколько раз"));
                _statusHints.AddChild(Hint($"{Key(ContentKeyFunctions.RotateObjectClockwise)} / {Key(ContentKeyFunctions.RotateObjectCounterclockwise)}: повернуть"));
                _statusHints.AddChild(Hint($"{Key(EngineKeyFunctions.UseSecondary)}: закончить"));
                break;
        }

        _statusPanel.Visible = _blueprints.Mode != BlueprintMode.None;
        RefreshActiveCard();
    }

    private void RefreshActiveCard()
    {
        var active = _blueprints.Mode == BlueprintMode.Paste ? _blueprints.ActiveName : null;
        foreach (var (name, card) in _cards)
        {
            card.SetActive(name == active);
        }
    }

    private void RefreshGhosts()
    {
        var ghosts = _blueprints.GhostCount;
        _ghosts.Text = ghosts == 0 ? "Призраков на станции нет" : $"Призраков на станции: {ghosts}";
        _clear.Disabled = ghosts == 0;
    }

    private string Key(BoundKeyFunction function)
    {
        return _input.GetKeyFunctionButtonString(function);
    }

    private sealed class Card
    {
        public readonly PanelContainer Root;

        private readonly StyleBoxFlat _box;
        private readonly Button _paste;
        private bool _active;

        public Card(BlueprintWindow window, string name)
        {
            var blueprints = window._blueprints;
            var loaded = blueprints.TryLoad(name, out var blueprint);

            var info = Vertical(2);
            info.HorizontalExpand = true;
            info.VerticalAlignment = VAlignment.Center;
            info.AddChild(new Label { Text = name, ClipText = true, StyleClasses = { StyleClass.FontLarge } });

            if (loaded)
            {
                var size = blueprint!.Size;
                info.AddChild(new Label
                {
                    Text = $"{BlueprintSystem.Plural(blueprint.Entries.Count)} · {size.X}×{size.Y}",
                    StyleClasses = { StyleClass.LabelSubText },
                });

                var materials = blueprints.MaterialsOf(blueprint);
                if (materials.Count > 0)
                    info.AddChild(MaterialsLabel(materials));
            }
            else
            {
                info.AddChild(new Label { Text = "Файл не читается", StyleClasses = { StyleClass.LabelWeak } });
            }

            _paste = new Button { Disabled = !loaded };
            _paste.OnPressed += _ =>
            {
                if (_active)
                    blueprints.Cancel();
                else
                    blueprints.BeginPaste(name);
            };

            var export = new Button
            {
                Text = "В буфер",
                Disabled = !loaded,
                ToolTip = "Скопировать чертёж текстом, чтобы переслать",
            };
            export.OnPressed += _ => window.Export(name);

            var delete = new ConfirmButton { Text = "Удалить", ConfirmationText = "Точно?" };
            delete.AddStyleClass(StyleClass.Negative);
            delete.OnPressed += _ => blueprints.Delete(name);

            var lower = Horizontal(4);
            lower.AddChild(export);
            lower.AddChild(delete);

            var buttons = Vertical(4);
            buttons.VerticalAlignment = VAlignment.Center;
            buttons.AddChild(_paste);
            buttons.AddChild(lower);

            var row = Horizontal(10);
            row.Margin = new Thickness(6);
            if (loaded)
                row.AddChild(new BlueprintPreview(blueprint!.Size, blueprints.IconsOf(blueprint), PreviewSize));
            row.AddChild(info);
            row.AddChild(buttons);

            _box = new StyleBoxFlat
            {
                BackgroundColor = CardBackground,
                BorderThickness = new Thickness(1),
            };

            Root = new PanelContainer { PanelOverride = _box };
            Root.AddChild(row);

            SetActive(false);
        }

        public void SetActive(bool active)
        {
            _active = active;
            _box.BorderColor = active ? Accent : CardBorder;
            _paste.Text = active ? "Хватит" : "Поставить";
            _paste.RemoveStyleClass(active ? StyleClass.Positive : StyleClass.Negative);
            _paste.AddStyleClass(active ? StyleClass.Negative : StyleClass.Positive);
        }

        private static Label MaterialsLabel(List<BlueprintMaterial> materials)
        {
            var inline = new StringBuilder();
            var full = new StringBuilder("Нужно на весь чертёж:");

            for (var i = 0; i < materials.Count; i++)
            {
                var (name, amount) = materials[i];
                full.Append('\n').Append(name).Append(": ").Append(amount);

                if (i >= MaterialsInline)
                    continue;

                if (i > 0)
                    inline.Append(" · ");
                inline.Append(name).Append(": ").Append(amount);
            }

            if (materials.Count > MaterialsInline)
                inline.Append(" · ещё ").Append(materials.Count - MaterialsInline);

            return new Label
            {
                Text = inline.ToString(),
                ToolTip = full.ToString(),
                ClipText = true,
                MouseFilter = MouseFilterMode.Pass,
                StyleClasses = { StyleClass.LabelWeak },
            };
        }
    }

    private static Label Heading(string text) => new() { Text = text, StyleClasses = { StyleClass.LabelHeading } };

    private static Label Hint(string text) => new() { Text = text, StyleClasses = { StyleClass.LabelSubText } };

    private static BoxContainer Vertical(int separation) => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical,
        SeparationOverride = separation,
    };

    private static BoxContainer Horizontal(int separation) => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Horizontal,
        SeparationOverride = separation,
    };
}
