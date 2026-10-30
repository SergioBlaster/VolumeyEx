#nullable enable
using System.Windows;
using System.Windows.Data;
using VolumeyEx.View.Converters;
using VolumeyEx.ViewModel.Settings;
using Wpf.Ui.Controls;

namespace VolumeyEx.View.DialogContent;

/// <summary>
/// App hotkeys page: adds one of the open apps to the solo round. Add only closes the dialog when the app was added.
/// </summary>
public sealed partial class AddSoloAppDialog
{
	private readonly AppMuteHotkeysViewModel vm;

	internal AddSoloAppDialog(AppMuteHotkeysViewModel vm)
	{
		this.vm = vm;
		DataContext = vm;
		//WPF-UI's dialog style is implicit, which doesn't reach a subclass (without it: no dialog chrome, no buttons)
		SetResourceReference(StyleProperty, typeof(ContentDialog));
		InitializeComponent();
	}

	public override void OnApplyTemplate()
	{
		base.OnApplyTemplate();
		//Add is disabled until an app is picked (not through IsPrimaryButtonEnabled: WPF-UI hides the button then)
		if(GetTemplateChild("PrimaryButton") is UIElement add)
		{
			var picked = new MultiBinding { Converter = new AnyPropertyIsNullToEnabledConverter() };
			picked.Bindings.Add(new Binding(nameof(AppMuteHotkeysViewModel.SelectedSoloApp)));
			BindingOperations.SetBinding(add, IsEnabledProperty, picked);
		}
	}

	protected override void OnButtonClick(ContentDialogButton button)
	{
		if(button == ContentDialogButton.Primary && !vm.AddSoloApp())
			return;
		base.OnButtonClick(button);
	}
}
