using Xunit;

// CoreLogicTests is a UseWindowsForms assembly: several test classes instantiate
// WinForms controls/dialogs (FloatingPanel, the PSU settings dialogs, process
// panels) and several touch the process-wide static EventLogBus. Neither the
// WinForms subsystem (global GDI+/Application/handle state) nor a static event
// bus is safe to access from two tests at once; xUnit's default per-class
// parallelism produced intermittent failures that passed in isolation. The
// suite is small, so serializing every test costs little wall-clock time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
