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
/// Mute all apps and the solo round over the picked apps (<see cref="AppMuteSwitcher"/>), on app models with fake sessions
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

	/// <summary>The apps of the solo round, in its order</summary>
	private void Pick(params string[] names) => switcher.SetSoloApps(names);

	private string Next(Func<AudioProcessModel, bool> isForeground = null) => switcher.SoloNext(isForeground ?? NoForeground)?.Name;

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
	public void OnlyThePickedAppsTakePartRoundAndRoundInTheListsOrder()
	{
		App("A");
		App("B");
		App("C");
		App("Sounds", processId: 0);
		Pick("C", "A");

		Assert.AreEqual("C", Next());
		//apps that aren't picked keep their state
		Assert.AreEqual("B,C,Sounds", Audible());
		Assert.AreEqual("A", Next());
		Assert.AreEqual("A,B,Sounds", Audible());
		//after the last one all picked apps are muted
		Assert.IsNull(Next());
		Assert.IsTrue(switcher.SoloAllMuted);
		Assert.AreEqual("B,Sounds", Audible());
		//then the first again
		Assert.AreEqual("C", Next());
		Assert.IsFalse(switcher.SoloAllMuted);
		Assert.IsTrue(switcher.SoloOn);
	}

	[TestMethod]
	public void WithOneOpenAppTheRoundSwitchesItOnAndOff()
	{
		App("A");
		App("Other");
		Pick("A", "Closed");

		Assert.AreEqual("A", Next());
		Assert.IsNull(Next());
		Assert.AreEqual("Other", Audible());
		Assert.AreEqual("A", Next());
		Assert.AreEqual("A,Other", Audible());
	}

	[TestMethod]
	public void EndingTheRoundAtTheMutedStepGivesTheStatesBack()
	{
		App("A");
		App("B", muted: true);
		Pick("A", "B");
		Next();
		Next();
		Assert.IsNull(Next());

		switcher.RestoreSolo();

		Assert.AreEqual("A", Audible());
		Assert.IsFalse(switcher.SoloAllMuted);
	}

	[TestMethod]
	public void PickedAppsThatArentOpenAreSkipped()
	{
		App("A");
		App("B");
		Pick("Closed", "A", "B");

		Assert.AreEqual("A", Next());
		Assert.AreEqual("B", Next());
		Assert.IsNull(Next());
		Assert.AreEqual("A", Next());
	}

	[TestMethod]
	public void AnOpenAppTakesPartAlsoWhileItsSilent()
	{
		App("A");
		App("Quiet", playing: false);
		Pick("A", "Quiet");

		Assert.AreEqual("A", Next());
		Assert.AreEqual("Quiet", Next());
		Assert.AreEqual("Quiet", Audible());
	}

	[TestMethod]
	public void TheAppsOfOneNameGoTogether()
	{
		App("Browser");
		App("A");
		App("Browser");
		Pick("Browser", "A");

		Assert.AreEqual("Browser", Next());
		Assert.AreEqual("Browser,Browser", Audible());
		Assert.AreEqual("A", Next());
		Assert.AreEqual("A", Audible());
	}

	[TestMethod]
	public void TheFirstPressStartsWithTheForegroundAppWhenItsPicked()
	{
		App("A");
		var b = App("B");
		App("C");
		Pick("A", "B", "C");
		Func<AudioProcessModel, bool> isB = app => app == b;

		Assert.AreEqual("B", Next(isB));
		Assert.AreEqual("C", Next(isB));
		Assert.IsNull(Next(isB));
		//the round goes on in order: A, not the foreground app again
		Assert.AreEqual("A", Next(isB));
	}

	[TestMethod]
	public void AForegroundAppThatIsntPickedDoesntChangeWhereTheRoundStarts()
	{
		App("A");
		var other = App("Other");
		App("B");
		Pick("A", "B");

		Assert.AreEqual("A", Next(app => app == other));
		Assert.AreEqual("A,Other", Audible());
	}

	[TestMethod]
	public void EndingTheRoundGivesTheStatesBack()
	{
		App("A");
		App("B");
		App("C", muted: true);
		Pick("A", "B", "C");
		Next();
		Next();

		switcher.RestoreSolo();

		Assert.AreEqual("A,B", Audible());
		Assert.IsFalse(switcher.SoloOn);
	}

	[TestMethod]
	public void AnAppTakenOffTheListGetsItsStateBackRightAway()
	{
		App("A");
		App("B");
		App("C");
		Pick("A", "B", "C");
		Assert.AreEqual("A", Next());

		Pick("A", "C");

		Assert.AreEqual("A,B", Audible());
		Assert.AreEqual("C", Next());
		//B isn't part of the round any more
		Assert.AreEqual("B,C", Audible());
	}

	[TestMethod]
	public void AnAppThatOpensDuringTheRoundStaysAsItIsUntilTheNextPress()
	{
		App("A");
		App("B");
		Pick("A", "B", "Late");
		Next();

		App("Late");
		Assert.AreEqual("A,Late", Audible());

		Assert.AreEqual("B", Next());
		Assert.AreEqual("B", Audible());
		Assert.AreEqual("Late", Next());
	}

	[TestMethod]
	public void WhenTheAudibleAppClosesTheRoundStartsOver()
	{
		App("A");
		App("B");
		App("C");
		Pick("A", "B", "C");
		Next();
		Assert.AreEqual("B", Next());

		apps.Remove(apps.Single(x => x.Name == "B"));

		Assert.AreEqual("A", Next());
	}

	[TestMethod]
	public void NothingHappensWhenNoPickedAppIsOpen()
	{
		App("A");

		Assert.IsNull(Next());
		Pick("Closed");
		Assert.IsNull(Next());

		Assert.AreEqual("A", Audible());
		Assert.IsFalse(switcher.SoloOn);
	}

	[TestMethod]
	public void AnAppThatExitedWhileMutedGetsItsStateBackWhenItStartsAgain()
	{
		App("A");
		var b = App("B");
		Pick("A", "B");
		Next();
		Assert.IsTrue(b.IsMuted);

		apps.Remove(b);

		//Windows starts it muted: it kept the mute of the app
		var again = App("B", muted: true);
		Assert.IsFalse(again.IsMuted);
	}

	[TestMethod]
	public void MuteAllDuringTheRoundGivesTheRoundsStateBackAndTheRoundGoesOn()
	{
		App("A");
		App("B");
		App("C", muted: true);
		Pick("A", "B", "C");
		Next();
		Assert.AreEqual("A", Audible());

		switcher.ToggleMuteAll();
		Assert.AreEqual("", Audible());

		//back to where the round was, not to before the round
		switcher.ToggleMuteAll();
		Assert.AreEqual("A", Audible());
		Assert.IsTrue(switcher.SoloOn);

		Assert.AreEqual("B", Next());
		Assert.AreEqual("C", Next());
		switcher.RestoreSolo();
		//the round's end gives the states from before the round: C was muted
		Assert.AreEqual("A,B", Audible());
	}

	[TestMethod]
	public void ASoloPressWhileAllAreMutedEndsMuteAllFirst()
	{
		App("A");
		App("B");
		App("Other");
		Pick("A", "B");
		switcher.ToggleMuteAll();

		Assert.AreEqual("A", Next());

		Assert.IsFalse(switcher.AllMuted);
		Assert.AreEqual("A,Other", Audible());
	}

	[TestMethod]
	public void EndingTheRoundWhileAllAreMutedKeepsThemMutedUntilMuteAllIsPressedAgain()
	{
		App("A");
		App("B");
		App("C", muted: true);
		Pick("A", "B", "C");
		Next();
		switcher.ToggleMuteAll();

		switcher.RestoreSolo();
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
		Pick("A", "B");
		Next();

		switcher.Detach();

		Assert.AreEqual("A,B", Audible());
	}
}
