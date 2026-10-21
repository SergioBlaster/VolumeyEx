#nullable enable
namespace VolumeyEx.ViewModel.Settings;

/// <summary>
/// Who holds a key combination (<see cref="IHotkeyService.FindOwner"/>)
/// </summary>
public enum HotkeyOwner
{
	Nobody,
	/// <summary>Registered by VolumeyEx's blocking hotkey manager</summary>
	VolumeyEx,
	/// <summary>Registered by another app with RegisterHotKey</summary>
	AnotherApp
}
