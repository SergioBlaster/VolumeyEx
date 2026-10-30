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
/// A group of apps for the OSD: "All apps" (mute all) or "Apps in the round" (the solo round's step where all of them are muted).
/// It shows the default device's icon and volume, muted while the group is muted. Like every OSD it's usable: the mute button
/// changes the group's state, the bar changes the device's volume.
/// </summary>
internal sealed class AllAppsSession : IManagedMasterAudioSession
{
	private readonly string nameKey;
	private readonly Func<MasterSessionModel?> getMaster;
	private readonly Func<bool> getMuted;
	private readonly Action toggle;

	/// <param name="id">The OSD's own id</param>
	/// <param name="nameKey">The resource of the name screen readers read</param>
	/// <param name="getMaster">The default device's master session, null without a device</param>
	/// <param name="getMuted">Whether the group is muted</param>
	/// <param name="toggle">What the mute button does</param>
	internal AllAppsSession(string id, string nameKey, Func<MasterSessionModel?> getMaster, Func<bool> getMuted, Action toggle)
	{
		Id = id;
		this.nameKey = nameKey;
		this.getMaster = getMaster;
		this.getMuted = getMuted;
		this.toggle = toggle;
		MuteCommand = new ActionCommand(() => IsMuted = !IsMuted);
	}

	/// <summary>The name screen readers read for the OSD</summary>
	public string Name
	{
		get => TranslationSource.Instance[nameKey] ?? string.Empty;
		set { }
	}

	public bool IsMuted
	{
		get => getMuted();
		set
		{
			if(value != getMuted())
			{
				toggle();
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

	//one OSD for the group, whichever device is the default
	public string Id { get; }
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
