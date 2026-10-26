#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;
using log4net;
using NAudio.CoreAudioApi;
using VolumeyEx.DataProvider;
using VolumeyEx.Helper;
using VolumeyEx.Localization;
using VolumeyEx.Model;
using VolumeyEx.ViewModel.Settings;
using Windows.Win32.Foundation;

namespace VolumeyEx.CoreAudioWrapper.Wrapper;

/// <summary>
/// Builds the device, master, process and session models from NAudio objects.
/// </summary>
internal static class AudioModelFactory
{
	private static readonly ILog Logger = LogManager.GetLogger(typeof(AudioModelFactory));

	/// <returns>Null if the device's audio objects can't be read</returns>
	internal static OutputDeviceModel? CreateOutputDeviceModel(AudioDevice device, IDeviceStateNotificationHandler deviceStateNotificationHandler, IHotkeyService hotkeys)
	{
		try
		{
			var sessionManager = device.Device.AudioSessionManager;
			var processes = GetCurrentProcessList(sessionManager.Sessions, hotkeys);
			var sessionProvider = new AudioSessionProvider(sessionManager, hotkeys);
			var master = CreateMaster(device, hotkeys);

			return new OutputDeviceModel(device, deviceStateNotificationHandler, sessionProvider, master, processes);
		}
		catch(Exception e)
		{
			Logger.Error("Failed to create output device model", e);
			return null;
		}
	}

	private static MasterSessionModel CreateMaster(AudioDevice device, IHotkeyService hotkeys)
	{
		try
		{
			var endpointVolume = device.Device.AudioEndpointVolume;

			var volumeHandler = new MasterVolumeNotificationHandler(endpointVolume);
			volumeHandler.RegisterMVolumeNotifications();

			var masterVolume = new MasterAudioSessionVolume(endpointVolume);
			float volume = masterVolume.GetVolume();
			bool muteState = masterVolume.GetMute();
			Icon? icon = device.GetIcon();
			string deviceName = device.GetFriendlyName();
			string deviceDesc = device.GetDeviceDesc();
			string id = device.GetId();

			return new MasterSessionModel(deviceName, deviceDesc, VolumePercent.FromScalar(volume), muteState, id, icon,
										  masterVolume,
										  volumeHandler,
										  hotkeys);
		}
		catch(Exception e)
		{
			Logger.Error("Failed to create master session", e);
			throw;
		}
	}

	private static List<AudioProcessModel> GetCurrentProcessList(SessionCollection sessions, IHotkeyService hotkeys)
	{
		if(sessions == null)
			return new List<AudioProcessModel>();

		return GroupIntoProcesses(sessions.Count, i => sessions[i], CreateSessionModel,
			(AudioSessionModel session, AudioSessionControl control, out bool isSystemSounds) => CreateProcessModel(session, control, hotkeys, out isSystemSounds),
			control => control.Dispose());
	}

	internal delegate AudioProcessModel? ProcessModelFactory<TControl>(AudioSessionModel session, TControl control, out bool isSystemSounds);

	/// <summary>
	/// Builds the app models of a device's existing sessions: sessions of one app (same grouping parameter or file) go into one model,
	/// System Sounds first. A session that doesn't end up in a model is released: a session model registers for Windows' events
	/// when it's created, and an unreleased one would keep that registration (and itself) until VolumeyEx exits.
	/// Generic over the session control so it can be tested without Windows' audio objects.
	/// </summary>
	/// <param name="createSession">takes ownership of the control; null if the session can't be read</param>
	/// <param name="releaseControl">releases a control that no session model took over</param>
	internal static List<AudioProcessModel> GroupIntoProcesses<TControl>(int count, Func<int, TControl> getControl,
		Func<TControl, AudioSessionModel?> createSession, ProcessModelFactory<TControl> createProcess, Action<TControl> releaseControl)
		where TControl : class
	{
		var processes = new List<AudioProcessModel>();
		for(int i = 0; i < count; i++)
		{
			TControl? control = null;
			AudioSessionModel? session = null;
			bool kept = false;
			try
			{
				control = getControl(i);
				session = createSession(control);
				if(session == null)
					continue;

				if(processes.FirstOrDefault(p => BelongsTo(session, p)) is { } process)
				{
					session.Name = process.Name;
					process.AddSession(session);
					kept = true;
				}
				else
				{
					AudioProcessModel? newProcess = createProcess(session, control, out bool isSystemSounds);
					if(newProcess == null)
						continue;

					newProcess.AddSession(session);
					kept = true;
					session.Name = newProcess.Name;

					//put system sounds process on top of the processes list
					if(isSystemSounds)
						processes.Insert(0, newProcess);
					else
						processes.Add(newProcess);
				}
			}
			catch(Exception e)
			{
				Logger.Debug("Skipped a session that couldn't be read", e);
			}
			finally
			{
				//the session model owns its control once it exists
				if(!kept)
				{
					if(session != null)
						session.Dispose();
					else if(control != null)
						releaseControl(control);
				}
			}
		}
		return processes;
	}

	/// <summary>
	/// Whether the session goes into the app's model: Windows gave both the same grouping parameter, or they come from the same
	/// executable under the same app name. A shared executable alone isn't enough: the sessions of WhatsApp and of another app built
	/// on WebView2 both come from msedgewebview2.exe, and Windows' mixer shows them as two apps
	/// </summary>
	internal static bool BelongsTo(AudioSessionModel session, AudioProcessModel process)
		=> process.GroupingParam.Equals(session.GroupingParam)
		   || (Equals(process.FilePath, session.FilePath) && string.Equals(process.Name, session.Name, StringComparison.Ordinal));

	internal static AudioProcessModel? CreateProcessModel(AudioSessionModel session, AudioSessionControl sControl, IHotkeyService hotkeys, out bool isSystemSounds)
	{
		//null if the process can't be opened (e.g. it has exited already)
		Process? proc = null;
		isSystemSounds = false;
		//the app's name, as Windows' mixer shows it (see CreateSessionModel)
		string processName = session.Name;
		//the session's path (see CreateSessionModel), or its process ID when the path couldn't be read
		string? path = Path.IsPathFullyQualified(session.FilePath) ? session.FilePath : null;

		try { proc = Process.GetProcessById((int)session.ProcessId); }
		catch { }

		ImageSource? icon = null;

		try
		{
			if(sControl.IsSystemSoundsSession)
			{
				isSystemSounds = true;
				//check if the system language was changed to get a new localized name for the system sounds session
				var systemLanguage = TranslationSource.GetSystemLanguage();
				var settings = SettingsProvider.Settings;
				string? systemSoundsName = settings.General.SystemSoundsName;
				if(systemSoundsName == null || settings.General.SystemSoundsLanguage != systemLanguage)
				{
					//returns resources "DllPath,index"
					processName = GetSystemSoundsName(sControl);
					settings.General.SystemSoundsLanguage = systemLanguage;
					settings.General.SystemSoundsName = processName;
				}
				else
					processName = systemSoundsName;

				try
				{
					icon = IconHelper.GetIconSource(Environment.ExpandEnvironmentVariables("%SystemRoot%\\System32\\AudioSrv.dll"), Icon.ExtractAssociatedIcon);
				}
				catch(Exception e)
				{
					Logger.Error($"Failed to extract system sounds icon from AudioSrv.dll.", e);
				}
			}
			else
			{
				//on the calling (COM) thread: icon extraction doesn't need the UI thread, and a COM callback must not wait for it
				icon = ExtractSessionIcon(session.ProcessId, path, processName, sControl);
			}
		}
		catch(Exception e)
		{
			Logger.Error($"Failed to create process model from session model, process: [{processName}]", e);
			proc?.Dispose();
			return null;
		}
		//the model takes ownership of the process; the icon is a shared frozen image
		AudioProcessModel model = new AudioProcessModel(session.Volume, session.IsMuted, processName, session.ProcessId, session.FilePath, icon, proc, hotkeys);
		//an app that refuses a handle (e.g. a game guarded by anti-cheat) can't be watched for its exit through the process
		model.WatchExitInProcessList();
		return model;
	}

	/// <summary>
	/// Creates the session model. The model takes ownership of the session control.
	/// </summary>
	/// <returns>Null if the session's volume or events can't be read</returns>
	internal static AudioSessionModel? CreateSessionModel(AudioSessionControl sControl)
	{
		uint processId = sControl.GetProcessID;

		string filePath = processId.ToString();
		string name = filePath;

		//read once here: the app model takes its description and icon from the session's path.
		//The System Sounds session (process 0) has no executable
		int pathError = 0;
		string? path = processId == 0 ? null : NativeMethods.GetProcessPath((int)processId, out pathError);
		if(path != null)
		{
			filePath = path;
			name = NativeMethods.ProcessNameFromPath(path);
		}
		else
		{
			//Windows keeps the sessions of exited apps (inactive), and nothing would ever remove their rows
			if(processId != 0 && !NativeMethods.IsProcessRunning((int)processId))
			{
				Logger.Debug($"Skipped the session of the exited process {processId}");
				return null;
			}
			if(processId != 0)
				LogPathError(processId, pathError);
			try
			{
				//disposed: every new session opens a process handle
				using Process proc = Process.GetProcessById((int)processId);
				name = proc.ProcessName;
			}
			catch { }
		}

		//the app's name instead of the process's, as Windows' mixer shows it: e.g. "WhatsApp" for the msedgewebview2 process that
		//plays its sound. The System Sounds session gets its own name with the app model (CreateProcessModel)
		if(processId != 0)
		{
			string? displayName = null;
			try { displayName = sControl.DisplayName; }
			catch { }
			name = AppIdentity.GetAppName(displayName, AppIdentity.GetPackage(processId), path, name);
		}

		try
		{
			var sessionVolume = new AudioSessionVolume(sControl.SimpleAudioVolume);
			var muteState = sessionVolume.GetMute();
			var volume = sessionVolume.GetVolume();
			var sessionId = sControl.GetSessionIdentifier;
			var groupingParam = sControl.GetGroupingParam();

			AudioSessionStateNotifications sessionStateNotifications = new AudioSessionStateNotifications(sControl, sessionVolume.EchoFilter);

			try { sessionStateNotifications.RegisterNotifications(); }
			catch { }

			AudioSessionModel session = new AudioSessionModel(muteState, VolumePercent.FromScalar(volume), sessionId, processId,
															  name,
															  filePath,
															  groupingParam,
															  sessionVolume,
															  sessionStateNotifications);
			//after registering: a change from now on comes as an event
			try { session.IsActive = sControl.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive; }
			catch { }
			return session;
		}
		catch(Exception e)
		{
			Logger.Error($"Failed to create audio session model", e);
		}
		return null;
	}

	/// <summary>
	/// Path errors that are expected and only logged as debug (not sent to Loggly): access denied (other users' processes,
	/// games guarded by anti-cheat), a general failure and an invalid parameter (the process is exiting or has exited).
	/// </summary>
	private static bool IsExpectedPathError(int error)
		=> error is (int)WIN32_ERROR.ERROR_ACCESS_DENIED or (int)WIN32_ERROR.ERROR_GEN_FAILURE or (int)WIN32_ERROR.ERROR_INVALID_PARAMETER;

	/// <summary>Neither the process nor the system's process list had the path</summary>
	private static void LogPathError(uint processId, int error)
	{
		if(IsExpectedPathError(error))
			Logger.Debug($"Couldn't read the path of the process {processId}, error code {error}");
		else
			Logger.Error($"Failed to read the path of the process {processId}, error code {error}");
	}

	/// <summary>
	/// The app's icon, in the order Windows' volume mixer looks for it: the icon the app gave its session, the icon of its main window
	/// (as the taskbar shows it: an executable can give its window another of its icons), its package's logo (a Store app's helper
	/// process, e.g. WhatsApp's WebView2, has no window), its executable's icon, and the generic program icon
	/// </summary>
	/// <param name="path">The app's executable, null if its path couldn't be read</param>
	/// <param name="name">The app's name for the log</param>
	private static ImageSource? ExtractSessionIcon(uint processId, string? path, string name, AudioSessionControl sc)
	{
		ImageSource? icon = TryExtractSessionIcon(name, sc) ?? IconHelper.GetWindowIconSource(processId);
		if(icon == null && AppIdentity.GetPackage(processId)?.LogoPath is { } logo)
			icon = IconHelper.GetImageFileSource(logo);
		if(icon == null && path != null)
		{
			try { icon = IconHelper.GetAppIconSource(path); }
			catch(Exception e)
			{
				//e.g. a file the user may not read
				Logger.Debug($"Couldn't read the icon of [{name}] from [{path}]", e);
			}
		}
		return icon ?? IconHelper.GenericExeIconSource;
	}

	/// <summary>The icon the app gave its session (IAudioSessionControl::GetIconPath), null if it gave none or it can't be read</summary>
	private static ImageSource? TryExtractSessionIcon(string name, AudioSessionControl sc)
	{
		string? iconPath = null;
		try { iconPath = sc.IconPath; }
		catch { }
		if(string.IsNullOrEmpty(iconPath))
			return null;
		//an indirect resource path ("@dll,-id") names the same icon without the "@"
		iconPath = iconPath.TrimStart('@');

		try
		{
			//"executable-file-path,resource-identifier", or a plain file path
			return IconHelper.GetIconSource(iconPath, IconHelper.GetFromResourcePath);
		}
		catch(Exception e)
		{
			//the app's own icon path is missing or broken; the window's and the executable's icons are tried next
			Logger.Debug($"Failed to extract the session icon, process: [{name}] icon path: [{iconPath}]", e);
			return null;
		}
	}

	private static string GetSystemSoundsName(AudioSessionControl sc)
	{
		string systemSoundsName = string.Empty;
		string resourcePath = string.Empty;
		try
		{
			resourcePath = sc.DisplayName;
			if(!string.IsNullOrEmpty(resourcePath))
			{
				var index = int.Parse(resourcePath.Split(',')[1], CultureInfo.InvariantCulture);
				systemSoundsName = NativeMethods.ExtractStringFromSystemDLL("AudioSrv.dll",
																	   index < 0 ? -index : index);
			}
		}
		catch(Exception e)
		{
			Logger.Error($"Failed to get system sounds session name, path: [{resourcePath}]", e);
		}
		return systemSoundsName;
	}
}
