#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace VolumeyEx.Model;

/// <summary>
/// The "mute all apps" and "one app of the round at a time" hotkeys on one device's apps.
/// <para>
/// Solo works on the apps the user picked (<see cref="SetSoloApps"/>, by name) and leaves every other app alone: each press leaves the next
/// picked app audible and mutes the other picked ones, in the order of the user's list; after the last one a press mutes all of them, and the
/// next one starts over (A, B, C, none, A...). Picked apps that
/// aren't open are skipped; an open one takes part also while it's silent. The apps of one name (e.g. several processes of a browser) go
/// together. The first press starts with the foreground app when it's picked. An app that opens during the round stays as it is until the
/// next press. The apps get their states back when the round is ended (<see cref="RestoreSolo"/>: the hotkey is turned off, the default
/// device changes, VolumeyEx exits), and an app that's taken off the list gets its state back right away.
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
	//each app's mute state before the solo round changed it: what it gets back when the round is ended
	private readonly Dictionary<AudioProcessModel, bool> soloStates = new();

	//each app's mute state right before mute all: what it gets back when mute all is pressed again
	private readonly Dictionary<AudioProcessModel, bool> muteAllStates = new();

	//apps that exited while muted here, by executable and name: the state they get back when they start again
	private readonly Dictionary<string, bool> exitedApps = new(StringComparer.OrdinalIgnoreCase);

	//the apps of the solo round, by name, in the user's order
	private IReadOnlyList<string> soloApps = Array.Empty<string>();

	private bool allMuted;
	private bool soloOn;

	//the name of the round's audible app; null while the round is on: the step where all picked apps are muted
	private string? soloName;

	private ObservableCollection<AudioProcessModel>? apps;

	/// <summary>Mute all is on</summary>
	internal bool AllMuted => allMuted;

	/// <summary>The solo round is on</summary>
	internal bool SoloOn => soloOn;

	/// <summary>The name of the round's audible app, null when the round isn't on or all its apps are muted</summary>
	internal string? SoloName => soloOn ? soloName : null;

	/// <summary>The round is at its step where all picked apps are muted</summary>
	internal bool SoloAllMuted => soloOn && soloName == null;

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
	/// The apps of the solo round, by name, in the order the round goes through them. An app taken off the list during the round
	/// gets its state back
	/// </summary>
	internal void SetSoloApps(IReadOnlyList<string> names)
	{
		soloApps = names.ToList();
		var picked = new HashSet<string>(soloApps, StringComparer.Ordinal);
		foreach(var app in soloStates.Keys.Where(app => !picked.Contains(app.Name)).ToList())
		{
			RestoreSoloState(app, soloStates[app]);
			soloStates.Remove(app);
		}
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
	/// Makes the next picked app the only audible one of the picked apps; after the last one mutes all of them, then the first again
	/// </summary>
	/// <param name="isForeground">Whether an app is the foreground app (where the first press starts)</param>
	/// <returns>
	/// One of the audible app's models (for its OSD); null when all picked apps are muted now (<see cref="SoloAllMuted"/>) or none of them
	/// is open (nothing changes then)
	/// </returns>
	internal AudioProcessModel? SoloNext(Func<AudioProcessModel, bool> isForeground)
	{
		if(apps == null)
			return null;
		//the picked apps that are open, in the user's order, each with its models
		var round = soloApps
			.Select(name => (name, models: apps.Where(app => app.Name == name).ToList()))
			.Where(entry => entry.models.Count > 0)
			.ToList();
		if(round.Count == 0)
			return null;
		//a solo press means "let me hear one": mute all ends first
		RestoreMuteAll();

		//an app's index, or round.Count: the step where all picked apps are muted
		int next;
		if(soloOn)
		{
			//after the last app all muted, after that the first again; when the audible app was closed or taken off the list (-1), also the first
			int current = soloName == null ? round.Count : round.FindIndex(entry => entry.name == soloName);
			next = current == round.Count ? 0 : current + 1;
		}
		else
		{
			next = round.FindIndex(entry => entry.models.Any(isForeground));
			if(next < 0)
				next = 0;
		}

		for(int i = 0; i < round.Count; i++)
		{
			foreach(var app in round[i].models)
			{
				soloStates.TryAdd(app, app.IsMuted);
				app.IsMuted = i != next;
			}
		}
		soloOn = true;
		if(next == round.Count)
		{
			soloName = null;
			return null;
		}
		soloName = round[next].name;
		return round[next].models[0];
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
			RestoreSoloState(app, wasMuted);
		soloStates.Clear();
		soloOn = false;
		soloName = null;
	}

	private void RestoreSoloState(AudioProcessModel app, bool wasMuted)
	{
		if(allMuted && muteAllStates.ContainsKey(app))
			muteAllStates[app] = wasMuted;
		else if(IsShown(app))
			app.IsMuted = wasMuted;
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
