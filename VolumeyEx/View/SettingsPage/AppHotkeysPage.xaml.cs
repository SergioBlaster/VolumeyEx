#nullable enable
using System.Windows;
using log4net;
using VolumeyEx.Helper;
using VolumeyEx.View.DialogContent;
using VolumeyEx.ViewModel.Pages;

namespace VolumeyEx.View.SettingsPage;

public sealed partial class AppHotkeysPage
{
	private static readonly ILog Logger = LogManager.GetLogger(typeof(AppHotkeysPage));

	private readonly AppHotkeysPageViewModel vm;

	internal AppHotkeysPage(AppHotkeysPageViewModel vm)
	{
		this.vm = vm;
		DataContext = vm;
		InitializeComponent();
	}

	private void OnAddAppClick(object sender, RoutedEventArgs e)
	{
		if(Window.GetWindow(this) is not SettingsWindow window)
			return;
		vm.AppHotkeys.PrepareAddApp();
		new AddAppDialog(vm.AppHotkeys) { DialogHostEx = window.DialogHost }.ShowAsync().Forget(Logger, "Failed to show the add app dialog");
	}

	private void OnAddSoloAppClick(object sender, RoutedEventArgs e)
	{
		if(Window.GetWindow(this) is not SettingsWindow window)
			return;
		vm.AppMute.PrepareAddSoloApp();
		new AddSoloAppDialog(vm.AppMute) { DialogHostEx = window.DialogHost }.ShowAsync().Forget(Logger, "Failed to show the add solo app dialog");
	}
}
