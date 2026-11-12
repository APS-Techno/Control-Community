using APS.Controls.Theme;
using APS.RecipeModel.Model;

namespace APS.RecipeModel.Controls;

public class RecipeParamForm : UserControl
{
    private readonly Label _titleLabel;
    private readonly DarkScrollPanel _scrollPanel;
    private readonly List<(string Name, TextBox Edit)> _edits = [];

    public RecipeFile? Recipe { get; private set; }

    public RecipeParamForm()
    {
        AutoScroll = false;
        Padding = new Padding(DpiScale.Px(6));

        _titleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = DpiScale.Px(32),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, DpiScale.Px(4))
        };
        Controls.Add(_titleLabel);

        _scrollPanel = new DarkScrollPanel
        {
            Dock = DockStyle.Fill,
        };
        Controls.Add(_scrollPanel);

        _scrollPanel.BringToFront();
    }

    public void LoadRecipe(RecipeFile recipe)
    {
        Recipe = recipe;

        SuspendLayout();
        _scrollPanel.BeginUpdate();
        try
        {
            Clear();
            _titleLabel.Text = recipe.Description.Title;

            // Build groups bottom-up so Dock=Top stacks them in correct order
            for (int g = recipe.Parameters.Count - 1; g >= 0; g--)
            {
                var group = recipe.Parameters[g];
                AddGroup(group);
            }

            DarkModeHelper.FixControls(this);
        }
        finally
        {
            _scrollPanel.EndUpdate();
            ResumeLayout(true);
        }

        AutoFitHeight();
    }

    /// <summary>Only effective when Dock is Top or None.</summary>
    private void AutoFitHeight()
    {
        int total = _titleLabel.Height + _titleLabel.Margin.Vertical;
        foreach (Control c in _scrollPanel.ContentPanel.Controls)
        {
            total += c.Height + c.Margin.Vertical;
        }
        Height = total + Padding.Vertical + DpiScale.Px(4);
    }

    public Dictionary<string, string> GetParameterValues()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var paramsByName = Recipe?.Parameters
            .SelectMany(g => g.Params)
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, RecipeParam>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, edit) in _edits)
        {
            string value = edit.Text.Trim();
            if (paramsByName.TryGetValue(name, out var param))
                value = AppendUnitSuffix(value, param.Unit);
            result[name] = value;
        }
        return result;
    }

    // Pressure and time params declare their unit in the .rcpx (e.g.
    // unit="mTorr", unit="min"). Recipe variables are substituted as plain
    // text into the DSL, where bare numbers in pwait/swait positions are
    // interpreted as Torr by PressureParser, and bare numbers in
    // wait/warmup/timeout/deposit positions are interpreted as seconds by
    // TimeParser. Append the matching suffix ONLY for non-canonical units so
    // "2.00" + unit "mTorr" becomes "2.00mT" and "30" + unit "min" becomes
    // "30m", so the runtime resolves the value in the operator's intended
    // units. Skip if the operator already typed an explicit suffix.
    //
    // Deliberately NOT suffixed: the canonical base units "Torr" and "sec".
    // A bare number is ALREADY interpreted in those units at every position,
    // so the suffix ("30" -> "30s", "0.05" -> "0.05T") is pure redundancy —
    // and it breaks positions whose argument binder does a plain float.Parse
    // rather than going through TimeParser/PressureParser (deposit dwell, bias
    // delay). Leaving canonical-unit values bare keeps them numeric everywhere.
    //
    // Limitation: RecipeParam.Unit is a free-text caption string (whatever
    // the .rcpx author wrote), not a validated enum, so this only catches
    // the unit spellings actually in use ("min"/"hour" plus common synonyms).
    // An author who writes an unrecognized spelling (e.g. "mins") still gets
    // a bare, silently-seconds substitution.
    private static string AppendUnitSuffix(string value, string unit)
    {
        if (string.IsNullOrEmpty(value)) return value;
        string suffix = unit switch
        {
            "mTorr"  => "mT",
            "uTorr"  => "uT",
            "µTorr"  => "uT",
            "nTorr"  => "nT",
            "min" or "minute" or "minutes"     => "m",
            "hour" or "hours" or "hr" or "hrs" => "h",
            // "Torr" and "sec"/"seconds" are canonical (bare = that unit) — no suffix.
            _        => "",
        };
        if (suffix.Length == 0) return value;
        if (char.IsLetter(value[^1]))
            return value;
        return value + suffix;
    }

    private void Clear()
    {
        _edits.Clear();
        _scrollPanel.ContentPanel.Controls.Clear();
    }

    private void AddGroup(ParamGroup group)
    {
        var groupBox = new GroupBox
        {
            Text = group.Name,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Padding = new Padding(DpiScale.Px(8), DpiScale.Px(4), DpiScale.Px(8), DpiScale.Px(8)),
            Margin = new Padding(DpiScale.Px(4), DpiScale.Px(4), DpiScale.Px(4), DpiScale.Px(10))
        };

        var flowPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(DpiScale.Px(4), DpiScale.Px(8), DpiScale.Px(4), DpiScale.Px(4))
        };

        // Track the tallest field so the row height follows real font metrics
        // (fields self-size from PreferredHeight, so they grow with DPI).
        int fieldHeight = 0;
        foreach (var param in group.Params)
        {
            var container = CreateParamField(param);
            fieldHeight = Math.Max(fieldHeight, container.Height + container.Margin.Vertical);
            flowPanel.Controls.Add(container);
        }

        groupBox.Controls.Add(flowPanel);

        // Both sides of the division are 96-DPI logical values, so the row
        // estimate does not depend on DPI.
        int paramsPerRow = Math.Max(1, (Width > 0 ? Width - DpiScale.Px(40) : DpiScale.Px(400)) / DpiScale.Px(160));
        int rows = (int)Math.Ceiling((double)group.Params.Count / paramsPerRow);
        groupBox.Height = DpiScale.Px(30) + rows * (fieldHeight + DpiScale.Px(6));

        _scrollPanel.ContentPanel.Controls.Add(groupBox);
    }

    private Panel CreateParamField(RecipeParam param)
    {
        string caption = string.IsNullOrEmpty(param.Unit)
            ? param.Caption
            : $"{param.Caption} ({param.Unit})";

        var label = new Label
        {
            Text = caption,
            AutoSize = true,
            Font = new Font("Segoe UI", 8f, FontStyle.Regular),
            Margin = new Padding(0)
        };

        var textBox = new TextBox
        {
            Text = param.Value,
            Width = DpiScale.Px(70),
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            Tag = param,
            Margin = new Padding(0, 1, 0, 0)
        };

        int labelWidth = TextRenderer.MeasureText(caption, label.Font).Width;
        int containerWidth = Math.Max(labelWidth, textBox.Width) + DpiScale.Px(4);

        // Stack height from real font metrics so the field never clips at high DPI.
        int textBoxTop = label.PreferredHeight + DpiScale.Px(2);
        var container = new Panel
        {
            Width = containerWidth,
            Height = textBoxTop + textBox.PreferredHeight + DpiScale.Px(4),
            Margin = new Padding(DpiScale.Px(6), DpiScale.Px(2), DpiScale.Px(6), DpiScale.Px(2))
        };

        label.Location = new Point(0, 0);
        textBox.Location = new Point(0, textBoxTop);

        container.Controls.Add(textBox);
        container.Controls.Add(label);

        _edits.Add((param.Name, textBox));

        return container;
    }
}
