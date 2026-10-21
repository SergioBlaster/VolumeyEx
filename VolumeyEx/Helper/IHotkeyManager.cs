#nullable enable
using System;
using System.Collections.Generic;
using VolumeyEx.Controls;

namespace VolumeyEx.Helper;

public interface IHotkeyManager : IDisposable
{
	event Action<HotKey>? HotkeyPressed;
	int RegisteredHotkeysCount { get; }
	IReadOnlyList<HotKey> GetRegisteredHotkeys();
	void RegisterHotkey(HotKey key);
	void UnregisterHotkey(HotKey key);
}
