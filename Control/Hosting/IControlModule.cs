namespace APS.SystemControl.Hosting;

internal interface IControlModule
{
    void OnCoreReady(FrmMain form) { }

    void OnLoad(FrmMain form) { }

    void OnBootComplete(FrmMain form) { }

    void OnRunStateChanged(FrmMain form, bool running) { }

    void OnClosing(FrmMain form) { }

    void OnShutdown(FrmMain form) { }

    void OnSourcesReloaded(FrmMain form) { }
}
