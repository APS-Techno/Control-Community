# APS Dark Theme Design Guide

Shared theme conventions for Control, Builder, Emulator, RecipeEditor, and
LicenseAdmin. Established during the RecipeEditor polish pass (2026-04-22).
Apply these to any form/control in any of the five apps for visual consistency.

## 1. Color palette (single source of truth)

All colors live in `VacuumControls/Theme/DarkModeHelper.cs` and
`VacuumControls/Theme/DarkTabControl.cs`. **Do not define ad-hoc colors in
forms** — reference these or the `Color.FromArgb(...)` RGB values below.

| Role          | RGB                | Hex       | Where used                                                                                  |
| ------------- | ------------------ | --------- | ------------------------------------------------------------------------------------------- |
| `BgDark`      | `13, 27, 42`       | `#0D1B2A` | **Everything.** Panel bg, form bg, menu/toolbar/status strip, tab strip, tab page, trees, grids, text inputs, scrollbars. |
| `BgPanel`     | *= BgDark*         | `#0D1B2A` | Aliased to BgDark — kept as a name for legacy call sites but never a different tone.        |
| `BgHover`     | `30, 44, 66`       | `#1E2C42` | Hover states only (buttons, menu items, scrollbar thumb hover, palette button hover).       |
| `BgInput`     | `18, 30, 48`       | `#121E30` | TextBox/ComboBox/NumericUpDown fill (input fields keep a slight contrast from chrome).      |
| `Border`      | `40, 60, 90`       | `#28375A` | 1px frame painted around elements that don't get a native Fixed3D border.                   |
| `Accent`      | `72, 169, 254`     | `#48A9FE` | Selection highlight, scrollbar thumb during drag, selected tab underline.                   |
| `TextPrimary` | `200, 214, 229`    | `#C8D6E5` | Default foreground.                                                                         |
| `TextDim`     | `119, 136, 153`    | `#778899` | Labels, column headers, captions.                                                           |

**The single dark tone** (`BgDark`) is the whole story. Apps earlier had a
lighter "panel" tone (`#162032`) — that's been collapsed. If something looks
wrong because it's BgPanel and you want it darker, it's already BgDark.

## 2. Required boilerplate for every new Form

```csharp
public MyForm()
{
    InitializeComponent();
    // ...build UI...
    APS.Controls.Theme.DarkModeHelper.FixControls(this);
}
```

If you create controls dynamically after `FixControls(this)` runs, call the
helper on the new subtree:

```csharp
InitBlockEditor(); // adds child controls
DarkModeHelper.FixControls(_tabVisualBuilder);
```

`DarkModeHelper` handles: window titlebar dark-mode (`DWMWA_USE_IMMERSIVE_DARK_MODE`),
dark Win32 scrollbars (`DarkMode_Explorer` theme) on TreeView/RichTextBox/
TextBox/ListBox/DataGridView/AutoScroll Panels, menu/toolbar/status strip
rendering, DGV headers, propgrid, etc.

## 3. Tab controls

Always use `APS.Controls.Theme.DarkTabControl` instead of `TabControl`.
It paints its own strip, page background, accent underline, and a 1px outer
frame — no additional work needed.

## 4. Element framing (how the left column in RecipeEditor looks)

The reference look is the params tree: a dark "well" with a thin 1px gray
border, sitting inside a container with 3px padding so there's a visible gap
to neighbouring elements.

| Control                       | How to frame                                                                    |
| ----------------------------- | ------------------------------------------------------------------------------- |
| `TreeView`, `ListBox`, `RichTextBox` | Default `BorderStyle = Fixed3D` renders as a thin gray line in dark mode. Leave as-is. |
| `DataGridView`                | Default `FixedSingle` renders near-black. Set `BorderStyle = Fixed3D`.          |
| `Panel` hosting content       | Set `Padding = new Padding(1)` and paint a 1px border in `Paint`: `e.Graphics.DrawRectangle(new Pen(SystemColors.ControlDark), 0, 0, r.Width-1, r.Height-1)`. |
| Custom `Control`/`Panel` class (palette, canvas, preview) | Override `OnPaint` and draw the same 1px rectangle. Color: `SystemColors.ControlDark` for UI containers, `#28375A` (`Border`) for block-style elements. |

**Use `SystemColors.ControlDark` for frames wherever possible** — dark mode
WinForms theming remaps it to match Fixed3D's shade, so custom-painted frames
and native Fixed3D borders look identical.

## 5. Container padding (spacing between framed elements)

Containers that host multiple framed elements should use `Padding(3)` on the
relevant split panel, and a 3px BgDark spacer `Panel { Dock = Top, Height = 3 }`
between stacked Dock-Top controls.

Pattern from `FrmMain.cs`:

```csharp
var pad = new Padding(3);
_splitOuter.Panel1.Padding  = pad;  // params column
_splitCenter.Panel1.Padding = pad;  // central column (tab control)
_splitRight.Panel2.Padding  = pad;  // right column (preview + reference)

// Spacer between preview (Top) and reference (Fill):
_splitRight.Panel2.Controls.Add(_referencePanel);
_splitRight.Panel2.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 3 });
_splitRight.Panel2.Controls.Add(_preview);
```

## 6. Scrollbars — use custom, not Win11 native

Win11's `DarkMode_Explorer` native scrollbar doesn't match our palette.
Three classes in `VacuumControls.Theme` replace them:

### 6.1 `DarkScrollBar` — painted scrollbar

12px thick, armless, `Border` idle / `BgHover` hover / `Accent` drag thumb.
Vertical or horizontal. Use directly only when driving scroll manually.

### 6.2 `DarkScrollPanel` — drop-in replacement for `Panel { AutoScroll = true }`

Reparents children into an internal content panel, shows a DarkScrollBar when
content overflows, routes mouse-wheel via `IMessageFilter`. Children added via
`Controls.Add` are automatically reparented.

**Caveat** — children layout uses `ContentPanel.ClientSize.Width`, not the
outer `ClientSize.Width`. Update layout math accordingly.

```csharp
public sealed class MyPalette : DarkScrollPanel
{
    public MyPalette()
    {
        ContentPanel.Padding = new Padding(6);
        // ... add children via Controls.Add as usual
    }
}
```

Used by: `BlockPalette`, `RecipeParamForm._scrollPanel`.

### 6.3 `DarkScrollHost` — wrapper for controls with native Win32 scrollbars

Wrap any `RichTextBox`, `TreeView`, `ListView`, or `Panel { AutoScroll = true }`:

```csharp
_referencePanel.Controls.Add(new DarkScrollHost(_txtReference) { Dock = DockStyle.Fill });
```

Hides native scrollbars via `ShowScrollBar(false)` (polled 100 ms to stay
hidden), queries scroll state control-specifically (RTB via line count, tree
via visible-node walk, others via `GetScrollInfo`), and drives scroll via
`EM_LINESCROLL` (RTB), `TopNode = ...` (TreeView), or `WM_VSCROLL`/`WM_HSCROLL`
(generic). Handles both axes.

**Already wrapped in RecipeEditor:** `_txtReference`, `_txtEditor`, ParamDesigner's
`_tree`, `_blockStack`.

**Not yet wrapped:** DataGridView (has its own managed scroll; needs different
approach — leave with `DarkMode_Explorer` theming for now).

## 7. Migration checklist for other apps

For each app (Control, Builder, Emulator, LicenseAdmin):

1. **Replace `TabControl` with `DarkTabControl`** everywhere.
2. **Remove ad-hoc color constants** — if a form defines its own "panel"/"bg"
   color, delete it and rely on `DarkModeHelper`.
3. **Strip `Padding(0, 0, 8, 0)` / similar scrollbar-reserve hacks** — the new
   `DarkScrollHost` takes its own space and handles this.
4. **Wrap native-scrollbar controls** in `DarkScrollHost`:
   - `RichTextBox`, `TreeView`, `ListBox`, `Panel { AutoScroll = true }`
5. **Replace `Panel { AutoScroll = true }` with `DarkScrollPanel`** where the
   panel hosts custom-positioned children.
6. **Add 1px frames** to `Panel`s that host distinct content (reference-panel
   pattern — see §4).
7. **Confirm container padding** — every framed child should sit in a
   `Padding(3)`-ed split panel or equivalent; 3px spacers between stacked
   Top-docked elements.
8. **Call `DarkModeHelper.FixControls(this)`** at the end of every form ctor.
   If you populate controls in a later init method, call it on that subtree
   too.

## 8. What NOT to do

- Don't set `BackColor = Color.FromArgb(22, 32, 50)` (the old BgPanel). If you
  see this in a form, replace with `Color.FromArgb(13, 27, 42)` or delete and
  let `DarkModeHelper` handle it.
- Don't use `BorderStyle = FixedSingle` on DGV or custom painted borders of
  `Pen(Color.Black)` — both render jet-black and break the consistency.
- Don't rely on `Application.SetColorMode(SystemColorMode.Dark)` alone for
  scrollbars — it gives you Win11's gray native scrollbar, which doesn't match.
- **Never test "are we dark?" with the .NET 9 color-mode APIs directly.**
  `Application.SystemColorMode` returns the **OS** theme (Classic on a
  light-themed Windows box), and `Application.ColorMode` was observed NOT to
  report `Dark` even after `SetColorMode(Dark)` on a light-OS machine — both
  leave forms half-themed (dark chrome, white grids/buttons). Always test
  **`VacuumTheme.IsDark`** for dark-mode branches.
- **Always-dark apps (Emulator, Control) must pin the theme.** In `Program.cs`,
  after `ApplicationConfiguration.Initialize()`, call
  `Application.SetColorMode(SystemColorMode.Dark)` (native chrome) AND
  `VacuumTheme.ForceDark = true` (makes `VacuumTheme.IsDark` return `true`
  unconditionally, bypassing the unreliable APIs so `FixControls` always runs).
  Do this before any window is created.
- Don't strip `WS_VSCROLL` via `SetWindowLong` to hide native bars. It breaks
  the control's scroll machinery. `ShowScrollBar(false)` + `DarkScrollHost`'s
  polling approach is the right path.
- Don't `Controls.Add` a control to a `DarkScrollPanel` expecting it to be
  a direct child — it gets reparented to `ContentPanel`. If you need a handle
  to the actual parent, use `ContentPanel`.

## 9. Known quirks

- **Mouse wheel over custom-painted children of `DarkScrollPanel`** works via
  `IMessageFilter` pointing at the panel's `ClientRectangle`. If two
  `DarkScrollPanel`s nest, both filters trigger — acceptable because the
  inner panel's `ClientRectangle` is smaller, so the outer only fires when
  you're genuinely in its area.
- **Polling cost** — `DarkScrollHost`'s 100 ms timer queries scroll state. At
  ~10 Hz per scrollable control, negligible, but don't wrap hundreds of them.
- **DGV** keeps its WinForms-managed scrollbar + `DarkMode_Explorer` theming.
  It's Win11-gray, not our palette. Acceptable compromise until we write a
  DGV-specific wrapper.

## 10. Numeric readout status

`LEDPanel.UpdateState` keeps unavailable measurements distinct from numeric zero:

| Input | Display |
|---|---|
| Finite value | The configured numeric format |
| Positive infinity | `OVER` |
| Negative infinity | `UNDER` |
| NaN | `INVALID` |

Pressure models use these states for measurement-range limits. Keep the status when recording data. Do not replace it with zero. Numeric plots must treat non-finite values as gaps, not as axis limits.
