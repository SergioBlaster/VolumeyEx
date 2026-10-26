#nullable enable
using VolumeyEx.ViewModel.Settings;

namespace VolumeyEx.ViewModel.Pages;

/// <summary>
/// The App hotkeys settings page: the foreground app's hotkeys, mute all and the solo round, and the per-app list
/// </summary>
internal sealed class AppHotkeysPageViewModel
{
	/// <summary>For the page's hotkey boxes</summary>
	public IHotkeyService Hotkeys { get; }

	/// <summary>For the hotkey mode note</summary>
	public HotkeyModeViewModel HotkeyMode { get; }

	public ForegroundWindowVolumeViewModel Foreground { get; }
	public AppsHotkeysViewModel AppHotkeys { get; }

	/// <summary>Mute all apps and the solo round</summary>
	public AppMuteHotkeysViewModel AppMute { get; }

	internal AppHotkeysPageViewModel(IHotkeyService hotkeys, HotkeyModeViewModel hotkeyMode, ForegroundWindowVolumeViewModel foreground,
		AppsHotkeysViewModel appHotkeys, AppMuteHotkeysViewModel appMute)
	{
		Hotkeys = hotkeys;
		HotkeyMode = hotkeyMode;
		Foreground = foreground;
		AppHotkeys = appHotkeys;
		AppMute = appMute;
	}
}
