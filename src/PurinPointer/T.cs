using System.Windows.Data;
using System.Windows.Markup;
using PurinPointer.Core;

namespace PurinPointer;

/// <summary>XAML shorthand for a translated string: <c>Text="{local:T section.size}"</c>.
/// The binding follows <see cref="Loc"/>, so labels update when the language changes.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
