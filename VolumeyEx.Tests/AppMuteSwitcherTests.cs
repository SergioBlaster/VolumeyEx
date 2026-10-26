using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VolumeyEx.CoreAudioWrapper.Wrapper;
using VolumeyEx.Model;
using VolumeyEx.ViewModel.Settings;

namespace VolumeyEx.Tests;

/// <summary>
/// Mute all apps and the solo round (<see cref="AppMuteSwitcher"/>), on app models with fake sessions
/// </summary>
[TestClass]
public class AppMuteSwitcherTests
{
	private readonly HotkeyService hotkeys = new HotkeyService();
	private readonly ObservableCollection<AudioProcessModel> apps = new();
	private readonly AppMuteSwitcher switcher = new AppMuteSwitcher();
	private uint nextProcessId = 100;

	[TestInitialize]
	public void Attach() => switcher.Attach(apps);

	/// <param name="processId">0: System Sounds</param>
	private AudioProcessModel App(string name, bool playing = true, bool muted = false, uint? processId = null, bool add = true)
	{
		uint id = processId ?? nextProcessId++;
		string path = id == 0 ? "0" : $@"C:\Apps\{name}.exe";
		var process = new AudioProcessModel(50, muted, name, id, path, null, null, hotkeys);
		var session = new AudioSessionModel(muted, 50, Guid.NewGuid().ToString(), id, name, path, Guid.NewGuid(),
			new Mock<IAudioSessionVolume>().Object, new Mock<IAudioSessionStateNotifications>().Object) { IsActive = playing };
		process.AddSession(session);
		if(add)
			apps.Add(process);
		return process;
	}

	private static Func<AudioProcessModel, bool> NoForeground => _ => false;

	private string Audible() => string.Join(",", apps.Where(a => !a.IsMuted).Select(a => a.Name));

	[TestMethod]
	public void MuteAllMutesEveryAppAndGivesEachItsStateBack()
	{
		App("A");
		App("B", muted: true);
		App("Sounds", processId: 0);
		App("Quiet", playing: false);

		Assert.IsTrue(switcher.ToggleMuteAll());
		Assert.AreEqual("", Audible());
		Assert.IsTrue(switcher.AllMuted);

		Assert.IsFalse(switcher.ToggleMuteAll());
		Assert.AreEqual("A,Sounds,Quiet", Audible());
		Assert.IsFalse(switcher.AllMuted);
	}

	[TestMethod]
	public void AnAppThatStartsWhileAllAreMutedIsMutedAndGivenItsStateBack()
	{
		App("A");
		switcher.ToggleMuteAll();

		var late = App("Late");

		Assert.IsTrue(late.IsMuted);
		switcher.ToggleMuteAll();
		Assert.AreEqual("A,Late", Audible());
	}

	[TestMethod]
	public void TheSoloRoundGoesThroughThePlayingAppsThenAllPlayAgain()
	{
		App("A");
		App("B");
		App("Quiet", playing: false);
		App("C");
		App("Sounds", processId: 0);

		Assert.AreEqual("A", switcher.SoloNext(NoForeground)!.Name);
		//apps that don't play and System Sounds keep their state
		Assert.AreEqual("A,Quiet,Sounds", Audible());
		Assert.AreEqual("B", switcher.SoloNext(NoForeground)!.Name);
		Assert.AreEqual("B,Quiet,Sounds", Audible());
		Assert.AreEqual("C", switcher.SoloNext(NoForeground)!.Name);
		Assert.AreEqual("Quiet,C,Sounds", Audible());

		//after the last one: "all"
		Assert.IsNull(switcher.SoloNext(NoForeground));
		Assert.AreEqual("A,B,Quiet,C,Sounds", Audible());
		Assert.IsNull(switcher.SoloApp);
	}

	[TestMethod]
	public void TheFirstPressStartsWithTheForegroundAppAndTheNextRoundWithTheFirst()
	{
		App("A");
		var b = App("B");
		App("C");
		Func<AudioProcessModel, bool> isB = app => app == b;

		Assert.AreEqual("B", switcher.SoloNext(isB)!.Name);
		Assert.AreEqual("C", switcher.SoloNext(isB)!.Name);
		Assert.IsNull(switcher.SoloNext(isB));
		//the round goes on in order: A, not the foreground app again
		Assert.AreEqual("A", switcher.SoloNext(isB)!.Name);
	}

	[TestMethod]
	public void AppsTheUserHadMutedStayMutedAfterTheRound()
	{
		App("A");
		App("B", muted: true);

		Assert.AreEqual("A", switcher.SoloNext(NoForeground)!.Name);
		Assert.AreEqual("B", switcher.SoloNext(NoForeground)!.Name);
		Assert.AreEqual("B", Audible());
		Assert.IsNull(switcher.SoloNext(NoForeground));

		Assert.AreEqual("A", Audible());
	}

	[TestMethod]
	public void AnAppThatStartsPlayingDuringTheRoundStaysAudibleUntilTheNextPress()
	{
		App("A");
		App("B");
		switcher.SoloNext(NoForeground);

		App("Late");
		Assert.AreEqual("A,Late", Audible());

		Assert.AreEqual("B", switcher.SoloNext(NoForeground)!.Name);
		Assert.AreEqual("B", Audible());
		Assert.AreEqual("Late", switcher.SoloNext(NoForeground)!.Name);
	}

	[TestMethod]
	public void WhenTheAudibleAppStopsPlayingTheRoundStartsOver()
	{
		var a = App("A");
		App("B");
		App("C");
		switcher.SoloNext(NoForeground);
		switcher.SoloNext(NoForeground);
		Assert.AreEqual("B", switcher.SoloApp!.Name);

		apps.Remove(apps.Single(x => x.Name == "B"));

		Assert.AreSame(a, switcher.SoloNext(NoForeground));
	}

	[TestMethod]
	public void NothingHappensWhenNoAppIsPlaying()
	{
		App("Quiet", playing: false);
		App("Sounds", processId: 0);

		Assert.IsNull(switcher.SoloNext(NoForeground));
		Assert.AreEqual("Quiet,Sounds", Audible());
	}

	[TestMethod]
	public void AnAppThatExitedWhileMutedGetsItsStateBackWhenItStartsAgain()
	{
		App("A");
		var b = App("B");
		switcher.SoloNext(NoForeground);
		Assert.IsTrue(b.IsMuted);

		apps.Remove(b);
		switcher.SoloNext(NoForeground);
		Assert.IsNull(switcher.SoloApp);

		//Windows starts it muted: it kept the mute of the app
		var again = App("B", muted: true);
		Assert.IsFalse(again.IsMuted);
	}

	[TestMethod]
	public void MuteAllDuringTheRoundGivesTheStatesFromBeforeTheRoundBack()
	{
		App("A");
		App("B");
		App("C", muted: true);
		switcher.SoloNext(NoForeground);

		switcher.ToggleMuteAll();
		Assert.AreEqual("", Audible());

		switcher.ToggleMuteAll();
		Assert.AreEqual("A,B", Audible());
	}

	[TestMethod]
	public void AnotherDeviceGivesThePreviousDevicesAppsTheirStatesBack()
	{
		App("A");
		App("B");
		switcher.ToggleMuteAll();

		var other = new ObservableCollection<AudioProcessModel>();
		switcher.Attach(other);

		Assert.AreEqual("A,B", Audible());
		Assert.IsFalse(switcher.AllMuted);
		//the previous device's list isn't watched any more
		Assert.IsFalse(App("Late").IsMuted);
	}

	[TestMethod]
	public void DetachGivesTheStatesBack()
	{
		App("A");
		App("B");
		switcher.SoloNext(NoForeground);

		switcher.Detach();

		Assert.AreEqual("A,B", Audible());
	}
}
