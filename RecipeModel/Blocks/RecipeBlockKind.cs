namespace APS.RecipeModel.Blocks;

public enum RecipeBlockKind
{
    Valve, Mfc, Pump, Stage, Shutter, Holder, Scan, Message, Transfer, Home, Abort,
    Bias, BiasArb, Power, Ramp, Set,
    Warmup, Deposit, Rotation, Rotate,
    Delay, Wait, Pressure, Swait, Check,
    Loop, IfElse, BeginEnd, Label, Goto, Function, State,
    Call, Var,
}
