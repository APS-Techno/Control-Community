using System.Reflection;
using System.Xml.Linq;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.XML;

namespace APS.SystemControl.Core;

public partial class Core
{
    public string LastErrorMessage = "";

    private XMLHardware? xmlHwConf;

    /// <summary>Absolute path of the HWConf.xml currently in use. Captured on
    /// load so calibration changes (drive offset/reverse-offset edits) can
    /// be persisted back to the same file.</summary>
    private string? _hwConfPath;


    private readonly Assemblies HWDlls = [];

    public void SendErrorMesssage(string message)
    {
        guiwriter.TryWrite(new ValMsg<string>(MsgType.Error, DeviceType.System, "HW Loader", $"Hardware error: {message}"));
    }

    public void SendLogMesssage(string eventtype, string message)
    {
        guiwriter.TryWrite(new ValMsg<string>(MsgType.Log, DeviceType.System, eventtype, message));
    }

    public void WriteGUI(ThreadMsg msg) => guiwriter.TryWrite(msg);


    public bool LoadHWConfigFromFile(string confFileName, string dllBasePath, Action<string, int>? progress = null)
    {
        bool result = false;
        LastErrorMessage = "";

        try
        {
            _hwConfPath = confFileName;
            xmlHwConf = new XMLHardware(confFileName);
            if (xmlHwConf != null)
            {
                SendLogMesssage("Core", $"Hardware configuration loaded from {confFileName}");

                progress?.Invoke("Loading hardware libraries...", 35);
                foreach (XElement xml_lib in xmlHwConf.Libs!.Items)
                {
                    var Atr = XMLHardware.Attributes(xml_lib);
                    string dllPath = Path.Combine(dllBasePath, xml_lib.Value);

                    // Verify DLL integrity before Assembly.LoadFrom. If the
                    // <library> element carries a sha256="..." attribute, the
                    // binary MUST match or loading aborts. Absent pinning logs
                    // a warning with the current hash for operator copy-paste.
                    string? expectedHash = xml_lib.Attribute("sha256")?.Value;
                    DllHashVerifier.Verify(dllPath, expectedHash,
                        msg => SendLogMesssage("Core", msg));

                    Assembly? hardwareDll = Assembly.LoadFrom(dllPath);
                    if (hardwareDll != null)
                    {
                        HWDlls.Add((string)Atr[1], hardwareDll);
                        SendLogMesssage("Core", $"Hardware components loaded from {dllPath}");
                    }

                }

                progress?.Invoke("Connecting to PLCs...", 40);
                if (!LoadPLCs(xmlHwConf.PLCs)) throw new InvalidOperationException($"PLC not found.");

                progress?.Invoke("Loading hardware elements...", 48);
                HwXmlTypeMapper xmlTypeMapper = new();

                foreach (KeyValuePair<DeviceType, Type> xmlEntry in xmlTypeMapper.Type)
                {
                    foreach (var section in xmlHwConf.Sections[HwXmlTypeMapper.Name(xmlEntry.Key)])
                    {
                        if (!CallLoadWHSection(xmlEntry.Key, section, xmlEntry.Value))
                            throw new InvalidOperationException($"Loading {xmlEntry.Key} failed.");
                    }
                }

                // Subscribe to MFC setpoint changes for group-transition evaluation
                if (hwElements.Item.TryGetValue(DeviceType.MFC, out var mfcElements))
                {
                    foreach (var kvp in mfcElements)
                    {
                        if (kvp.Value is BaseMFC mfc)
                            mfc.SetpointChanged += _ => CheckGroupTransitionAndLog($"MFC {mfc.Params.ID} setpoint change");
                        if (kvp.Value is PLCMFC plcMfc)
                        {
                            plcMfc.WriteFailed += failedValue =>
                                SendLogMesssage(plcMfc.Params.ID, $"WRITE_FAILED:{failedValue:F2} sccm");
                        }
                    }
                }

                // Subscribe to Valve write-failures so silent PLC drops surface to operator.
                if (hwElements.Item.TryGetValue(DeviceType.Valve, out var valveElements))
                {
                    foreach (var kvp in valveElements)
                    {
                        if (kvp.Value is PlcValve plcValve)
                        {
                            plcValve.WriteFailed += target =>
                                SendLogMesssage(plcValve.Params.ID, $"WRITE_FAILED:{(target ? "OPEN" : "CLOSE")}");
                        }
                    }
                }

                progress?.Invoke("Connecting controllers...", 55);
                if (ConnectControllers())
                    SendLogMesssage("Core", $"Controllers connected");
                else
                    SendLogMesssage("Core", $"Controller connection failed : {LastErrorMessage}");

                progress?.Invoke("Connecting power supplies...", 60);
                if (ConnectPowerSupplies())
                    SendLogMesssage("Core", $"Power supplies connected");
                else
                    SendLogMesssage("Core", $"Power supply connection failed : {LastErrorMessage}");

                progress?.Invoke("Connecting drives...", 65);
                if (ConnectDrives())
                    SendLogMesssage("Core", $"Stepper drives connected");
                else
                    SendLogMesssage("Core", $"Stepper drive connection failed : {LastErrorMessage}");

                progress?.Invoke("Connecting robots...", 68);
                ConnectRobots();

                progress?.Invoke("Connecting sensors...", 70);
                ConnectSensors();

                result = true;
            }
        }
        catch (DllHashVerifier.HashMismatchException hashEx)
        {
            LastErrorMessage = $"\nDLL integrity check failed: {hashEx.Message}";
            SendErrorMesssage(hashEx.Message);
        }
        catch (Exception ex)
        {
            LastErrorMessage = String.Concat("\nXML parsing error: ", ex.Message, LastErrorMessage);
        }

        return result;
    }

    private bool CallLoadWHSection(DeviceType key, XMLHWSection section, Type type)
    {
        MethodInfo? loadWHSectionMethod = typeof(Core).GetMethod(nameof(LoadWHSection),
                                                        BindingFlags.NonPublic | BindingFlags.Instance) ?? 
                                                            throw new InvalidOperationException($"Method {nameof(LoadWHSection)} not found.");

        loadWHSectionMethod = loadWHSectionMethod.MakeGenericMethod([type]);

        object[] args = [hwElements.Item[key], section];

        object? invokeResult = loadWHSectionMethod.Invoke(this, args);
        if (invokeResult is bool loadResult)
            return loadResult;
        else
            return false;
    }

    public bool LoadPLCs(XMLHWSection section)
    {
        bool result = false;

        if (section != null)
        {
            try
            {
                Assembly Dll = HWDlls[section.Lib];
                if (Dll != null)
                {
                    foreach (XElement xml_element in section.Items)
                    {
                        // Isolate per PLC entry: one bad entry must not abandon
                        // the rest of a multi-PLC config, and the REAL exception
                        // text must reach the operator (audit C21).
                        try
                        {
                            var Atr = XMLHardware.Attributes(xml_element);
                            Type? classType = Dll.GetType((string)Atr[1]);
                            if (classType == null)
                            {
                                LastErrorMessage = $"\nPLC '{xml_element.Value}': class '{(string)Atr[1]}' not found in {section.Lib}";
                                SendErrorMesssage(LastErrorMessage);
                                continue;
                            }
                            if (Activator.CreateInstance(classType) is not IPLCController plc)
                            {
                                LastErrorMessage = $"\nPLC '{xml_element.Value}': {classType.FullName} is not an IPLCController";
                                SendErrorMesssage(LastErrorMessage);
                                continue;
                            }
                            plc.Connect((string)Atr[2]);
                            plcs.Add(xml_element.Value, plc);
                            result = true;
                        }
                        catch (Exception ex) when (ex.GetType().Name == "PlcLicenseException") { throw; }
                        catch (Exception ex)
                        {
                            LastErrorMessage = $"\nPLC '{xml_element.Value}' failed to load: {ex.Message}";
                            SendErrorMesssage(LastErrorMessage);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex.GetType().Name == "PlcLicenseException") { throw; }
            catch (Exception ex) { LastErrorMessage = $"\nPLC section failed to load: {ex.Message}"; }
        }
        return result;
    }

    /// <summary>
    /// Per-device connect timeout (ms). A virtual or physical serial port whose
    /// peer has gone can wedge <see cref="System.IO.Ports.SerialPort.Open"/> (or
    /// an initial read) indefinitely. These Connect loops run on the UI thread
    /// inside the FrmMain constructor, BEFORE the message loop starts, so a single
    /// wedged Connect froze the whole app on the splash ("stuck on connecting
    /// serial devices"), unclosable, recoverable only by a reboot. Bounding each
    /// connect guarantees the boot always reaches a usable, closable UI.
    /// </summary>
    private const int ConnectTimeoutMs = 5000;

    /// <summary>
    /// Runs a device's <c>Connect()</c> bounded by <see cref="ConnectTimeoutMs"/>.
    /// Returns the connect result if it completes in time; if it overruns, the
    /// connect is abandoned on its worker thread (a blocking native serial open
    /// cannot be cancelled cooperatively) and <c>false</c> is returned so the boot
    /// proceeds with that device left disconnected. A Connect that throws within
    /// the budget propagates its original exception.
    /// </summary>
    private bool ConnectBounded(string id, Func<bool> connect)
    {
        var task = Task.Run(connect);
        if (task.Wait(ConnectTimeoutMs))
            return task.GetAwaiter().GetResult();   // completed: result or original exception

        SendLogMesssage("Core",
            $"{id} connect timed out after {ConnectTimeoutMs} ms — port likely wedged (peer gone); "
          + "continuing boot without it. Reconnect from the Hardware menu once the port recovers.");
        System.Diagnostics.Trace.TraceWarning(
            $"ConnectBounded: '{id}' connect exceeded {ConnectTimeoutMs} ms and was abandoned to keep the boot alive.");
        return false;
    }

    public bool ConnectControllers()
    {
        bool result = true;
        foreach (var hwElementList in hwElements.Item.Values)
        {
            foreach (var hwElement in hwElementList)
            {
                if (hwElement.Value is BaseController controller)
                {
                    if (ConnectBounded(controller.Params.ID, controller.Connect))
                    {
                        SendLogMesssage("Core", $"Controller {controller.Params.ID} connected");
                    }
                    else
                    {
                        // Continue with the remaining controllers: one failure must
                        // not abandon the rest (audit M-10).
                        LastErrorMessage = $"{controller.Params.ID} {controller.ErrorMessage}";
                        SendLogMesssage("Core", $"Controller {controller.Params.ID} connection failed");
                        result = false;
                    }
                }
            }
        }
        return result;
    }

    public bool ConnectPowerSupplies()
    {
        bool result = true;
        if (hwElements.Item.TryGetValue(DeviceType.Power, out var powerElements))
        {
            foreach (var hwElement in powerElements)
            {
                if (hwElement.Value is BasePowerSupply psu)
                {
                    if (ConnectBounded(psu.Params.ID, psu.Connect))
                    {
                        SendLogMesssage("Core", $"Power supply {psu.Params.ID} connected");
                    }
                    else
                    {
                        SendLogMesssage("Core", $"Power supply {psu.Params.ID} connection failed");
                        result = false;
                    }
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Calls <see cref="BaseSensor.Connect"/> on every sensor instance. The
    /// default override is a no-op for PLC-hosted sensors (they share the
    /// PLC's socket); serial sensors (Modbus-RTU) override it to
    /// open their COM port. Without this loop the serial sensor's port
    /// would never open and <c>ReadHW</c> would return its default 0 °C.
    /// </summary>
    public void ConnectSensors()
    {
        if (!hwElements.Item.TryGetValue(DeviceType.Sensor, out var sensorElements))
            return;

        foreach (var hwElement in sensorElements)
        {
            if (hwElement.Value is BaseSensor sensor)
            {
                if (ConnectBounded(sensor.Params.ID, sensor.Connect))
                    SendLogMesssage("Core", $"Sensor {sensor.Params.ID} connected");
                else
                    SendLogMesssage("Core", $"Sensor {sensor.Params.ID} connection failed");
            }
        }
    }

    public void ConnectRobots()
    {
        if (!hwElements.Item.TryGetValue(DeviceType.Robot, out var robotElements))
            return;

        foreach (var hwElement in robotElements)
        {
            if (hwElement.Value is BaseRobot robot)
            {
                if (ConnectBounded(robot.Params.ID, robot.Connect))
                    SendLogMesssage("Core", $"Robot {robot.Params.ID} connected");
                else
                    SendLogMesssage("Core", $"Robot {robot.Params.ID} connection failed");
            }
        }
    }

    public bool ConnectDrives()
    {
        bool result = true;
        if (hwElements.Item.TryGetValue(DeviceType.Drive, out var driveElements))
        {
            foreach (var hwElement in driveElements)
            {
                if (hwElement.Value is BaseStepperDrive drive)
                {
                    drive.CalibrationChanged += SaveDriveCalibration;

                    if (ConnectBounded(drive.Params.ID, drive.Connect))
                    {
                        SendLogMesssage("Core", $"Stepper drive {drive.Params.ID} connected");
                    }
                    else
                    {
                        SendLogMesssage("Core", $"Stepper drive {drive.Params.ID} connection failed");
                        result = false;
                    }
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Persists a drive's offset / reverse-offset back to HWConf.xml so the
    /// operator-tuned calibration survives application restarts. Invoked
    /// from <see cref="BaseStepperDrive.CalibrationChanged"/>; runs on
    /// whichever thread triggered the property change (UI or CoreThread —
    /// XDocument.Save tolerates either, and edits are infrequent).
    /// </summary>
    private void SaveDriveCalibration(BaseStepperDrive drive)
    {
        if (string.IsNullOrEmpty(_hwConfPath)) return;
        try
        {
            var doc = XDocument.Load(_hwConfPath);
            var driveEl = doc.Descendants("drive")
                .FirstOrDefault(e => string.Equals(
                    e.Value.Trim(), drive.Params.ID, StringComparison.Ordinal));
            if (driveEl is null)
            {
                SendLogMesssage("Core",
                    $"Calibration not saved — <drive>{drive.Params.ID}</drive> not found in HWConf.xml");
                return;
            }

            driveEl.SetAttributeValue("offset", drive.Offset);
            driveEl.SetAttributeValue("reverseOffset", drive.ReverseOffset);
            doc.Save(_hwConfPath);
            SendLogMesssage("Core",
                $"Saved {drive.Params.ID} calibration: offset={drive.Offset}, reverseOffset={drive.ReverseOffset}");
        }
        catch (Exception ex)
        {
            SendLogMesssage("Core",
                $"Failed to persist {drive.Params.ID} calibration: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies a stage's optional extended-config attributes (presets, soft
    /// limits, jog step, home, speed) at load and subscribes persistence so
    /// operator edits from the stage popup survive restart.
    /// </summary>
    private void ApplyStageConfig(IBaseComponent? component, XElement element)
    {
        if (component is not BaseStage stage) return;
        stage.ApplyConfig(element);
        stage.ConfigChanged += SaveStageConfig;
    }

    /// <summary>
    /// Persists a stage's presets / soft limits / jog step / home / speed back
    /// to HWConf.xml. Invoked from <see cref="BaseStage.ConfigChanged"/>.
    /// InvariantCulture formatting keeps '.' as the decimal point so the values
    /// round-trip on comma-decimal locales. Soft limits stored only when set
    /// (finite); NaN means "unbounded" and no attribute is written.
    /// </summary>
    private void SaveStageConfig(BaseStage stage)
    {
        if (string.IsNullOrEmpty(_hwConfPath)) return;
        try
        {
            var doc = XDocument.Load(_hwConfPath);
            var el = doc.Descendants("stage")
                .FirstOrDefault(e => string.Equals(
                    e.Value.Trim(), stage.Params.ID, StringComparison.Ordinal));
            if (el is null)
            {
                SendLogMesssage("Core",
                    $"Stage config not saved — <stage>{stage.Params.ID}</stage> not found in HWConf.xml");
                return;
            }

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            el.SetAttributeValue("presets",
                string.IsNullOrEmpty(stage.SerializePresets()) ? null : stage.SerializePresets());
            el.SetAttributeValue("homePos", stage.HomePosition.ToString(inv));
            el.SetAttributeValue("jogStep", stage.JogStep.ToString(inv));
            el.SetAttributeValue("softMin",
                float.IsNaN(stage.SoftMin) ? null : stage.SoftMin.ToString(inv));
            el.SetAttributeValue("softMax",
                float.IsNaN(stage.SoftMax) ? null : stage.SoftMax.ToString(inv));
            el.SetAttributeValue("speed",
                stage.Speed == 0f ? null : stage.Speed.ToString(inv));
            doc.Save(_hwConfPath);
            SendLogMesssage("Core", $"Saved {stage.Params.ID} stage settings");
        }
        catch (Exception ex)
        {
            SendLogMesssage("Core",
                $"Failed to persist {stage.Params.ID} stage settings: {ex.Message}");
        }
    }

    private bool LoadWHSection<T>(Elements element_list, XMLHWSection section) where T : class
    {
        bool result = false;
        object[] Atr;
        IPLCController? Plc = null;

        if (section != null)
        {
            try
            {
                Assembly Dll = HWDlls[section.Lib];
                string hwType = section.HwType;
                if (section.Host != null)
                    Plc = (IPLCController?)plcs[section.Host];

                foreach (XElement xml_element in section.Items)
                {
                    try
                    {
                        Atr = XMLHardware.Attributes(xml_element);
                        if (Plc != null)
                        {
                            var hw_element = CreateHWItem<T>(Dll, Plc, hwType, Atr);
                            if (hw_element != null)
                            {
                                (hw_element as IBaseComponent)!.Connected = Plc.Connected;
                                ApplySensorChamber(hw_element as IBaseComponent, xml_element);
                                ApplyMfcGateValves(hw_element as IBaseComponent, xml_element);
                                ApplyRobotFaces(hw_element as IBaseComponent, xml_element);
                                ApplyStageConfig(hw_element as IBaseComponent, xml_element);
                                element_list.Add(xml_element.Value, hw_element as IBaseComponent);
                            }
                        }
                        else
                        {
                            var hw_element = CreateHWItem<T>(Dll, hwType, Atr);
                            if (hw_element != null)
                            {
                                ApplySensorChamber(hw_element as IBaseComponent, xml_element);
                                ApplyMfcGateValves(hw_element as IBaseComponent, xml_element);
                                ApplyRobotFaces(hw_element as IBaseComponent, xml_element);
                                ApplyStageConfig(hw_element as IBaseComponent, xml_element);
                                element_list.Add(xml_element.Value, hw_element as IBaseComponent);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Name the offending element (and, via the inner message,
                        // the offending attribute — see XMLHardware.Attributes).
                        // Rethrow so the section still fails CLOSED — safety
                        // hardware must not silently boot missing a device.
                        throw new InvalidOperationException(
                            $"Hardware element '{xml_element.Value}' in section " +
                            $"'{section.HwType}' failed to load: {ex.Message}", ex);
                    }
                }
                result = true;
            }
            catch (Exception ex)
            {
                LastErrorMessage = ex.Message;
                SendErrorMesssage(ex.Message);
                System.Diagnostics.Trace.TraceError(
                    $"LoadWHSection<{typeof(T).Name}> failed: {ex}");
            }
        }
        return result;
    }

    private static T? CreateHWItem<T>(Assembly Dll, IPLCController plc, string dllTypeName, object[] Args) where T : class
    {
        object[] fullArgs = [plc, .. Args];
        Type? classType = Dll.GetType($"{dllTypeName}");
        if (classType == null)
            return null;
        else
            return Activator.CreateInstance(type: classType, args: fullArgs) as T;
    }

    private static T? CreateHWItem<T>(Assembly Dll, string dllTypeName, object[] fullArgs) where T : class
    {
        Type? classType = Dll.GetType($"{dllTypeName}");
        if (classType == null)
            return null;
        else
            return Activator.CreateInstance(type: classType, args: fullArgs) as T;
    }

    /// <summary>
    /// Applies the optional <c>chamber="…"</c> attribute of a hardware XML element
    /// to sensors, valves, MFCs, pumps and robots; when absent the component keeps
    /// <see cref="CoreConstants.DefaultChamberId"/> ("Main").
    ///
    /// Valves and MFCs carry a chamber so per-chamber group-transition
    /// evaluation (idle↔processing on gas flow) considers only a chamber's own
    /// gas hardware.
    /// </summary>
    internal static void ApplySensorChamber(IBaseComponent? component, XElement element)
    {
        string chamber = element.Attribute("chamber")?.Value ?? CoreConstants.DefaultChamberId;
        switch (component)
        {
            case BaseSensor sensor: sensor.Chamber = chamber; break;
            case BaseValve valve:   valve.Chamber  = chamber; break;
            case BaseMFC mfc:       mfc.Chamber    = chamber; break;
            case BasePump pump:     pump.Chamber   = chamber; break;
            case BaseRobot robot:   robot.Chamber  = chamber; break;
            case BaseController controller: controller.Chamber = chamber; break;
        }
    }

    internal static void ApplyMfcGateValves(IBaseComponent? component, XElement element)
    {
        if (component is not BaseMFC mfc) return;
        mfc.GateValves = (element.Attribute("valve")?.Value ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Reads <c>&lt;face name="…" idx="…"/&gt;</c> child elements of a
    /// <c>&lt;robot&gt;</c> XML element and populates
    /// <see cref="BaseRobot.Faces"/> so the recipe <c>transfer</c> op can
    /// resolve human-readable face names (e.g. "LL", "C1") to wire indices.
    /// Park (index 0) is implicit and does not need a <c>&lt;face&gt;</c> entry.
    /// Unknown or malformed entries are silently skipped.
    /// </summary>
    internal static void ApplyRobotFaces(IBaseComponent? component, XElement element)
    {
        if (component is not BaseRobot robot) return;
        var dict     = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var slitMap  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var angleMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var faceEl in element.Elements("face"))
        {
            string? name   = faceEl.Attribute("name")?.Value;
            string? idxStr = faceEl.Attribute("idx")?.Value;
            if (name == null || idxStr == null) continue;
            if (!byte.TryParse(idxStr, out byte idx)) continue;
            dict[name] = idx;

            string? slit = faceEl.Attribute("slit")?.Value;
            if (!string.IsNullOrEmpty(slit))
                slitMap[name] = slit;

            string? angleStr = faceEl.Attribute("angle")?.Value;
            if (int.TryParse(angleStr, out int angle))
                angleMap[name] = angle;
        }
        robot.Faces        = dict;
        robot.FaceSlitMap  = slitMap;
        robot.FaceAngleMap = angleMap;
    }

}
