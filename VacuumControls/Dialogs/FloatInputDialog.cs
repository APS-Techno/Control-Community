using System.Globalization;
using APS.Controls.Theme;

namespace APS.Controls.Dialogs;
public class FloatInputDialog : Form
{
    /// <summary>
    /// Accepts both '.' and ',' as the decimal separator regardless of OS locale:
    /// InvariantCulture first, then the current culture. NumberStyles.Float excludes
    /// thousands separators so the two never collide.
    /// </summary>
    public static bool TryParseFloat(string? text, out float result)
    {
        text = text?.Trim() ?? "";
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
            || float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
    }

    private Button? buttonOk;
    private Button? buttonCancel;
    private TextBox? textBoxInput;
    private Label? labelPrompt;

    public float? InputValue { get; private set; }

    public FloatInputDialog(string prompt)
    {
        InitializeComponent();
        labelPrompt!.Text = prompt;
        DarkModeHelper.FixControls(this);
    }

    private void InitializeComponent()
    {
        // Layout values are 96-DPI logical pixels; raw pixels (button height included)
        // do not scale with DPI on their own. AutoScaleMode.None: we scale manually,
        // so WinForms must not also font-autoscale (see DpiScale).
        AutoScaleMode = AutoScaleMode.None;

        labelPrompt = new Label();
        textBoxInput = new TextBox();
        buttonOk = new Button();
        buttonCancel = new Button();

        labelPrompt.AutoSize = true;
        labelPrompt.Location = new Point(DpiScale.Px(12), DpiScale.Px(9));
        labelPrompt.Text = "Prompt";

        textBoxInput.Location = new Point(DpiScale.Px(15), DpiScale.Px(35));
        textBoxInput.Size = new Size(DpiScale.Px(257), DpiScale.Px(23));

        buttonOk.Text = "OK";
        buttonOk.DialogResult = DialogResult.OK;
        buttonOk.Size = new Size(DpiScale.Px(75), DpiScale.Px(28));
        buttonOk.Location = new Point(DpiScale.Px(116), DpiScale.Px(70));

        buttonCancel.Text = "Cancel";
        buttonCancel.DialogResult = DialogResult.Cancel;
        buttonCancel.Size = new Size(DpiScale.Px(75), DpiScale.Px(28));
        buttonCancel.Location = new Point(DpiScale.Px(197), DpiScale.Px(70));

        AcceptButton = buttonOk;
        CancelButton = buttonCancel;

        Controls.Add(labelPrompt);
        Controls.Add(textBoxInput);
        Controls.Add(buttonOk);
        Controls.Add(buttonCancel);

        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(DpiScale.Px(284), DpiScale.Px(111));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (DialogResult == DialogResult.OK)
        {
            if (TryParseFloat(textBoxInput!.Text, out float result))
            {
                InputValue = result;
            }
            else
            {
                ThemedMessageBox.Show(this, "Please enter a valid float value.", "Invalid Input", MessageBoxButtons.OK, MessageBoxIcon.Error);
                e.Cancel = true;
            }
        }
    }
}
