using System.IO.Ports;
using APS.Controls.Theme;

namespace APS.SystemControl.Forms;
public class SerialPortConsoleForm : Form
{
    private TextBox? txtConsole;
    private TextBox? txtInput;
    private Button? btnSend;
    private ComboBox? cmbPorts;
    private Button? btnConnect;
    private SerialPort? _serialPort;

    public SerialPortConsoleForm()
    {
        InitializeComponent();
        LoadAvailablePorts();
    }

    private void InitializeComponent()
    {
        this.txtConsole = new TextBox();
        this.txtInput = new TextBox();
        this.btnSend = new Button();
        this.cmbPorts = new APS.Controls.Theme.DarkComboBox();
        this.btnConnect = new Button();
        this.SuspendLayout();
        this.txtConsole.Location = new System.Drawing.Point(12, 12);
        this.txtConsole.Multiline = true;
        this.txtConsole.Name = "txtConsole";
        this.txtConsole.ScrollBars = ScrollBars.Vertical;
        this.txtConsole.Size = new System.Drawing.Size(360, 200);
        this.txtConsole.TabIndex = 0;
        this.txtInput.Location = new System.Drawing.Point(12, 218);
        this.txtInput.Name = "txtInput";
        this.txtInput.Size = new System.Drawing.Size(279, 20);
        this.txtInput.TabIndex = 1;
        this.btnSend.Location = new System.Drawing.Point(297, 216);
        this.btnSend.Name = "btnSend";
        this.btnSend.Size = new System.Drawing.Size(75, 23);
        this.btnSend.TabIndex = 2;
        this.btnSend.Text = "Send";
        this.btnSend.UseVisualStyleBackColor = true;
        this.btnSend.Click += new EventHandler(this.BtnSend_Click!);
        this.cmbPorts.FormattingEnabled = true;
        this.cmbPorts.Location = new System.Drawing.Point(12, 244);
        this.cmbPorts.Name = "cmbPorts";
        this.cmbPorts.Size = new System.Drawing.Size(121, 21);
        this.cmbPorts.TabIndex = 3;
        this.btnConnect.Location = new System.Drawing.Point(139, 242);
        this.btnConnect.Name = "btnConnect";
        this.btnConnect.Size = new System.Drawing.Size(75, 23);
        this.btnConnect.TabIndex = 4;
        this.btnConnect.Text = "Connect";
        this.btnConnect.UseVisualStyleBackColor = true;
        this.btnConnect.Click += new EventHandler(this.BtnConnect_Click!);
        this.ClientSize = new System.Drawing.Size(384, 281);
        this.Controls.Add(this.btnConnect);
        this.Controls.Add(this.cmbPorts);
        this.Controls.Add(this.btnSend);
        this.Controls.Add(this.txtInput);
        this.Controls.Add(this.txtConsole);
        this.Name = "SerialPortConsoleForm";
        this.Text = "Serial Port Console";
        this.ResumeLayout(false);
        this.PerformLayout();

        DarkModeHelper.FixControls(this);
    }

    private void LoadAvailablePorts()
    {
        cmbPorts!.Items.AddRange(SerialPort.GetPortNames());
    }

    private void BtnConnect_Click(object sender, EventArgs e)
    {
        if (_serialPort == null)
        {
            if (cmbPorts!.SelectedItem is not string portName || string.IsNullOrWhiteSpace(portName))
            {
                MessageBox.Show(this, "Select a COM port first.", "Serial Console",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var port = new SerialPort(portName) { BaudRate = 38400 };
            port.DataReceived += SerialPort_DataReceived;
            try
            {
                if (port.IsOpen) port.Close();
                port.Open();
            }
            catch (Exception ex)
            {
                // Open can throw (port busy / access denied) — clean up so we don't
                // leak a half-constructed port or a dangling DataReceived handler.
                port.DataReceived -= SerialPort_DataReceived;
                port.Dispose();
                MessageBox.Show(this, $"Failed to open {portName}: {ex.Message}", "Serial Console",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _serialPort = port;
            btnConnect!.Text = "Disconnect";
        }
        else
        {
            ClosePort();
            btnConnect!.Text = "Connect";
        }
    }

    /// <summary>
    /// Detaches the DataReceived handler BEFORE closing so an in-flight callback
    /// cannot dereference a nulled port, then closes and disposes the port and
    /// releases the COM handle. Safe to call when already disconnected.
    /// </summary>
    private void ClosePort()
    {
        if (_serialPort == null) return;
        _serialPort.DataReceived -= SerialPort_DataReceived;
        try { _serialPort.Close(); } catch { /* already closed / device gone */ }
        _serialPort.Dispose();
        _serialPort = null;
    }

    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        // Read from the event's own port, not the field, so a concurrent
        // Disconnect that nulls _serialPort cannot NRE this handler.
        if (sender is not SerialPort port) return;
        string data;
        try { data = port.ReadExisting(); }
        catch { return; }   // port closing/closed — drop the read

        // This runs on a ThreadPool thread. If the form has been disposed, Invoke
        // would throw here and, being off the UI thread, escape to
        // AppDomain.UnhandledException and terminate the whole process. Guard the
        // handle and post non-blocking.
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(new Action(() => txtConsole!.AppendText(data))); }
        catch (InvalidOperationException) { /* form torn down between check and post */ }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ClosePort();
        base.Dispose(disposing);
    }

    private void BtnSend_Click(object sender, EventArgs e)
    {
        if (_serialPort != null && _serialPort.IsOpen)
        {
            _serialPort.WriteLine(txtInput!.Text);
            txtInput.Clear();
        }
    }
}
