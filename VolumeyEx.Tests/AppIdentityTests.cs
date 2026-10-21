using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VolumeyEx.Helper;

namespace VolumeyEx.Tests;

/// <summary>
/// The app name shown for an audio session (<see cref="AppIdentity"/>), in the order Windows' volume mixer uses
/// </summary>
[TestClass]
public class AppIdentityTests
{
	private static readonly string Notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
	private static readonly AppIdentity.PackageInfo WhatsApp = new AppIdentity.PackageInfo("WhatsApp", null);

	[TestMethod]
	public void TheSessionsOwnNameComesFirst()
		=> Assert.AreEqual("GloboLiveWin", AppIdentity.GetAppName(" GloboLiveWin ", WhatsApp, Notepad, "msedgewebview2"));

	[TestMethod]
	public void ThenThePackagesName()
		=> Assert.AreEqual("WhatsApp", AppIdentity.GetAppName("", WhatsApp, Notepad, "msedgewebview2"));

	[TestMethod]
	public void ThenTheExecutablesDescription()
	{
		string description = AppIdentity.GetAppName(null, null, Notepad, "notepad");
		Assert.IsFalse(string.IsNullOrWhiteSpace(description));
		Assert.AreNotEqual("notepad", description, "the description wasn't read");
		Assert.AreEqual(description, AppIdentity.GetAppName("   ", new AppIdentity.PackageInfo(" ", null), Notepad, "notepad"));
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("1234")]
	[DataRow(@"C:\does not exist\app.exe")]
	public void TheProcessNameIsTheLastResort(string path)
		=> Assert.AreEqual("app", AppIdentity.GetAppName(null, null, path, "app"));

	[TestMethod]
	public void AnIndirectNameIsReadFromItsResource()
	{
		//Windows' own System Sounds session names itself this way
		string name = AppIdentity.ResolveDisplayName(@"@%SystemRoot%\System32\AudioSrv.Dll,-202");
		Assert.IsFalse(string.IsNullOrWhiteSpace(name));
		Assert.IsFalse(name.StartsWith('@'), name);
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("  ")]
	[DataRow(@"@C:\does not exist.dll,-1")]
	public void AnEmptyOrUnreadableNameIsNone(string displayName)
		=> Assert.IsNull(AppIdentity.ResolveDisplayName(displayName));

	[TestMethod]
	public void AnUnpackagedProcessHasNoPackage()
	{
		Assert.IsNull(AppIdentity.GetPackage((uint)Environment.ProcessId));
		Assert.IsNull(AppIdentity.GetPackage(0));
	}
}
