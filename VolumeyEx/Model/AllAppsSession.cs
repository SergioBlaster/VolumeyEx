#nullable enable
using System;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Xaml.Behaviors.Core;
using VolumeyEx.Controls;
using VolumeyEx.Localization;
using VolumeyEx.ViewModel.Settings;

namespace VolumeyEx.Model;

/// <summary>
/// "All apps" for the OSD of the mute all hotkey: the default device's icon and volume, muted while mute all is on.
/// Like every OSD it's usable: the mute button turns mute all on and off, the bar changes the device's volume.
/// </summary>
internal sealed class AllAppsSession : IManagedMasterAudioSession
{
	private readonly Func<MasterSessionModel?> getMaster;
	private readonly Func<bool> getAllMuted;
	private readonly Action toggleMuteAll;

	/// <param name="getMaster">The default device's master session, null without a device</param>
	/// <param name="getAllMuted">Whether mute all is on</param>
	/// <param name="toggleMuteAll">Turns mute all on or off</param>
	internal AllAppsSession(Func<MasterSessionModel?> getMaster, Func<bool> getAllMuted, Action toggleMuteAll)
	{
		this.getMaster = getMaster;
		this.getAllMuted = getAllMuted;
		this.toggleMuteAll = toggleMuteAll;
		MuteCommand = new ActionCommand(() => IsMuted = !IsMuted);
	}

	/// <summary>The name screen readers read for the OSD</summary>
	public string Name
	{
		get => TranslationSource.Instance["AppHotkeys_AllApps"] ?? string.Empty;
		set { }
	}

	public bool IsMuted
	{
		get => getAllMuted();
		set
		{
			if(value != getAllMuted())
			{
				toggleMuteAll();
				Refresh();
			}
		}
	}

	public int Volume
	{
		get => getMaster()?.Volume ?? 0;
		set
		{
			if(getMaster() is { } master && master.Volume != value)
			{
				master.Volume = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Volume)));
			}
		}
	}

	public ImageSource? IconSource
	{
		get => getMaster()?.IconSource;
		set { }
	}

	public ICommand MuteCommand { get; set; }

	//one OSD for it, whichever device is the default
	public string Id => "VolumeyEx.AllApps";
	public uint ProcessId => 0;
	public string FilePath => string.Empty;
	public Guid GroupingParam => Guid.Empty;
	public bool AnyHotkeyRegistered => false;

	/// <summary>Reads the state again, before it's shown: mute all, the device and its volume may have changed</summary>
	internal void Refresh()
	{
		foreach(string property in new[] { nameof(IsMuted), nameof(Volume), nameof(IconSource), nameof(Name) })
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	//nothing of its own to register or release
	public void SetVolume(int newValue, bool notify, Guid guid) => Volume = newValue;
	public void SetMute(bool newState, bool notify, Guid context) => IsMuted = newState;
	public void SetStateMediator(IAudioProcessStateMediator mediator) { }
	public void ResetStateMediator(bool force) { }
	public bool SetVolumeHotkeys(HotKey? volUp, HotKey? volDown) => false;
	public void ResetVolumeHotkeys() { }
	public void Dispose() { }
}
