#nullable enable
using VolumeyEx.ViewModel.Pages;

namespace VolumeyEx.View.SettingsPage;

public sealed partial class MediaKeysPage
{
	internal MediaKeysPage(MediaKeysPageViewModel vm)
	{
		DataContext = vm;
		InitializeComponent();
	}
}
