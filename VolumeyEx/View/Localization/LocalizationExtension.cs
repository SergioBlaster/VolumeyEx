#nullable enable
using System.Windows.Data;

namespace VolumeyEx.Localization;

public sealed class LocalizationExtension : Binding
{
	public LocalizationExtension(string name) : base($"[{name}]")
	{
		Mode = BindingMode.OneWay;
		Source = TranslationSource.Instance;
	}
}
