using System.Collections;
using System.Globalization;
using System.Reflection;

namespace POE2Radar.Overlay.UI;

/// <summary>
/// Reusable classic editor for settings lists, sets, dictionaries, and lists of row objects.
/// This keeps plugin-specific collection settings visible without teaching the shell every model type.
/// </summary>
internal sealed class ClassicCollectionEditorControl : UserControl
{
    private readonly Action _save;
    private readonly ListBox _collections;
    private readonly DataGridView _grid;
    private readonly TextBox _newKey;
    private readonly Label _description;
    private readonly List<CollectionBinding> _bindings = [];
    private bool _loading;

    public ClassicCollectionEditorControl(object target, string rootName, Action save)
    {
        _save = save;
        Dock = DockStyle.Fill;
        Font = ClassicUiPalette.UiFont;

        Discover(target, rootName, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
        _collections = new ListBox
        {
            Dock = DockStyle.Left,
            Width = 255,
            IntegralHeight = false,
            HorizontalScrollbar = true,
        };
        foreach (var binding in _bindings)
            _collections.Items.Add(binding);
        _collections.SelectedIndexChanged += (_, _) => ReloadGrid();

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.Fixed3D,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        _grid.CellValueChanged += GridCellValueChanged;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        _newKey = new TextBox { Width = 190 };
        var tools = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.System,
        };
        tools.Items.Add(new ToolStripLabel("New item/key:"));
        tools.Items.Add(new ToolStripControlHost(_newKey));
        tools.Items.Add(Button("Add", (_, _) => AddItem()));
        tools.Items.Add(Button("Remove", (_, _) => RemoveItem()));
        tools.Items.Add(Button("Up", (_, _) => MoveItem(-1)));
        tools.Items.Add(Button("Down", (_, _) => MoveItem(1)));
        tools.Items.Add(Button("Refresh", (_, _) => ReloadGrid()));

        _description = new Label
        {
            Dock = DockStyle.Top,
            Height = 27,
            Padding = new Padding(5, 0, 0, 0),
            ForeColor = SystemColors.GrayText,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 0, 0) };
        right.Controls.Add(_grid);
        right.Controls.Add(_description);
        right.Controls.Add(tools);
        Controls.Add(right);
        Controls.Add(_collections);
        if (_collections.Items.Count > 0)
            _collections.SelectedIndex = 0;
    }

    public int CollectionCount => _bindings.Count;

    private static ToolStripButton Button(string text, EventHandler click)
    {
        var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        button.Click += click;
        return button;
    }

    private void Discover(object? value, string path, int depth, HashSet<object> visited)
    {
        if (value is null || depth > 4 || IsScalar(value.GetType()) || !visited.Add(value)) return;
        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
            object? child;
            try { child = property.GetValue(value); }
            catch { continue; }
            if (child is null) continue;
            var childPath = $"{path} / {Friendly(property.Name)}";
            if (IsEditableCollection(child))
            {
                _bindings.Add(new CollectionBinding(childPath, child));
                DiscoverCollectionValues(child, childPath, depth + 1, visited);
            }
            else if (property.PropertyType.Namespace?.StartsWith("POE2Radar", StringComparison.Ordinal) == true)
            {
                Discover(child, childPath, depth + 1, visited);
            }
        }
    }

    private void DiscoverCollectionValues(object collection, string path, int depth, HashSet<object> visited)
    {
        if (depth > 4) return;
        if (collection is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
                if (entry.Value is { } value && !IsScalar(value.GetType()))
                    Discover(value, $"{path} [{entry.Key}]", depth + 1, visited);
        }
        else if (collection is IEnumerable enumerable)
        {
            var index = 0;
            foreach (var value in enumerable)
            {
                if (value is not null && !IsScalar(value.GetType()))
                    Discover(value, $"{path} [{index}]", depth + 1, visited);
                index++;
            }
        }
    }

    private static bool IsEditableCollection(object value)
        => value is IDictionary
           || value is IList
           || ImplementsGeneric(value.GetType(), typeof(ISet<>));

    private void ReloadGrid()
    {
        _loading = true;
        try
        {
            _grid.Columns.Clear();
            _grid.Rows.Clear();
            if (_collections.SelectedItem is not CollectionBinding binding) return;
            _description.Text = binding.Path;
            if (binding.Value is IDictionary dictionary)
                LoadDictionary(dictionary);
            else
                LoadSequence(binding.Value);
        }
        finally
        {
            _loading = false;
        }
    }

    private void LoadDictionary(IDictionary dictionary)
    {
        AddTextColumn("Key", "Key", 220, true);
        var valueType = DictionaryValueType(dictionary.GetType()) ?? typeof(string);
        if (IsScalar(valueType))
        {
            AddValueColumn("Value", "Value", valueType, 260);
            foreach (DictionaryEntry entry in dictionary)
                _grid.Rows.Add(Cell(entry.Key), Cell(entry.Value));
            return;
        }

        var properties = EditableScalarProperties(valueType);
        foreach (var property in properties)
            AddValueColumn(property.Name, Friendly(property.Name), property.PropertyType, 120);
        foreach (DictionaryEntry entry in dictionary)
        {
            var cells = new object[properties.Length + 1];
            cells[0] = Cell(entry.Key);
            for (var i = 0; i < properties.Length; i++)
                cells[i + 1] = Cell(properties[i].GetValue(entry.Value));
            var row = _grid.Rows[_grid.Rows.Add(cells)];
            row.Tag = entry.Value;
        }
    }

    private void LoadSequence(object sequence)
    {
        var values = ((IEnumerable)sequence).Cast<object?>().ToList();
        var elementType = SequenceElementType(sequence.GetType())
                          ?? values.FirstOrDefault(v => v is not null)?.GetType()
                          ?? typeof(string);
        if (IsScalar(elementType))
        {
            AddValueColumn("Value", "Value", elementType, 520);
            for (var index = 0; index < values.Count; index++)
            {
                var row = _grid.Rows[_grid.Rows.Add(Cell(values[index]))];
                row.Tag = index;
            }
            return;
        }

        var properties = EditableScalarProperties(elementType);
        foreach (var property in properties)
            AddValueColumn(property.Name, Friendly(property.Name), property.PropertyType, 125);
        for (var index = 0; index < values.Count; index++)
        {
            var item = values[index];
            var cells = properties.Select(p => Cell(p.GetValue(item))).ToArray();
            var row = _grid.Rows[_grid.Rows.Add(cells)];
            row.Tag = new SequenceRow(index, item);
        }
    }

    private void GridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || e.RowIndex < 0 || e.ColumnIndex < 0
            || _collections.SelectedItem is not CollectionBinding binding)
            return;
        var row = _grid.Rows[e.RowIndex];
        var column = _grid.Columns[e.ColumnIndex];
        try
        {
            if (binding.Value is IDictionary dictionary)
            {
                var key = row.Cells["Key"].Value;
                if (key is null) return;
                var valueType = DictionaryValueType(dictionary.GetType()) ?? typeof(string);
                if (IsScalar(valueType))
                    dictionary[key] = ConvertValue(row.Cells["Value"].Value, valueType);
                else if (row.Tag is { } item)
                    SetProperty(item, column.Name, row.Cells[e.ColumnIndex].Value);
            }
            else if (row.Tag is SequenceRow sequenceRow && sequenceRow.Item is { } item)
            {
                SetProperty(item, column.Name, row.Cells[e.ColumnIndex].Value);
            }
            else if (row.Tag is int index)
            {
                ReplaceSequenceValue(binding.Value, index, row.Cells[e.ColumnIndex].Value);
            }
            _save();
        }
        catch (Exception ex)
        {
            System.Media.SystemSounds.Exclamation.Play();
            _description.Text = $"Could not apply value: {ex.Message}";
            ReloadGrid();
        }
    }

    private void AddItem()
    {
        if (_collections.SelectedItem is not CollectionBinding binding) return;
        var keyText = _newKey.Text.Trim();
        try
        {
            if (binding.Value is IDictionary dictionary)
            {
                if (keyText.Length == 0) return;
                var keyType = DictionaryKeyType(dictionary.GetType()) ?? typeof(string);
                var valueType = DictionaryValueType(dictionary.GetType()) ?? typeof(string);
                var key = ConvertValue(keyText, keyType);
                if (key is null || dictionary.Contains(key)) return;
                dictionary.Add(key, DefaultValue(valueType));
            }
            else if (binding.Value is IList list)
            {
                var type = SequenceElementType(binding.Value.GetType()) ?? typeof(string);
                list.Add(keyText.Length > 0 ? ConvertValue(keyText, type) : DefaultValue(type));
            }
            else
            {
                InvokeCollection(binding.Value, "Add", keyText);
            }
            _newKey.Clear();
            _save();
            ReloadGrid();
        }
        catch (Exception ex)
        {
            _description.Text = $"Could not add item: {ex.Message}";
        }
    }

    private void RemoveItem()
    {
        if (_collections.SelectedItem is not CollectionBinding binding || _grid.CurrentRow is not { } row)
            return;
        if (binding.Value is IDictionary dictionary)
        {
            var key = row.Cells["Key"].Value;
            if (key is not null) dictionary.Remove(key);
        }
        else if (binding.Value is IList list)
        {
            var index = RowIndex(row);
            if (index >= 0 && index < list.Count) list.RemoveAt(index);
        }
        else
        {
            InvokeCollection(binding.Value, "Remove", row.Cells[0].Value);
        }
        _save();
        ReloadGrid();
    }

    private void MoveItem(int delta)
    {
        if (_collections.SelectedItem is not CollectionBinding binding
            || binding.Value is not IList list
            || _grid.CurrentRow is not { } row)
            return;
        var from = RowIndex(row);
        var to = Math.Clamp(from + delta, 0, list.Count - 1);
        if (from < 0 || from == to) return;
        var item = list[from];
        list.RemoveAt(from);
        list.Insert(to, item);
        _save();
        ReloadGrid();
        if (to < _grid.Rows.Count) _grid.Rows[to].Selected = true;
    }

    private static int RowIndex(DataGridViewRow row)
        => row.Tag switch
        {
            int index => index,
            SequenceRow sequence => sequence.Index,
            _ => row.Index,
        };

    private static void ReplaceSequenceValue(object sequence, int index, object? raw)
    {
        var type = SequenceElementType(sequence.GetType()) ?? typeof(string);
        var value = ConvertValue(raw, type);
        if (sequence is IList list)
        {
            list[index] = value;
            return;
        }
        var current = ((IEnumerable)sequence).Cast<object?>().ElementAt(index);
        InvokeCollection(sequence, "Remove", current);
        InvokeCollection(sequence, "Add", value);
    }

    private static void InvokeCollection(object collection, string methodName, object? raw)
    {
        var method = collection.GetType().GetMethods()
            .First(m => m.Name == methodName && m.GetParameters().Length == 1);
        var type = method.GetParameters()[0].ParameterType;
        method.Invoke(collection, [ConvertValue(raw, type)]);
    }

    private static void SetProperty(object item, string propertyName, object? raw)
    {
        var property = item.GetType().GetProperty(propertyName);
        if (property?.CanWrite == true)
            property.SetValue(item, ConvertValue(raw, property.PropertyType));
    }

    private void AddValueColumn(string name, string header, Type type, int width)
    {
        DataGridViewColumn column;
        var actual = Nullable.GetUnderlyingType(type) ?? type;
        if (actual == typeof(bool))
            column = new DataGridViewCheckBoxColumn();
        else if (actual.IsEnum)
            column = new DataGridViewComboBoxColumn { DataSource = Enum.GetValues(actual) };
        else
            column = new DataGridViewTextBoxColumn();
        column.Name = name;
        column.HeaderText = header;
        column.Width = width;
        _grid.Columns.Add(column);
    }

    private void AddTextColumn(string name, string header, int width, bool readOnly)
        => _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            ReadOnly = readOnly,
        });

    private static PropertyInfo[] EditableScalarProperties(Type type)
        => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead && p.CanWrite && IsScalar(p.PropertyType))
            .ToArray();

    private static object? DefaultValue(Type type)
    {
        var actual = Nullable.GetUnderlyingType(type) ?? type;
        if (actual == typeof(string)) return "";
        try { return Activator.CreateInstance(actual); }
        catch { return null; }
    }

    private static object Cell(object? value) => value ?? "";

    private static object? ConvertValue(object? raw, Type targetType)
    {
        var actual = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (raw is null || raw is DBNull || string.IsNullOrWhiteSpace(raw.ToString()))
            return Nullable.GetUnderlyingType(targetType) is not null || !actual.IsValueType
                ? null
                : Activator.CreateInstance(actual);
        if (actual.IsInstanceOfType(raw)) return raw;
        if (actual.IsEnum) return Enum.Parse(actual, raw.ToString()!, true);
        return Convert.ChangeType(raw, actual, CultureInfo.InvariantCulture);
    }

    private static bool IsScalar(Type type)
    {
        var actual = Nullable.GetUnderlyingType(type) ?? type;
        return actual.IsPrimitive || actual.IsEnum || actual == typeof(string)
               || actual == typeof(decimal) || actual == typeof(DateTime);
    }

    private static bool ImplementsGeneric(Type type, Type generic)
        => type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == generic);

    private static Type? SequenceElementType(Type type)
        => type.IsArray
            ? type.GetElementType()
            : type.GetInterfaces().Append(type)
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0];

    private static Type? DictionaryKeyType(Type type)
        => DictionaryInterface(type)?.GetGenericArguments()[0];

    private static Type? DictionaryValueType(Type type)
        => DictionaryInterface(type)?.GetGenericArguments()[1];

    private static Type? DictionaryInterface(Type type)
        => type.GetInterfaces().Append(type)
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));

    private static string Friendly(string name)
        => System.Text.RegularExpressions.Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2");

    private sealed record CollectionBinding(string Path, object Value)
    {
        public override string ToString()
        {
            var separator = Path.IndexOf(" / ", StringComparison.Ordinal);
            return separator >= 0 ? Path[(separator + 3)..] : Path;
        }
    }

    private sealed record SequenceRow(int Index, object? Item);
}
