using System;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using VolumeyEx.Controls;
using VolumeyEx.CoreAudioWrapper.Wrapper;
using VolumeyEx.Model;
using VolumeyEx.Services;
using VolumeyEx.ViewModel.Settings;

namespace VolumeyEx.Tests.ViewModels;

/// <summary>
/// The mute all and solo hotkeys (<see cref="AppMuteHotkeysViewModel"/>): saving, pressing, and giving the apps their states back
/// </summary>
[TestClass]
public class AppMuteHotkeysViewModelTests : ViewModelTestBase
{
	private static readonly HotKey MuteAll = new HotKey(Key.F9, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
	private static readonly HotKey Solo = new HotKey(Key.F10, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);

	private Mock<IAppShell> shell;
	private AudioProcessModel a, b;
	private OutputDeviceModel device;

	[TestInitialize]
	public void Initialize()
	{
		shell = new Mock<IAppShell>();
		shell.Setup(m => m.Dispatcher).Returns(Dispatcher.CurrentDispatcher);
		a = PlayingApp("A", 100);
		b = PlayingApp("B", 101);
		device = CreateDevice("device", "Speakers", a, b);
		SetDefaultDevice(device);
		//the round's apps, unless a test picks others
		Settings.Hotkeys.SoloApps = new[] { "A", "B" };
	}

	private AudioProcessModel PlayingApp(string name, uint processId)
	{
		string path = $@"C:\Apps\{name}.exe";
		var process = new AudioProcessModel(50, false, name, processId, path, null, null, Hotkeys);
		process.AddSession(new AudioSessionModel(false, 50, name, processId, name, path, Guid.NewGuid(),
			new Mock<IAudioSessionVolume>().Object, new Mock<IAudioSessionStateNotifications>().Object) { IsActive = true });
		return process;
	}

	private AppMuteHotkeysViewModel Create()
		=> new AppMuteHotkeysViewModel(DeviceProvider.Object, Hotkeys, Notifications.Object, shell.Object);

	private string Audible() => string.Join(",", device.Processes.Where(p => !p.IsMuted).Select(p => p.Name));

	[TestMethod]
	public void SavedHotkeysAreRegisteredOnStartAndWork()
	{
		Settings.Hotkeys.MuteAllApps = MuteAll;
		Settings.Hotkeys.SoloPlayingApp = Solo;
		var model = Create();
		ActivateHotkeys();

		Assert.IsTrue(model.MuteAllRegistered);
		Assert.IsTrue(model.SoloRegistered);
		HotkeyManager.Verify(m => m.RegisterHotkey(MuteAll), Times.Once);
		HotkeyManager.Verify(m => m.RegisterHotkey(Solo), Times.Once);

		PressHotkey(MuteAll);
		Assert.AreEqual("", Audible());
		PressHotkey(MuteAll);
		Assert.AreEqual("A,B", Audible());

		PressHotkey(Solo);
		Assert.AreEqual("A", Audible());
		PressHotkey(Solo);
		Assert.AreEqual("B", Audible());
		//round and round: no "all" step
		PressHotkey(Solo);
		Assert.AreEqual("A", Audible());
	}

	[TestMethod]
	public void TurningAHotkeyOnSavesIt()
	{
		ActivateHotkeys();
		var model = Create();

		model.SoloKey = Solo;
		model.SoloRegistered = true;

		Assert.IsTrue(model.SoloRegistered);
		Assert.AreEqual(Solo, Settings.Hotkeys.SoloPlayingApp);
		Assert.AreEqual("", model.SoloError);
	}

	[TestMethod]
	public void AHotkeyAnotherFeatureUsesIsRefused()
	{
		ActivateHotkeys();
		Settings.Hotkeys.DeviceMute = MuteAll;
		var model = Create();

		model.MuteAllKey = MuteAll;
		model.MuteAllRegistered = true;

		Assert.IsFalse(model.MuteAllRegistered);
		Assert.IsNull(Settings.Hotkeys.MuteAllApps);
		Assert.AreNotEqual("", model.MuteAllError);
	}

	[TestMethod]
	public void SoloShowsTheAudibleAppsNotification()
	{
		Settings.Notifications.Enabled = true;
		Settings.Hotkeys.SoloPlayingApp = Solo;
		Create();
		ActivateHotkeys();

		PressHotkey(Solo);

		Notifications.Verify(m => m.ShowNotification(a), Times.Once);
	}

	/// <summary>The notifications shown: the OSD's id and its mute state when it was shown</summary>
	private System.Collections.Generic.List<(string id, bool muted, int volume)> RecordNotifications()
	{
		var shown = new System.Collections.Generic.List<(string, bool, int)>();
		Notifications.Setup(m => m.ShowNotification(It.IsAny<IManagedMasterAudioSession>()))
			.Callback<IManagedMasterAudioSession>(s => shown.Add((s.Id, s.IsMuted, s.Volume)));
		return shown;
	}

	[TestMethod]
	public void MuteAllShowsTheAllAppsNotification()
	{
		Settings.Notifications.Enabled = true;
		Settings.Hotkeys.MuteAllApps = MuteAll;
		var shown = RecordNotifications();
		Create();
		ActivateHotkeys();

		PressHotkey(MuteAll);
		PressHotkey(MuteAll);

		//the device's volume, muted while all apps are muted
		CollectionAssert.AreEqual(new[] { ("VolumeyEx.AllApps", true, 70), ("VolumeyEx.AllApps", false, 70) }, shown);
	}

	[TestMethod]
	public void TheNotificationsMuteButtonTurnsMuteAllOff()
	{
		Settings.Notifications.Enabled = true;
		Settings.Hotkeys.MuteAllApps = MuteAll;
		IManagedMasterAudioSession osd = null;
		Notifications.Setup(m => m.ShowNotification(It.IsAny<IManagedMasterAudioSession>())).Callback<IManagedMasterAudioSession>(s => osd = s);
		Create();
		ActivateHotkeys();
		PressHotkey(MuteAll);

		osd.MuteCommand.Execute(null);

		Assert.AreEqual("A,B", Audible());
		Assert.IsFalse(osd.IsMuted);
	}

	[TestMethod]
	public void EachPressShowsTheAudibleApp()
	{
		Settings.Notifications.Enabled = true;
		Settings.Hotkeys.SoloPlayingApp = Solo;
		var shown = RecordNotifications();
		Create();
		ActivateHotkeys();

		PressHotkey(Solo);
		PressHotkey(Solo);
		PressHotkey(Solo);

		CollectionAssert.AreEqual(new[] { ("100", false, 50), ("101", false, 50), ("100", false, 50) }, shown);
	}

	[TestMethod]
	public void NoNotificationWhileNotificationsAreOff()
	{
		Settings.Notifications.Enabled = false;
		Settings.Hotkeys.MuteAllApps = MuteAll;
		Settings.Hotkeys.SoloPlayingApp = Solo;
		Create();
		ActivateHotkeys();

		PressHotkey(MuteAll);
		PressHotkey(Solo);

		Notifications.Verify(m => m.ShowNotification(It.IsAny<IManagedMasterAudioSession>()), Times.Never);
	}

	[TestMethod]
	public void TurningTheHotkeyOffGivesTheStatesBack()
	{
		Settings.Hotkeys.MuteAllApps = MuteAll;
		var model = Create();
		ActivateHotkeys();
		PressHotkey(MuteAll);

		model.MuteAllRegistered = false;

		Assert.AreEqual("A,B", Audible());
		Assert.IsNull(Settings.Hotkeys.MuteAllApps);
		HotkeyManager.Verify(m => m.UnregisterHotkey(MuteAll), Times.Once);
	}

	[TestMethod]
	public void TurningMuteAllOffDuringTheRoundKeepsTheRound()
	{
		Settings.Hotkeys.MuteAllApps = MuteAll;
		Settings.Hotkeys.SoloPlayingApp = Solo;
		var model = Create();
		ActivateHotkeys();
		PressHotkey(Solo);
		PressHotkey(MuteAll);

		model.MuteAllRegistered = false;

		Assert.AreEqual("A", Audible());
		PressHotkey(Solo);
		Assert.AreEqual("B", Audible());
	}

	[TestMethod]
	public void ExitingGivesTheStatesBack()
	{
		Settings.Hotkeys.SoloPlayingApp = Solo;
		Create();
		ActivateHotkeys();
		PressHotkey(Solo);
		Assert.AreEqual("A", Audible());

		shell.Raise(m => m.Exiting += null);

		Assert.AreEqual("A,B", Audible());
	}

	[TestMethod]
	public void LoggingOffGivesTheStatesBack()
	{
		Settings.Hotkeys.MuteAllApps = MuteAll;
		Create();
		ActivateHotkeys();
		PressHotkey(MuteAll);

		shell.Raise(m => m.SessionEnding += null);

		Assert.AreEqual("A,B", Audible());
	}

	[TestMethod]
	public void AnotherDefaultDeviceGivesThePreviousOnesAppsTheirStatesBack()
	{
		Settings.Hotkeys.MuteAllApps = MuteAll;
		Create();
		ActivateHotkeys();
		PressHotkey(MuteAll);

		ChangeDefaultDevice(CreateDevice("other", "Headphones"));

		Assert.AreEqual("A,B", Audible());
	}

	private static string Names(AppMuteHotkeysViewModel model) => string.Join(",", model.SoloApps.Select(app => app.Name));

	[TestMethod]
	public void TheSavedRoundAppsAreShownWithTheIconsOfTheOpenOnes()
	{
		Settings.Hotkeys.SoloApps = new[] { "B", "Closed", "A" };
		var model = Create();

		Assert.AreEqual("B,Closed,A", Names(model));
		Assert.IsNull(model.SoloApps[1].IconSource);
	}

	[TestMethod]
	public void TheAddDialogOffersTheOpenAppsThatArentInTheRoundAndAddsAtTheEnd()
	{
		Settings.Hotkeys.SoloApps = new[] { "A" };
		var model = Create();

		model.PrepareAddSoloApp();
		CollectionAssert.AreEqual(new[] { b }, model.AddableSoloApps);
		Assert.IsFalse(model.AddSoloApp(), "nothing picked");

		model.SelectedSoloApp = b;
		Assert.IsTrue(model.AddSoloApp());

		Assert.AreEqual("A,B", Names(model));
		CollectionAssert.AreEqual(new[] { "A", "B" }, Settings.Hotkeys.SoloApps.ToList());
	}

	[TestMethod]
	public void MovingAnAppChangesTheRoundsOrder()
	{
		Settings.Hotkeys.SoloPlayingApp = Solo;
		var model = Create();
		ActivateHotkeys();
		Assert.IsFalse(model.SoloApps[0].MoveUpCommand.CanExecute(null), "the first app can't move up");
		Assert.IsFalse(model.SoloApps[1].MoveDownCommand.CanExecute(null), "the last app can't move down");

		model.SoloApps[1].MoveUpCommand.Execute(null);

		Assert.AreEqual("B,A", Names(model));
		CollectionAssert.AreEqual(new[] { "B", "A" }, Settings.Hotkeys.SoloApps.ToList());
		PressHotkey(Solo);
		Assert.AreEqual("B", Audible());
	}

	[TestMethod]
	public void RemovingAnAppDuringTheRoundGivesItsStateBack()
	{
		Settings.Hotkeys.SoloPlayingApp = Solo;
		var model = Create();
		ActivateHotkeys();
		PressHotkey(Solo);
		Assert.AreEqual("A", Audible());

		model.SoloApps.Single(app => app.Name == "B").RemoveCommand.Execute(null);

		Assert.AreEqual("A,B", Audible());
		Assert.AreEqual("A", Names(model));
		CollectionAssert.AreEqual(new[] { "A" }, Settings.Hotkeys.SoloApps.ToList());
	}

	[TestMethod]
	public void AppsOutsideTheRoundArentTouched()
	{
		Settings.Hotkeys.SoloApps = new[] { "B" };
		Settings.Hotkeys.SoloPlayingApp = Solo;
		Create();
		ActivateHotkeys();

		PressHotkey(Solo);

		Assert.AreEqual("A,B", Audible());
	}
}
