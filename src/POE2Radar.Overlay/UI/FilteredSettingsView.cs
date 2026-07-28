using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace POE2Radar.Overlay.UI;

/// <summary>
/// Presents an existing settings object to PropertyGrid without copying it. The descriptors write
/// through to the live object, keeping the WinForms shell a shallow UI seam over the current model.
/// </summary>
internal sealed class FilteredSettingsView : ICustomTypeDescriptor
{
    private readonly object _target;
    private readonly string _category;
    private readonly Func<PropertyDescriptor, bool> _include;

    public FilteredSettingsView(object target, string category, Func<PropertyDescriptor, bool>? include = null)
    {
        _target = target;
        _category = category;
        _include = include ?? (_ => true);
    }

    public AttributeCollection GetAttributes() => TypeDescriptor.GetAttributes(_target);
    public string? GetClassName() => TypeDescriptor.GetClassName(_target);
    public string? GetComponentName() => TypeDescriptor.GetComponentName(_target);
    public TypeConverter GetConverter() => TypeDescriptor.GetConverter(_target);
    public EventDescriptor? GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(_target);
    public PropertyDescriptor? GetDefaultProperty() => TypeDescriptor.GetDefaultProperty(_target);
    public object? GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(_target, editorBaseType);
    public EventDescriptorCollection GetEvents() => TypeDescriptor.GetEvents(_target);
    public EventDescriptorCollection GetEvents(Attribute[]? attributes)
        => attributes is null
            ? TypeDescriptor.GetEvents(_target)
            : TypeDescriptor.GetEvents(_target, attributes);

    public PropertyDescriptorCollection GetProperties()
        => GetProperties(null);

    public PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
    {
        var source = attributes is null
            ? TypeDescriptor.GetProperties(_target)
            : TypeDescriptor.GetProperties(_target, attributes);
        var properties = source.Cast<PropertyDescriptor>()
            .Where(_include)
            .Select(property => new FriendlyPropertyDescriptor(
                property,
                _target,
                SettingsPropertyCatalog.For(_target, _category, property)))
            .OrderBy(property => property.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(property => property.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Cast<PropertyDescriptor>()
            .ToArray();
        return new PropertyDescriptorCollection(properties, readOnly: true);
    }

    public object GetPropertyOwner(PropertyDescriptor? pd) => _target;

    internal static string FriendlyName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            var previous = i > 0 ? name[i - 1] : '\0';
            var next = i + 1 < name.Length ? name[i + 1] : '\0';
            var startsWord =
                char.IsUpper(current) &&
                (char.IsLower(previous) || char.IsDigit(previous)
                 || (char.IsUpper(previous) && char.IsLower(next)));
            var startsNumber = char.IsDigit(current) && !char.IsDigit(previous);
            if (i > 0 && !char.IsWhiteSpace(previous) && (startsWord || startsNumber))
            {
                builder.Append(' ');
            }
            builder.Append(current);
        }

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(builder.ToString());
    }

    private sealed class FriendlyPropertyDescriptor : PropertyDescriptor
    {
        private readonly PropertyDescriptor _inner;
        private readonly object _owner;
        private readonly SettingsPropertyPresentation _presentation;
        private readonly TypeConverter? _converter;

        public FriendlyPropertyDescriptor(
            PropertyDescriptor inner,
            object owner,
            SettingsPropertyPresentation presentation)
            : base(inner)
        {
            _inner = inner;
            _owner = owner;
            _presentation = presentation;
            _converter = presentation.IntegerChoices is null
                ? presentation.IsHotkey
                    ? new SettingsPropertyCatalog.HotkeyValueConverter()
                    : null
                : new SettingsPropertyCatalog.IntegerChoiceConverter(presentation.IntegerChoices);
        }

        public override string DisplayName => _presentation.DisplayName;
        public override string Category => _presentation.Category;
        public override string Description => _presentation.Description;
        public override TypeConverter Converter => _converter ?? _inner.Converter;
        public override Type ComponentType => _inner.ComponentType;
        public override bool IsReadOnly => _inner.IsReadOnly;
        public override Type PropertyType => _inner.PropertyType;
        public override bool CanResetValue(object component) => _inner.CanResetValue(_owner);
        public override object? GetValue(object? component) => _inner.GetValue(_owner);
        public override void ResetValue(object component) => _inner.ResetValue(_owner);
        public override void SetValue(object? component, object? value) => _inner.SetValue(_owner, value);
        public override bool ShouldSerializeValue(object component) => _inner.ShouldSerializeValue(_owner);
    }
}
