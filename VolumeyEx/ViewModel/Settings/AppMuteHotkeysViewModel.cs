#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VolumeyEx.Controls;
using VolumeyEx.DataProvider;
using VolumeyEx.Helper;
using VolumeyEx.Model;
using VolumeyEx.Services;

namespace VolumeyEx.ViewModel.Settings;

/// <summary>
/// The "mute all apps" and "one app of the round at a time" hotkeys of the default device (see <see cref="AppMuteSwitcher"/>),
/// and the list of the round's apps. Each hotkey is turned on and off on its own; the apps get their states back when the hotkey
/// that changed them is turned off, when the default device changes and when VolumeyEx exits.
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

	/// <summary>The apps of the solo round, in the order it goes through them</summary>
	public ObservableCollection<SoloAppItem> SoloApps { get; } = new ObservableCollection<SoloAppItem>();

	/// <summary>The open apps the "Add app" dialog offers: the default device's apps that aren't in the round (see <see cref="PrepareAddSoloApp"/>)</summary>
	public ObservableCollection<AudioProcessModel> AddableSoloApps { get; } = new ObservableCollection<AudioProcessModel>();

	/// <summary>The app picked in the "Add app" dialog</summary>
	[ObservableProperty]
	public partial AudioProcessModel? SelectedSoloApp { get; set; }

	private readonly AppMuteSwitcher switcher = new AppMuteSwitcher();
	//what the OSD shows for mute all, and for the round's step where all its apps are muted
	private readonly AllAppsSession allApps;
	private readonly AllAppsSession roundApps;
	private readonly IDeviceProvider deviceProvider;
	private readonly INotificationService notifications;
	private readonly IAppShell shell;

	internal AppMuteHotkeysViewModel(IDeviceProvider deviceProvider, IHotkeyService hotkeys, INotificationService notifications, IAppShell shell)
		: base(hotkeys)
	{
		this.deviceProvider = deviceProvider;
		this.notifications = notifications;
		this.shell = shell;
		allApps = new AllAppsSession("VolumeyEx.AllApps", "AppHotkeys_AllApps", () => deviceProvider.DefaultDevice?.Master, () => switcher.AllMuted, ToggleMuteAll);
		//its mute button moves the round on, like the hotkey
		roundApps = new AllAppsSession("VolumeyEx.RoundApps", "AppHotkeys_SoloApps", () => deviceProvider.DefaultDevice?.Master,
			() => switcher.SoloAllMuted, () => SoloNext());
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
		foreach(string name in settings.SoloApps.Distinct())
			SoloApps.Add(CreateItem(name));
		switcher.SetSoloApps(SoloApps.Select(app => app.Name).ToList());
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
		=> shell.Dispatcher.Invoke(() =>
		{
			switcher.Attach(newDevice?.Processes);
			RefreshIcons();
		});

	/// <summary>Fills the "Add app" dialog's list: the default device's open apps that aren't in the round, one per name</summary>
	internal void PrepareAddSoloApp()
	{
		SelectedSoloApp = null;
		AddableSoloApps.Clear();
		foreach(var app in deviceProvider.DefaultDevice?.Processes.ToList() ?? new())
		{
			if(SoloApps.All(item => item.Name != app.Name) && AddableSoloApps.All(added => added.Name != app.Name))
				AddableSoloApps.Add(app);
		}
		RefreshIcons();
	}

	/// <summary>Adds the app picked in the "Add app" dialog at the end of the round</summary>
	/// <returns>False if no app is picked</returns>
	internal bool AddSoloApp()
	{
		if(SelectedSoloApp is not { } app || SoloApps.Any(item => item.Name == app.Name))
			return false;
		SoloApps.Add(CreateItem(app.Name, app.IconSource));
		SaveSoloApps();
		return true;
	}

	private void RemoveSoloApp(SoloAppItem item)
	{
		if(SoloApps.Remove(item))
			SaveSoloApps();
	}

	/// <param name="offset">-1: up (earlier in the round), 1: down</param>
	private void MoveSoloApp(SoloAppItem item, int offset)
	{
		int from = SoloApps.IndexOf(item);
		int to = from + offset;
		if(from < 0 || to < 0 || to >= SoloApps.Count)
			return;
		SoloApps.Move(from, to);
		SaveSoloApps();
	}

	private bool CanMove(SoloAppItem item, int offset)
	{
		int to = SoloApps.IndexOf(item) + offset;
		return to >= 0 && to < SoloApps.Count;
	}

	private void SaveSoloApps()
	{
		var names = SoloApps.Select(item => item.Name).ToList();
		//a list is saved only when it's replaced
		SettingsProvider.Settings.Hotkeys.SoloApps = names;
		switcher.SetSoloApps(names);
		foreach(var item in SoloApps)
			item.RefreshCommands();
		Logger.Info($"Solo apps: [{string.Join("], [", names)}]");
	}

	private SoloAppItem CreateItem(string name, ImageSource? icon = null)
		=> new SoloAppItem(name, icon ?? IconOf(name), RemoveSoloApp, MoveSoloApp, CanMove);

	//the icon of an open app of that name, else none (the app isn't open: it gets its icon when it is)
	private ImageSource? IconOf(string name)
		=> deviceProvider.DefaultDevice?.Processes.ToList().FirstOrDefault(app => app.Name == name)?.IconSource;

	private void RefreshIcons()
	{
		foreach(var item in SoloApps)
		{
			if(item.IconSource == null)
				item.IconSource = IconOf(item.Name);
		}
	}

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
				SoloNext();
		}
		catch(Exception e)
		{
			Logger.Error("Failed to change the apps' mute states", e);
		}
	}

	//from the hotkey and from the OSD's mute button of the round's apps
	private void SoloNext()
	{
		var app = switcher.SoloNext(ForegroundMatcher());
		IManagedMasterAudioSession? shown = null;
		if(app != null)
		{
			Logger.Info($"Only [{app.Name}] is audible");
			shown = app;
		}
		else if(switcher.SoloAllMuted)
		{
			Logger.Info("All apps of the round are muted");
			roundApps.Refresh();
			shown = roundApps;
		}
		else
			Logger.Info("Solo: none of the round's apps is open");
		if(shown != null && SettingsProvider.Settings.Notifications.Enabled && deviceProvider.DefaultDevice != null)
			notifications.ShowNotification(shown);
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

/// <summary>
/// An app of the solo round in the settings list, with its buttons: up, down and remove
/// </summary>
public sealed partial class SoloAppItem : ObservableObject
{
	public string Name { get; }

	/// <summary>The icon of the open app, null while it isn't open</summary>
	[ObservableProperty]
	public partial ImageSource? IconSource { get; set; }

	public IRelayCommand MoveUpCommand { get; }
	public IRelayCommand MoveDownCommand { get; }
	public ICommand RemoveCommand { get; }

	internal SoloAppItem(string name, ImageSource? icon, Action<SoloAppItem> remove, Action<SoloAppItem, int> move, Func<SoloAppItem, int, bool> canMove)
	{
		Name = name;
		IconSource = icon;
		MoveUpCommand = new RelayCommand(() => move(this, -1), () => canMove(this, -1));
		MoveDownCommand = new RelayCommand(() => move(this, 1), () => canMove(this, 1));
		RemoveCommand = new RelayCommand(() => remove(this));
	}

	/// <summary>The first app can't move up, the last one can't move down</summary>
	internal void RefreshCommands()
	{
		MoveUpCommand.NotifyCanExecuteChanged();
		MoveDownCommand.NotifyCanExecuteChanged();
	}
}
