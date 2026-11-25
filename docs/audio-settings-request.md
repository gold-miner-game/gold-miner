# Request: sound settings in the UI

Owner of the sound: Claude (sound director). Owner of this request: whoever builds menus and the pause popup.
The sound side is already done. The game plays music and effects and saves volume settings. Nothing
lets the player change them yet. This request is only the UI part.

## What the player needs

1. **Music volume** slider, 0–100 %, default 70 %.
2. **Sound effects volume** slider, 0–100 %, default 100 %. This covers gameplay sounds and the mine ambience.
3. **Menu sounds volume** slider, 0–100 %, default 100 %. This covers button clicks, the store and character pick.
4. **Mute all** toggle, default off. The sliders keep their values while muted.
5. Optional: **Master volume** slider, 0–100 %, default 100 %. Leave it out if space is tight.

## Where

- In the **pause popup** (HUD, Esc) as a small "Sound" section or a "Sound" button that opens a panel.
  Players need it mid-level.
- In the **main menu** as an "Options / Sound" entry, so it can be set before playing.
- Optional: an **M** key that toggles mute, shown in the pause popup as a hint.

## How to wire it (no audio knowledge needed)

```csharp
// read, to place the sliders
float music = SoundDirector.GetVolume(SoundDirector.Volume.Music);   // 0..1
float sfx   = SoundDirector.GetVolume(SoundDirector.Volume.Sfx);
float ui    = SoundDirector.GetVolume(SoundDirector.Volume.Ui);
float all   = SoundDirector.GetVolume(SoundDirector.Volume.Master);
bool muted  = SoundDirector.Muted;

// write, on slider change (applies at once and is saved in PlayerPrefs)
SoundDirector.SetVolume(SoundDirector.Volume.Music, value01);
SoundDirector.Muted = isOn;
```

- Saving is automatic, so you don't need to call PlayerPrefs.Save().
- Changes take effect immediately, music included, so the player can hear the slider while dragging it.
- When the player lets go of the **effects** or **menu** slider, call `Sfx.Ui("ui_click")` (menu) or
  `Sfx.Play("coin_small")` (effects) so they hear the new level.
- The pause popup stops time (`Time.timeScale = 0`). Menu sounds still play while paused, so the preview works.
- uGUI buttons and sliders click by themselves (SoundDirector listens to the EventSystem), so don't add a click
  sound to them. IMGUI (`GUI.Button`) buttons need `Sfx.Ui("ui_click")` when pressed, as MainMenu does.

## Look and feel

Follow the papercraft art rules (docs/art-style.md): paper-tag slider handles, Fredoka labels. Show
percentages as whole numbers.

## Done when

- All four controls are in the pause popup and the main menu.
- Values survive quitting and relaunching the game, including in the WebGL build.
- Muting silences everything, including music already playing, and unmuting brings back the same levels.
