#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace VolumeyEx.Model;

/// <summary>
/// The "mute all apps" and "only one playing app at a time" hotkeys on one device's apps.
/// <para>
/// Mute all mutes every app, also the ones that start while it's on; pressed again it gives each app back the mute state it had before.
/// Solo leaves one of the apps that are playing audible and mutes the other playing ones; each press moves on to the next playing app
/// in the mixer's order, and after the last one every app gets its state back (A, B, C, all, A...). The first press starts with the
/// foreground app when it's playing. System Sounds isn't part of the round, and an app that starts playing during it stays audible
/// until the next press.
/// </para>
/// Windows keeps an app's mute after it exits, so an app that exits while muted here gets its state back when it starts again.
/// Used on the UI thread: the hotkeys and the device's app list (<see cref="ObservableCollection{T}.CollectionChanged"/>) are raised there.
/// </summary>
internal sealed class AppMuteSwitcher
{
	private enum Mode { None, MuteAll, Solo }

	private Mode mode;

	//each app's mute state before this changed it: what it gets back
	private readonly Dictionary<AudioProcessModel, bool> savedStates = new();

	//apps that exited while muted here, by executable and name: the state they get back when they start again
	private readonly Dictionary<string, bool> exitedApps = new(StringComparer.OrdinalIgnoreCase);

	//the audible app of the solo round
	private AudioProcessModel? soloApp;

	//the round just went through "all": the next press starts with the first app again, not with the foreground one
	private bool roundEnded;

	private ObservableCollection<AudioProcessModel>? apps;

	/// <summary>Mute all is on</summary>
	internal bool AllMuted => mode == Mode.MuteAll;

	/// <summary>The audible app of the solo round, null when it isn't on</summary>
	internal AudioProcessModel? SoloApp => mode == Mode.Solo ? soloApp : null;

	/// <summary>
	/// Works on these apps from now on (the default device's); the apps of the previous device get their states back first
	/// </summary>
	internal void Attach(ObservableCollection<AudioProcessModel>? deviceApps)
	{
		if(ReferenceEquals(apps, deviceApps))
			return;
		Detach();
		apps = deviceApps;
		if(apps != null)
			apps.CollectionChanged += OnAppsChanged;
	}

	/// <summary>Gives every app its state back and stops watching the device's apps</summary>
	internal void Detach()
	{
		Restore();
		if(apps != null)
			apps.CollectionChanged -= OnAppsChanged;
		apps = null;
	}

	/// <summary>
	/// Mutes every app, or gives each its state back when mute all is on
	/// </summary>
	/// <returns>True if the apps are muted now</returns>
	internal bool ToggleMuteAll()
	{
		if(mode == Mode.MuteAll)
		{
			Restore();
			return false;
		}
		if(apps == null)
			return false;
		foreach(var app in apps.ToList())
			SetMute(app, true);
		mode = Mode.MuteAll;
		soloApp = null;
		roundEnded = false;
		return true;
	}

	/// <summary>
	/// Makes the next playing app the only audible one, or gives every app its state back after the last one
	/// </summary>
	/// <param name="isForeground">Whether an app is the foreground app (where the first press starts)</param>
	/// <returns>The audible app, null if every app got its state back or none is playing</returns>
	internal AudioProcessModel? SoloNext(Func<AudioProcessModel, bool> isForeground)
	{
		if(apps == null)
			return null;
		var playing = apps.Where(app => !app.IsSystemSounds && app.IsPlaying).ToList();

		AudioProcessModel? next;
		if(mode == Mode.Solo)
		{
			int current = soloApp == null ? -1 : playing.IndexOf(soloApp);
			//after the last app (or when none plays any more): "all", the end of the round
			if(playing.Count == 0 || (current >= 0 && current == playing.Count - 1))
			{
				Restore();
				roundEnded = true;
				return null;
			}
			//the audible app stopped playing or exited: from the start again
			next = playing[current + 1];
		}
		else
		{
			if(playing.Count == 0)
				return null;
			next = roundEnded ? playing[0] : playing.FirstOrDefault(isForeground) ?? playing[0];
		}

		foreach(var app in playing)
			SetMute(app, !ReferenceEquals(app, next));
		mode = Mode.Solo;
		soloApp = next;
		roundEnded = false;
		return next;
	}

	/// <summary>Gives every app the mute state it had before mute all or the solo round</summary>
	internal void Restore()
	{
		foreach(var (app, wasMuted) in savedStates)
		{
			if(apps != null && apps.Contains(app))
				app.IsMuted = wasMuted;
		}
		savedStates.Clear();
		mode = Mode.None;
		soloApp = null;
	}

	private void SetMute(AudioProcessModel app, bool mute)
	{
		savedStates.TryAdd(app, app.IsMuted);
		app.IsMuted = mute;
	}

	private static string KeyOf(AudioProcessModel app) => app.FilePath + "|" + app.Name;

	private void OnAppsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if(e.OldItems != null)
		{
			foreach(AudioProcessModel app in e.OldItems)
				OnAppRemoved(app);
		}
		if(e.NewItems != null)
		{
			foreach(AudioProcessModel app in e.NewItems)
				OnAppAdded(app);
		}
	}

	private void OnAppRemoved(AudioProcessModel app)
	{
		if(savedStates.Remove(app, out bool wasMuted) && app.IsMuted != wasMuted)
			exitedApps[KeyOf(app)] = wasMuted;
		if(ReferenceEquals(soloApp, app))
			soloApp = null;
	}

	private void OnAppAdded(AudioProcessModel app)
	{
		//muted here when it exited: Windows started it muted
		if(exitedApps.Remove(KeyOf(app), out bool wasMuted))
			app.IsMuted = wasMuted;
		if(mode == Mode.MuteAll)
			SetMute(app, true);
	}
}
