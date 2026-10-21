using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using NandoHost.Core;

namespace NandoHost.App;

internal sealed class LocalizedValue : INotifyPropertyChanged
{
    private readonly Func<object?> evaluate;
    internal LocalizedValue(Func<object?> evaluate)
    {
        this.evaluate = evaluate;
        WeakEventManager<LanguageState, EventArgs>.AddHandler(LanguageState.Current, nameof(LanguageState.Changed), OnChanged);
    }
    public object? Value => evaluate();
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(object? sender, EventArgs e) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
}

internal static class LocalizedUi
{
    private static readonly Dictionary<(Type, string), DependencyProperty> Properties = new();
    internal static void Set(DependencyObject target, string property, Func<object?> evaluate)
    {
        var key = (target.GetType(), property);
        if (!Properties.TryGetValue(key, out var dependency))
        {
            dependency = DependencyPropertyDescriptor.FromName(property, key.Item1, key.Item1)?.DependencyProperty
                ?? throw new InvalidOperationException("Unknown localized property: " + property);
            Properties[key] = dependency;
        }
        BindingOperations.SetBinding(target, dependency, new Binding(nameof(LocalizedValue.Value)) { Source = new LocalizedValue(evaluate), Mode = BindingMode.OneWay });
    }
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TranslateExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var source = LocalizationKeys.Get(Key);
        return new Binding(nameof(LocalizedValue.Value)) { Source = new LocalizedValue(() => Localizer.Tr(source)), Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
    }
}
