namespace APS.SystemControl.Plugin;

public interface IMenuContext
{
    IHwCommandSink Hw { get; }
    IHardwareRegistry Hardware { get; }
    IUiServices Ui { get; }
    IFlagService Flags { get; }

    /// <summary>Live machine state (recipe running), used to gate motion items.</summary>
    IRuntimeState Runtime { get; }

    IAuthorization Auth { get; }

    Form Owner { get; }
}
