<h3 align="center">
  <br>
  <img width="200" src=".github/images/icon.png" alt="VolumeyEx">
  <br>
  VolumeyEx
</h3>
<h4 align="center">
  A volume mixer for Windows with global hotkeys for every app and device.
  <br>
  <br>
  <a href="https://github.com/SergioBlaster/VolumeyEx/releases/latest"><img src="https://img.shields.io/github/v/release/SergioBlaster/VolumeyEx" alt="Latest release"></a>
  <a href="https://github.com/SergioBlaster/VolumeyEx/releases"><img src="https://img.shields.io/github/downloads/SergioBlaster/VolumeyEx/total" alt="GitHub downloads"></a>
  <a href="https://github.com/SergioBlaster/VolumeyEx/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/SergioBlaster/VolumeyEx/build.yml?branch=main" alt="Build status"></a>
  <br>
  <br>
  A personal fork of <a href="https://github.com/G-Stas/Volumey">G-Stas/Volumey</a>.
</h4>

## Features
### Hotkeys
- **Any app:** turn a chosen app's volume up or down
- **Foreground app:** control whichever app is in focus
- **Output device:** change the volume of the default device or mute it
- **Default device:** switch to another output device with one key
- **Mute all apps:** one key mutes every app, the next gives each its state back
- **One app at a time:** each press leaves the next playing app audible and mutes the others, then all play again
- **Media keys:** play/pause, previous, next and stop
- **Mixer:** open the volume mixer
- **Two modes:** keep the keys to VolumeyEx, or share them with other apps (handy in games)
- **Your step:** pick how much each press changes the volume

### Volume mixer
- Opens from the tray icon or by a hotkey
- A compact pop-up next to the tray or a resizable window
- Volume and mute for every app, each of its audio sessions and the output device

### More
- **On-screen notifications** in the corner of the screen you choose
- **Volume limit:** apps that start at 100% start at your preferred volume instead
- **Volume sync:** keep app volumes in step with the device volume

## Preview
<p>
  <img height="420" src=".github/images/mixer.png" alt="The volume mixer">
</p>

## Download
- [GitHub Releases](https://github.com/SergioBlaster/VolumeyEx/releases)

## Supported OS
`Windows 11` `Windows 10 19041.0+`

## Built With
- [WPF UI](https://github.com/lepoco/wpfui)
- [NAudio](https://github.com/naudio/NAudio)
- [SharpHook](https://github.com/TolikPylypchuk/SharpHook)
- [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon)
- [AutoUpdater.NET](https://github.com/ravibpatel/AutoUpdater.NET)

## License
VolumeyEx is released under the [GPL-3.0 License](https://github.com/SergioBlaster/VolumeyEx/blob/main/LICENSE).
