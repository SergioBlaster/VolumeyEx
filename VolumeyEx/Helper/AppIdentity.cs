#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using log4net;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace VolumeyEx.Helper;

/// <summary>
/// The app behind an audio session, found the way Windows' volume mixer finds it: the name the app gave its session first,
/// then its package's name (a Store app, and its helper processes: WhatsApp's sound plays in a WebView2 process of its package),
/// then the description of its executable
/// </summary>
internal static class AppIdentity
{
	private static readonly ILog Logger = LogManager.GetLogger(typeof(AppIdentity));

	/// <param name="DisplayName">The package's name as the Start menu shows it</param>
	/// <param name="LogoPath">The package's logo image, null if it has none</param>
	internal sealed record PackageInfo(string DisplayName, string? LogoPath);

	//by package full name (one per version): read from Windows once per run. Null: the package couldn't be read
	private static readonly ConcurrentDictionary<string, PackageInfo?> Packages = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// The app's name: the session's own name, the package's, the executable's description, else <paramref name="fallback"/>
	/// </summary>
	/// <param name="sessionDisplayName">The name the app gave its session (IAudioSessionControl::GetDisplayName), often empty</param>
	/// <param name="path">The app's executable, null if it couldn't be read</param>
	/// <param name="fallback">The process name</param>
	internal static string GetAppName(string? sessionDisplayName, PackageInfo? package, string? path, string fallback)
	{
		if(ResolveDisplayName(sessionDisplayName) is { } name)
			return name;
		if(package != null && !string.IsNullOrWhiteSpace(package.DisplayName))
			return package.DisplayName.Trim();
		if(GetFileDescription(path) is { } description)
			return description;
		return fallback;
	}

	/// <summary>
	/// A session's display name as text: an indirect string ("@dll,-id", as Windows' own sessions give it) is read from its resource.
	/// Null if the name is empty or its resource can't be read
	/// </summary>
	internal static string? ResolveDisplayName(string? displayName)
	{
		if(string.IsNullOrWhiteSpace(displayName))
			return null;
		displayName = displayName.Trim();
		if(!displayName.StartsWith('@'))
			return displayName;
		try
		{
			Span<char> buffer = stackalloc char[512];
			string? resolved = LoadIndirectString(displayName, buffer);
			return string.IsNullOrWhiteSpace(resolved) ? null : resolved.Trim();
		}
		catch(Exception e)
		{
			Logger.Debug($"Couldn't read the session name [{displayName}]", e);
			return null;
		}
	}

	private static unsafe string? LoadIndirectString(string source, Span<char> buffer)
	{
		fixed(char* input = source)
		fixed(char* output = buffer)
		{
			HRESULT result = PInvoke.SHLoadIndirectString(new PCWSTR(input), new PWSTR(output), (uint)buffer.Length, null);
			return result.Succeeded ? new string(output) : null;
		}
	}

	/// <summary>The description in the executable's version info (what Task Manager shows), null if it has none</summary>
	internal static string? GetFileDescription(string? path)
	{
		if(path == null || !Path.IsPathFullyQualified(path))
			return null;
		try
		{
			//from the file, not the process: it also works for apps that can't be opened (elevated, guarded by anti-cheat)
			return FileVersionInfo.GetVersionInfo(path).FileDescription is { } description && !string.IsNullOrWhiteSpace(description)
				? description.Trim()
				: null;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// The package the process runs in, null if it isn't packaged or can't be opened
	/// </summary>
	internal static PackageInfo? GetPackage(uint processId)
	{
		if(processId == 0)
			return null;
		string? fullName = GetPackageFullName(processId);
		return fullName == null ? null : Packages.GetOrAdd(fullName, ReadPackage);
	}

	private static string? GetPackageFullName(uint processId)
	{
		using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
		if(process.IsInvalid)
			return null;
		//a package full name is at most 127 characters; an unpackaged process gives APPMODEL_ERROR_NO_PACKAGE
		Span<char> buffer = stackalloc char[128];
		uint length = (uint)buffer.Length;
		WIN32_ERROR result = PInvoke.GetPackageFullName(process, ref length, buffer);
		return result == WIN32_ERROR.ERROR_SUCCESS && length > 1 ? buffer[..((int)length - 1)].ToString() : null;
	}

	private static PackageInfo? ReadPackage(string fullName)
	{
		try
		{
			var package = new Windows.Management.Deployment.PackageManager().FindPackageForUser(string.Empty, fullName);
			if(package == null)
				return null;
			string? logo = null;
			try
			{
				if(package.Logo is { IsFile: true } uri && File.Exists(uri.LocalPath))
					logo = uri.LocalPath;
			}
			catch { }
			return new PackageInfo(package.DisplayName, logo);
		}
		catch(Exception e)
		{
			Logger.Debug($"Couldn't read the package [{fullName}]", e);
			return null;
		}
	}
}
