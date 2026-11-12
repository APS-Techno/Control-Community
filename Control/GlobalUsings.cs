// Canonical DeviceType lives in VacuumControls (APS.Controls.Core) because it
// is shared by Control, Builder, and Emulator; the alias keeps one type identity.
global using DeviceType = APS.Controls.Core.DeviceType;
