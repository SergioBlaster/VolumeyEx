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
/// Solo leaves one of the apps that are playing audible and mutes the other playing ones; each press moves on to the next playing app
/// in the mixer's order, and after the last one every app gets its state back (A, B, C, all, A...). The first press starts with the
/// foreground app when it's playing. System Sounds isn't part of the round, and an app that starts playing during it stays audible
/// until the next press.
/// </para>
/// <para>
/// Mute all mutes every app, also the ones that start while it's on; pressed again it gives each app back the state it had right before,
/// so during a solo round the round's state comes back and the round goes on. The two keep their own saved states: mute all is a layer
/// over the round. A solo press while all apps are muted gives the states from before mute all back first.
/// </para>
/// Windows keeps an app's mute after it exits, so an app that exits while muted here gets its state back when it starts again.
/// Used on the UI thread: the hotkeys and the device's app list (<see cref="ObservableCollection{T}.CollectionChanged"/>) are raised there.
/// </summary>
internal sealed class AppMuteSwitcher
{
	//each app's mute state before the solo round changed it: what it gets back when the round ends
	private readonly Dictionary<AudioProcessModel, bool> soloStates = new();

	//each app's mute state right before mute all: what it gets back when mute all is pressed again
	private readonly Dictionary<AudioProcessModel, bool> muteAllStates = new();

	//apps that exited while muted here, by executable and name: the state they get back when they start again
	private readonly Dictionary<string, bool> exitedApps = new(StringComparer.OrdinalIgnoreCase);

	private bool allMuted;
	private bool soloOn;

	//the audible app of the solo round; null while the round is on: it stopped playing or exited
	private AudioProcessModel? soloApp;

	//the round just went through "all": the next press starts with the first app again, not with the foreground one
	private bool roundEnded;

	private ObservableCollection<AudioProcessModel>? apps;

	/// <summary>Mute all is on</summary>
	internal bool AllMuted => allMuted;

	/// <summary>The solo round is on</summary>
	internal bool SoloOn => soloOn;

	/// <summary>The audible app of the solo round, null when it isn't on</summary>
	internal AudioProcessModel? SoloApp => soloOn ? soloApp : null;

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
	/// Mutes every app, or gives each the state it had right before when mute all is on
	/// </summary>
	/// <returns>True if the apps are muted now</returns>
	internal bool ToggleMuteAll()
	{
		if(allMuted)
		{
			RestoreMuteAll();
			return false;
		}
		if(apps == null)
			return false;
		foreach(var app in apps.ToList())
			MuteForAll(app);
		allMuted = true;
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
		//a solo press means "let me hear one": mute all ends first
		RestoreMuteAll();
		var playing = apps.Where(app => !app.IsSystemSounds && app.IsPlaying).ToList();

		AudioProcessModel? next;
		if(soloOn)
		{
			int current = soloApp == null ? -1 : playing.IndexOf(soloApp);
			//after the last app (or when none plays any more): "all", the end of the round
			if(playing.Count == 0 || (current >= 0 && current == playing.Count - 1))
			{
				RestoreSolo();
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
		{
			soloStates.TryAdd(app, app.IsMuted);
			app.IsMuted = !ReferenceEquals(app, next);
		}
		soloOn = true;
		soloApp = next;
		roundEnded = false;
		return next;
	}

	/// <summary>Gives every app the state it had before mute all and the solo round</summary>
	internal void Restore()
	{
		RestoreMuteAll();
		RestoreSolo();
	}

	/// <summary>Gives each app the state it had right before mute all (the solo round's, while it's on)</summary>
	internal void RestoreMuteAll()
	{
		foreach(var (app, wasMuted) in muteAllStates)
		{
			if(IsShown(app))
				app.IsMuted = wasMuted;
		}
		muteAllStates.Clear();
		allMuted = false;
	}

	/// <summary>
	/// Ends the solo round: each app gets the state it had before it. While mute all is on the apps stay muted, and mute all
	/// gives them those states when it's pressed again
	/// </summary>
	internal void RestoreSolo()
	{
		foreach(var (app, wasMuted) in soloStates)
		{
			if(allMuted && muteAllStates.ContainsKey(app))
				muteAllStates[app] = wasMuted;
			else if(IsShown(app))
				app.IsMuted = wasMuted;
		}
		soloStates.Clear();
		soloOn = false;
		soloApp = null;
	}

	private bool IsShown(AudioProcessModel app) => apps != null && apps.Contains(app);

	private void MuteForAll(AudioProcessModel app)
	{
		muteAllStates.TryAdd(app, app.IsMuted);
		app.IsMuted = true;
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
		//its state before anything here changed it: the round's saved state comes from before mute all's
		bool hadSolo = soloStates.Remove(app, out bool beforeSolo);
		bool hadMuteAll = muteAllStates.Remove(app, out bool beforeMuteAll);
		if(hadSolo || hadMuteAll)
		{
			bool original = hadSolo ? beforeSolo : beforeMuteAll;
			if(app.IsMuted != original)
				exitedApps[KeyOf(app)] = original;
		}
		if(ReferenceEquals(soloApp, app))
			soloApp = null;
	}

	private void OnAppAdded(AudioProcessModel app)
	{
		//muted here when it exited: Windows started it muted
		if(exitedApps.Remove(KeyOf(app), out bool wasMuted))
			app.IsMuted = wasMuted;
		if(allMuted)
			MuteForAll(app);
	}
}
