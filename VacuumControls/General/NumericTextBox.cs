using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Forms;

namespace APS.Controls.General;

/// <summary>
/// Mirrors the NumericUpDown surface so it can stand next to a FineSpinButtons
/// stepper without the duplicate-arrow visual.
/// </summary>
public class NumericTextBox : TextBox
{
    private decimal _value;
    private decimal _minimum;
    private decimal _maximum = 100m;
    private decimal _increment = 1m;
    private int _decimalPlaces;

    public event EventHandler? ValueChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Value
    {
        get => _value;
        set
        {
            decimal clamped = Clamp(value);
            if (clamped == _value) return;
            _value = clamped;
            UpdateText();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Minimum
    {
        get => _minimum;
        set { _minimum = value; Value = Clamp(_value); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Maximum
    {
        get => _maximum;
        set { _maximum = value; Value = Clamp(_value); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public decimal Increment
    {
        get => _increment;
        set => _increment = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int DecimalPlaces
    {
        get => _decimalPlaces;
        set { _decimalPlaces = value; UpdateText(); }
    }

    public NumericTextBox()
    {
        TextAlign = HorizontalAlignment.Center;
        UpdateText();
    }

    private decimal Clamp(decimal v)
    {
        if (v < _minimum) return _minimum;
        if (v > _maximum) return _maximum;
        return v;
    }

    private void UpdateText()
    {
        Text = _value.ToString("F" + _decimalPlaces, CultureInfo.CurrentCulture);
    }

    protected override void OnValidating(CancelEventArgs e)
    {
        Commit();
        base.OnValidating(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            Commit();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void Commit()
    {
        if (decimal.TryParse(Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed))
            Value = parsed;
        else
            UpdateText();
    }
}
