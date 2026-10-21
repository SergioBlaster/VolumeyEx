using Microsoft.VisualStudio.TestTools.UnitTesting;
using VolumeyEx.DataProvider;
using VolumeyEx.DataProvider.Settings;

namespace VolumeyEx.Tests;

[TestClass]
public static class TestAssemblySetup
{
	/// <summary>
	/// Keeps tests away from the real settings file of the machine.
	/// </summary>
	[AssemblyInitialize]
	public static void Initialize(TestContext context)
		=> SettingsProvider.UseSettings(new VolumeyExSettings());
}
