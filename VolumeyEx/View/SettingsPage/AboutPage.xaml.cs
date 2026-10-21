#nullable enable
using VolumeyEx.ViewModel.Settings;

namespace VolumeyEx.View.SettingsPage;

public sealed partial class AboutPage
{
	internal AboutPage(AboutViewModel vm)
	{
		DataContext = vm;
		InitializeComponent();
	}
}
