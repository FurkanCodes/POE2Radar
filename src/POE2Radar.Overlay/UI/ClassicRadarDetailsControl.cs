using System.Globalization;
using POE2Radar.Core.Game;
using POE2Radar.Overlay.Web;

namespace POE2Radar.Overlay.UI;

/// <summary>
/// Classic, table-first editor for radar data that cannot be represented by a PropertyGrid.
/// It edits the same thread-safe stores used by the renderer and the modern settings window.
/// </summary>
internal sealed class ClassicRadarDetailsControl : UserControl
{
    private readonly DisplayRules _rules;
    private readonly HiddenEntities _hidden;
    private readonly DataGridView _ruleGrid;
    private readonly DataGridView _entityGrid;
    private readonly DataGridView _hiddenGrid;
    private readonly TextBox _ruleSearch;
    private readonly TextBox _hiddenPattern;
    private bool _loading;

    public ClassicRadarDetailsControl(DisplayRules rules, HiddenEntities hidden)
    {
        _rules = rules;
        _hidden = hidden;
        Dock = DockStyle.Fill;
        Font = ClassicUiPalette.UiFont;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        _ruleGrid = CreateGrid();
        _entityGrid = CreateGrid(readOnly: true);
        _hiddenGrid = CreateGrid(readOnly: true);
        _ruleSearch = new TextBox { Width = 220 };
        _hiddenPattern = new TextBox { Width = 280 };

        tabs.TabPages.Add(CreateRulesPage());
        tabs.TabPages.Add(CreateEntitiesPage());
        tabs.TabPages.Add(CreateHiddenPage());
        Controls.Add(tabs);

        BuildRuleColumns();
        BuildEntityColumns();
        BuildHiddenColumns();
        ReloadRules();
        ReloadHidden();
    }

    private TabPage CreateRulesPage()
    {
        var page = new TabPage("Display Rules") { Padding = new Padding(6) };
        var tools = CreateToolStrip();
        tools.Items.Add(new ToolStripLabel("Find:"));
        tools.Items.Add(new ToolStripControlHost(_ruleSearch));
        tools.Items.Add(CreateButton("Add", (_, _) =>
        {
            _rules.Add(new DisplayRule { Name = "New rule" });
            ReloadRules();
            SelectRule(_rules.Count - 1);
        }));
        tools.Items.Add(CreateButton("Duplicate", (_, _) => DuplicateSelectedRule()));
        tools.Items.Add(CreateButton("Delete", (_, _) => DeleteSelectedRule()));
        tools.Items.Add(CreateButton("Move Up", (_, _) => MoveSelectedRule(-1)));
        tools.Items.Add(CreateButton("Move Down", (_, _) => MoveSelectedRule(1)));
        tools.Items.Add(CreateButton("Refresh", (_, _) => ReloadRules()));
        _ruleSearch.TextChanged += (_, _) => ReloadRules();
        _ruleGrid.CellValueChanged += RuleGridCellValueChanged;
        _ruleGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_ruleGrid.IsCurrentCellDirty)
                _ruleGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        page.Controls.Add(_ruleGrid);
        page.Controls.Add(tools);
        return page;
    }

    private TabPage CreateEntitiesPage()
    {
        var page = new TabPage("Entities in Zone") { Padding = new Padding(6) };
        var note = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "Live read-only radar details. Updates while the settings window is open.",
            TextAlign = ContentAlignment.MiddleLeft,
        };
        page.Controls.Add(_entityGrid);
        page.Controls.Add(note);
        return page;
    }

    private TabPage CreateHiddenPage()
    {
        var page = new TabPage("Never Show") { Padding = new Padding(6) };
        var tools = CreateToolStrip();
        tools.Items.Add(new ToolStripLabel("Metadata text or glob:"));
        tools.Items.Add(new ToolStripControlHost(_hiddenPattern));
        tools.Items.Add(CreateButton("Add", (_, _) =>
        {
            if (_hidden.Add(_hiddenPattern.Text))
            {
                _hiddenPattern.Clear();
                ReloadHidden();
            }
        }));
        tools.Items.Add(CreateButton("Remove Selected", (_, _) =>
        {
            if (_hiddenGrid.CurrentRow?.Cells["Pattern"].Value is string pattern && _hidden.Remove(pattern))
                ReloadHidden();
        }));
        tools.Items.Add(CreateButton("Refresh", (_, _) => ReloadHidden()));
        page.Controls.Add(_hiddenGrid);
        page.Controls.Add(tools);
        return page;
    }

    private static ToolStrip CreateToolStrip()
        => new()
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
            Padding = new Padding(2),
        };

    private static ToolStripButton CreateButton(string text, EventHandler click)
    {
        var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        button.Click += click;
        return button;
    }

    private static DataGridView CreateGrid(bool readOnly = false)
        => new()
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToOrderColumns = true,
            AutoGenerateColumns = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            ReadOnly = readOnly,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };

    private void BuildRuleColumns()
    {
        AddText(_ruleGrid, "Order", "#", 42, readOnly: true);
        AddCheck(_ruleGrid, "Enabled", "On", 38);
        AddText(_ruleGrid, "Name", "Rule Name", 150);
        AddText(_ruleGrid, "Categories", "Categories", 90);
        AddText(_ruleGrid, "Match", "Metadata Match", 145);
        AddText(_ruleGrid, "Rarity", "Rarity", 62);
        AddText(_ruleGrid, "Reaction", "Reaction", 68);
        AddText(_ruleGrid, "Life", "Life", 55);
        AddText(_ruleGrid, "Chest", "Chest", 65);
        AddText(_ruleGrid, "Poi", "POI", 45);
        AddCheck(_ruleGrid, "Hide", "Hide", 42);
        AddCheck(_ruleGrid, "Navigable", "Path", 42);
        AddText(_ruleGrid, "Shape", "Icon", 62);
        AddText(_ruleGrid, "Color", "Color", 68);
        AddText(_ruleGrid, "Opacity", "Alpha", 52);
        AddText(_ruleGrid, "Size", "Size", 48);
        AddText(_ruleGrid, "Label", "Label", 100);
        AddCheck(_ruleGrid, "HideLabel", "No Label", 58);
    }

    private void BuildEntityColumns()
    {
        AddText(_entityGrid, "Id", "ID", 72, true);
        AddText(_entityGrid, "Category", "Category", 75, true);
        AddText(_entityGrid, "Name", "Name / Metadata", 250, true);
        AddText(_entityGrid, "Rarity", "Rarity", 65, true);
        AddText(_entityGrid, "Reaction", "Reaction", 70, true);
        AddText(_entityGrid, "Life", "Life", 75, true);
        AddText(_entityGrid, "Distance", "Distance", 65, true);
        AddText(_entityGrid, "Poi", "POI", 38, true);
        AddText(_entityGrid, "Rule", "Matched Rule", 155, true);
    }

    private void BuildHiddenColumns()
        => AddText(_hiddenGrid, "Pattern", "Hidden Metadata Pattern", 520, true);

    private static void AddText(
        DataGridView grid,
        string name,
        string header,
        int width,
        bool readOnly = false)
        => grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            ReadOnly = readOnly,
            SortMode = DataGridViewColumnSortMode.Automatic,
        });

    private static void AddCheck(DataGridView grid, string name, string header, int width)
        => grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
        });

    private void ReloadRules()
    {
        var selectedIndex = SelectedRuleIndex();
        var find = _ruleSearch.Text.Trim();
        _loading = true;
        try
        {
            _ruleGrid.Rows.Clear();
            var rules = _rules.All;
            for (var index = 0; index < rules.Count; index++)
            {
                var rule = rules[index];
                var searchable = $"{rule.Name} {string.Join(' ', rule.Categories)} {string.Join(' ', rule.Match)}";
                if (find.Length > 0 && !searchable.Contains(find, StringComparison.OrdinalIgnoreCase))
                    continue;
                var row = _ruleGrid.Rows[_ruleGrid.Rows.Add(
                    index + 1,
                    rule.Enabled,
                    rule.Name,
                    Join(rule.Categories),
                    Join(rule.Match),
                    rule.Rarity ?? "",
                    rule.Reaction ?? "",
                    rule.Life ?? "",
                    rule.Chest ?? "",
                    rule.Poi ?? "",
                    rule.Hide,
                    rule.Navigable,
                    rule.Shape,
                    rule.Color,
                    rule.Opacity.ToString("0.##", CultureInfo.InvariantCulture),
                    rule.Size.ToString("0.##", CultureInfo.InvariantCulture),
                    rule.Label ?? "",
                    rule.HideLabel)];
                row.Tag = index;
            }
        }
        finally
        {
            _loading = false;
        }
        SelectRule(selectedIndex);
    }

    private void RuleGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || e.RowIndex < 0 || _ruleGrid.Rows[e.RowIndex].Tag is not int index) return;
        var row = _ruleGrid.Rows[e.RowIndex];
        var old = _rules.All.ElementAtOrDefault(index);
        if (old is null) return;
        var updated = new DisplayRule
        {
            Enabled = Bool(row, "Enabled"),
            Name = CellText(row, "Name"),
            Categories = Split(CellText(row, "Categories")),
            Match = Split(CellText(row, "Match")),
            Rarity = NullIfEmpty(CellText(row, "Rarity")),
            Reaction = NullIfEmpty(CellText(row, "Reaction")),
            Life = NullIfEmpty(CellText(row, "Life")),
            Chest = NullIfEmpty(CellText(row, "Chest")),
            Poi = NullIfEmpty(CellText(row, "Poi")),
            Hide = Bool(row, "Hide"),
            Navigable = Bool(row, "Navigable"),
            Shape = CellText(row, "Shape", "Circle"),
            Color = CellText(row, "Color", "#FFFFFF"),
            Opacity = Float(row, "Opacity", old.Opacity),
            Size = Float(row, "Size", old.Size),
            Sprite = old.Sprite,
            Label = NullIfEmpty(CellText(row, "Label")),
            HideLabel = Bool(row, "HideLabel"),
        };
        _rules.Update(index, updated);
    }

    private void DuplicateSelectedRule()
    {
        var index = SelectedRuleIndex();
        var source = _rules.All.ElementAtOrDefault(index);
        if (source is null) return;
        var copy = Clone(source);
        copy.Name = $"{copy.Name} copy";
        var list = _rules.All.ToList();
        list.Insert(index + 1, copy);
        _rules.Replace(list);
        ReloadRules();
        SelectRule(index + 1);
    }

    private void DeleteSelectedRule()
    {
        var index = SelectedRuleIndex();
        if (index < 0) return;
        _rules.RemoveAt(index);
        ReloadRules();
        SelectRule(Math.Min(index, _rules.Count - 1));
    }

    private void MoveSelectedRule(int delta)
    {
        var index = SelectedRuleIndex();
        if (index < 0) return;
        var target = Math.Clamp(index + delta, 0, _rules.Count - 1);
        _rules.Move(index, target);
        ReloadRules();
        SelectRule(target);
    }

    private int SelectedRuleIndex()
        => _ruleGrid.CurrentRow?.Tag is int index ? index : -1;

    private void SelectRule(int index)
    {
        if (index < 0) return;
        foreach (DataGridViewRow row in _ruleGrid.Rows)
        {
            if (row.Tag is not int rowIndex || rowIndex != index) continue;
            row.Selected = true;
            _ruleGrid.CurrentCell = row.Cells[Math.Min(2, row.Cells.Count - 1)];
            break;
        }
    }

    private void ReloadHidden()
    {
        _hiddenGrid.Rows.Clear();
        foreach (var pattern in _hidden.All)
            _hiddenGrid.Rows.Add(pattern);
    }

    public void UpdateContext(RenderContext context)
    {
        if (!Visible) return;
        var selectedId = _entityGrid.CurrentRow?.Cells["Id"].Value?.ToString();
        _entityGrid.SuspendLayout();
        try
        {
            _entityGrid.Rows.Clear();
            foreach (var entity in context.Entities.OrderBy(e => e.Category).ThenBy(e => e.Metadata))
            {
                var distance = System.Numerics.Vector2.Distance(entity.Grid, context.PlayerGrid);
                var rule = _rules.Resolve(entity);
                var name = string.IsNullOrWhiteSpace(entity.ItemName)
                    ? ShortName(entity.Metadata)
                    : entity.ItemName;
                var rowIndex = _entityGrid.Rows.Add(
                    entity.Id,
                    entity.Category,
                    name,
                    entity.Rarity,
                    entity.IsFriendly ? "Friendly" : "Hostile",
                    entity.HasLife ? $"{entity.HpCur}/{entity.HpMax}" : "—",
                    distance.ToString("0.0", CultureInfo.InvariantCulture),
                    entity.Poi ? "Yes" : "",
                    rule?.Name ?? "(not displayed)");
                if (string.Equals(entity.Id.ToString(), selectedId, StringComparison.Ordinal))
                    _entityGrid.Rows[rowIndex].Selected = true;
            }
        }
        finally
        {
            _entityGrid.ResumeLayout();
        }
    }

    private static string ShortName(string metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return "(unknown)";
        var slash = Math.Max(metadata.LastIndexOf('/'), metadata.LastIndexOf('\\'));
        return slash >= 0 && slash + 1 < metadata.Length ? metadata[(slash + 1)..] : metadata;
    }

    private static DisplayRule Clone(DisplayRule source)
        => new()
        {
            Enabled = source.Enabled,
            Name = source.Name,
            Categories = source.Categories.ToList(),
            Match = source.Match.ToList(),
            Rarity = source.Rarity,
            Reaction = source.Reaction,
            Life = source.Life,
            Chest = source.Chest,
            Poi = source.Poi,
            Hide = source.Hide,
            Shape = source.Shape,
            Color = source.Color,
            Opacity = source.Opacity,
            Size = source.Size,
            Sprite = source.Sprite,
            Label = source.Label,
            HideLabel = source.HideLabel,
            Navigable = source.Navigable,
        };

    private static string Join(IEnumerable<string> values) => string.Join(", ", values);
    private static List<string> Split(string text)
        => text.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static string CellText(DataGridViewRow row, string name, string fallback = "")
        => row.Cells[name].Value?.ToString()?.Trim() is { Length: > 0 } value ? value : fallback;
    private static bool Bool(DataGridViewRow row, string name)
        => row.Cells[name].Value is true;
    private static float Float(DataGridViewRow row, string name, float fallback)
        => float.TryParse(CellText(row, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
}
