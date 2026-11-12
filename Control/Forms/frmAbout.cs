using APS.Controls.Theme;

namespace APS.SystemControl.Forms;

public class FrmAbout : Form
{
    public FrmAbout()
    {
        APS.SystemControl.Core.UIScaling.ApplyToForm(this);
        Text = "About";
        StartPosition = FormStartPosition.CenterScreen;
        // Pixel values are 96-DPI logical, scaled once via DpiScale.Px.
        Size = new Size(DpiScale.Px(300), DpiScale.Px(300));

        ControlBox = false;

        PictureBox logo = new()
        {
            Image = Properties.Resources.SystemPicture,
            SizeMode = PictureBoxSizeMode.StretchImage,
            Size = new Size(DpiScale.Px(100), DpiScale.Px(100))
        };
        logo.Location = new Point((ClientSize.Width - logo.Width) / 2, DpiScale.Px(10));

        string version = APS.SystemControl.Core.AppVersion.Full;

        Label label = new()
        {
            Text = "Vacuum Sysytem Controller",
            TextAlign = ContentAlignment.TopCenter,
            Font = new Font("Arial", 12, FontStyle.Bold),
            Width = ClientSize.Width - DpiScale.Px(20),
            Location = new Point(DpiScale.Px(10), logo.Bottom + DpiScale.Px(10))
        };
        label.Height = label.PreferredHeight;

        Label labelV = new()
        {
            Text = $"Version: {version}",
            TextAlign = ContentAlignment.TopCenter,
            Width = ClientSize.Width - DpiScale.Px(20),
            Location = new Point(DpiScale.Px(10), label.Bottom + DpiScale.Px(10))
        };
        labelV.Height = labelV.PreferredHeight;

        Label labelC = new()
        {
            Text = "(c) 2024 APS Technology",
            TextAlign = ContentAlignment.TopCenter,
            Font = new Font("Arial", 10, FontStyle.Bold),
            Width = ClientSize.Width - DpiScale.Px(20),
            Location = new Point(DpiScale.Px(10), labelV.Bottom + DpiScale.Px(10))
        };
        labelC.Height = labelC.PreferredHeight;

        Button closeButton = new()
        {
            Text = "Close",
            Size = new Size(DpiScale.Px(75), DpiScale.Px(26))
        };
        closeButton.Location = new Point((ClientSize.Width - closeButton.Width) / 2, labelC.Bottom + DpiScale.Px(20));
        closeButton.Click += (sender, e) => Close();

        Controls.Add(logo);
        Controls.Add(label);
        Controls.Add(labelV);
        Controls.Add(labelC);
        Controls.Add(closeButton);

        DarkModeHelper.FixControls(this);
    }
}
