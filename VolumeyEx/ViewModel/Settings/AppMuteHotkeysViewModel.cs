#nullable enable
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using VolumeyEx.Controls;
using VolumeyEx.DataProvider;
using VolumeyEx.Helper;
using VolumeyEx.Model;
using VolumeyEx.Services;

namespace VolumeyEx.ViewModel.Settings;

/// <summary>
/// The "mute all apps" and "only one playing app" hotkeys of the default device (see <see cref="AppMuteSwitcher"/>).
/// Each is turned on and off on its own; the apps get their states back when the hotkey that changed them is turned off,
/// when the default device changes and when VolumeyEx exits.
/// </summary>
public sealed partial class AppMuteHotkeysViewModel : HotkeyViewModel
{
	[ObservableProperty]
	public partial HotKey MuteAllKey { get; set; }

	[ObservableProperty]
	public partial HotKey SoloKey { get; set; }

	/// <summary>Why turning the mute all hotkey on failed</summary>
	[ObservableProperty]
	public partial string MuteAllError { get; private set; }

	/// <summary>Why turning the solo hotkey on failed</summary>
	[ObservableProperty]
	public partial string SoloError { get; private set; }

	private ErrorMessageType muteAllErrorType = ErrorMessageType.None;
	private ErrorMessageType soloErrorType = ErrorMessageType.None;

	partial void OnMuteAllKeyChanged(HotKey value) => SetMuteAllError(ErrorMessageType.None);

	partial void OnSoloKeyChanged(HotKey value) => SetSoloError(ErrorMessageType.None);

	private bool muteAllRegistered;
	public bool MuteAllRegistered
	{
		get => muteAllRegistered;
		set
		{
			if(value && !muteAllRegistered)
				muteAllRegistered = TryRegister(MuteAllKey, SetMuteAllError, key => SettingsProvider.Settings.Hotkeys.MuteAllApps = key, "mute all apps");
			else if(!value && muteAllRegistered)
			{
				muteAllRegistered = false;
				Unregister(MuteAllKey, SetMuteAllError, () => SettingsProvider.Settings.Hotkeys.MuteAllApps = null);
				//only what mute all changed: a solo round goes on
				if(switcher.AllMuted)
					switcher.RestoreMuteAll();
			}
			OnPropertyChanged();
		}
	}

	private bool soloRegistered;
	public bool SoloRegistered
	{
		get => soloRegistered;
		set
		{
			if(value && !soloRegistered)
				soloRegistered = TryRegister(SoloKey, SetSoloError, key => SettingsProvider.Settings.Hotkeys.SoloPlayingApp = key, "solo app");
			else if(!value && soloRegistered)
			{
				soloRegistered = false;
				Unregister(SoloKey, SetSoloError, () => SettingsProvider.Settings.Hotkeys.SoloPlayingApp = null);
				//only what the round changed: while mute all is on the apps stay muted
				if(switcher.SoloOn)
					switcher.RestoreSolo();
			}
			OnPropertyChanged();
		}
	}

	private readonly AppMuteSwitcher switcher = new AppMuteSwitcher();
	//what the OSD shows for mute all
	private readonly AllAppsSession allApps;
	private readonly IDeviceProvider deviceProvider;
	private readonly INotificationService notifications;
	private readonly IAppShell shell;

	internal AppMuteHotkeysViewModel(IDeviceProvider deviceProvider, IHotkeyService hotkeys, INotificationService notifications, IAppShell shell)
		: base(hotkeys)
	{
		this.deviceProvider = deviceProvider;
		this.notifications = notifications;
		this.shell = shell;
		allApps = new AllAppsSession(() => deviceProvider.DefaultDevice?.Master, () => switcher.AllMuted, ToggleMuteAll);
		ErrorDictionary.LanguageChanged += () =>
		{
			SetMuteAllError(muteAllErrorType);
			SetSoloError(soloErrorType);
		};

		switcher.Attach(deviceProvider.DefaultDevice?.Processes);
		deviceProvider.DefaultDeviceChanged += OnDefaultDeviceChanged;
		hotkeys.HotkeyPressed += OnHotkeyPressed;
		//Windows keeps the mute of an app, also after it exits: VolumeyEx doesn't leave apps muted behind
		shell.Exiting += switcher.Detach;
		shell.SessionEnding += switcher.Detach;

		var settings = SettingsProvider.Settings.Hotkeys;
		if(settings.MuteAllApps is HotKey muteAll)
		{
			MuteAllKey = muteAll;
			muteAllRegistered = true;
		}
		if(settings.SoloPlayingApp is HotKey solo)
		{
			SoloKey = solo;
			soloRegistered = true;
		}
		if(muteAllRegistered || soloRegistered)
			RegisterWhenHotkeysActive(RegisterLoadedHotkeys);
	}

	private void RegisterLoadedHotkeys()
	{
		//a saved hotkey that fails now (e.g. another app took the combination) shows an error; it stays saved and is tried again on the next start
		if(muteAllRegistered && !hotkeys.RegisterHotkey(MuteAllKey))
			SetMuteAllError(ErrorMessageType.OpenReg);
		if(soloRegistered && !hotkeys.RegisterHotkey(SoloKey))
			SetSoloError(ErrorMessageType.OpenReg);
	}

	private bool TryRegister(HotKey? key, Action<ErrorMessageType> setError, Action<HotKey> save, string name)
	{
		var error = key == null ? ErrorMessageType.Missing : hotkeys.HotkeyIsValid(key);
		if(error == ErrorMessageType.None && !hotkeys.RegisterHotkey(key))
			//the hotkey service logs why
			error = ErrorMessageType.OpenReg;
		setError(error);
		if(error != ErrorMessageType.None)
			return false;
		save(key!);
		Logger.Info($"Registered {name} hotkey: [{key}]");
		return true;
	}

	private void Unregister(HotKey? key, Action<ErrorMessageType> setError, Action clear)
	{
		try { hotkeys.UnregisterHotkey(key); }
		catch { }
		setError(ErrorMessageType.None);
		clear();
	}

	private void SetMuteAllError(ErrorMessageType type)
	{
		muteAllErrorType = type;
		MuteAllError = type == ErrorMessageType.None ? string.Empty : ErrorDictionary.Instance[type];
	}

	private void SetSoloError(ErrorMessageType type)
	{
		soloErrorType = type;
		SoloError = type == ErrorMessageType.None ? string.Empty : ErrorDictionary.Instance[type];
	}

	//may be raised off the UI thread; the device's app list is used on the UI thread
	private void OnDefaultDeviceChanged(OutputDeviceModel? newDevice)
		=> shell.Dispatcher.Invoke(() => switcher.Attach(newDevice?.Processes));

	//on the UI thread
	private void OnHotkeyPressed(HotKey hotkey)
	{
		try
		{
			if(muteAllRegistered && hotkey.Equals(MuteAllKey))
			{
				ToggleMuteAll();
				ShowAllAppsNotification();
			}
			else if(soloRegistered && hotkey.Equals(SoloKey))
			{
				bool roundWasOn = switcher.SoloOn;
				var app = switcher.SoloNext(ForegroundMatcher());
				if(app != null)
				{
					Logger.Info($"Only [{app.Name}] is audible");
					if(SettingsProvider.Settings.Notifications.Enabled)
						notifications.ShowNotification(app);
				}
				else if(roundWasOn)
				{
					Logger.Info("Solo round ended: the apps got their mute states back");
					//all apps play again
					ShowAllAppsNotification();
				}
				else
					Logger.Info("Solo: no app is playing");
			}
		}
		catch(Exception e)
		{
			Logger.Error("Failed to change the apps' mute states", e);
		}
	}

	//from the hotkey and from the OSD's mute button
	private void ToggleMuteAll()
	{
		bool muted = switcher.ToggleMuteAll();
		Logger.Info(muted ? "Muted all apps" : "Gave the apps their mute states back");
	}

	/// <summary>The OSD of all apps: the default device's icon and volume, muted while mute all is on</summary>
	private void ShowAllAppsNotification()
	{
		if(!SettingsProvider.Settings.Notifications.Enabled || deviceProvider.DefaultDevice == null)
			return;
		allApps.Refresh();
		notifications.ShowNotification(allApps);
	}

	/// <summary>
	/// Whether an app is the foreground app: by process ID, else by the foreground process's executable (read once, only when needed)
	/// </summary>
	private Func<AudioProcessModel, bool> ForegroundMatcher()
	{
		uint processId = 0;
		try { processId = NativeMethods.GetForegroundWindowProcessId(); }
		catch { }
		string? path = null;
		bool pathRead = false;
		return app =>
		{
			if(processId == 0)
				return false;
			if(app.ProcessId == processId)
				return true;
			if(!pathRead)
			{
				path = NativeMethods.GetProcessPath((int)processId, out _);
				pathRead = true;
			}
			return path != null && string.Equals(app.FilePath, path, StringComparison.OrdinalIgnoreCase);
		};
	}
}
