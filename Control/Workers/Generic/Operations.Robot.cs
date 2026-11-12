using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.Workers.Generic;

public static partial class Operations
{
    internal static int TransferTimeoutMs = 60_000;
    internal static int HomeTimeoutMs = 30_000;
    internal static int AbortTimeoutMs = 10_000;
    internal static int RobotPollMs = 100;

    /// <summary>
    /// Moves a wafer between two named faces and waits until the robot is idle again.
    /// The destination becomes the chamber that source-addressed <c>Deposit</c> looks in.
    /// Unknown faces, a robot that is not homed or not idle, a device error and a timeout
    /// all fail the step (null).
    /// </summary>
    public static async Task<object?> Transfer(BaseRobot robot, string srcName, string dstName, CancellationToken ct)
    {
        const string op = "Transfer";
        if (!robot.Connected)
            return Fail(op, $"robot {robot.Params.ID} is not connected");
        if (!TryResolveFace(robot, srcName, out byte src))
            return Fail(op, $"unknown face name '{srcName}'");
        if (!TryResolveFace(robot, dstName, out byte dst))
            return Fail(op, $"unknown face name '{dstName}'");

        robot.Poll();
        if (!robot.Homed)
            return Fail(op, "robot is not homed");
        if (robot.State != RobotState.Idle)
            return Fail(op, $"robot is not idle (state={robot.State})");

        byte issued;
        try
        {
            robot.Transfer(src, dst);
            issued = robot.LastIssuedCmdId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(op, $"robot {robot.Params.ID} rejected the transfer: {ex.Message}");
        }

        int elapsed = 0;
        while (elapsed < TransferTimeoutMs)
        {
            await Task.Delay(RobotPollMs, ct).ConfigureAwait(false);
            elapsed += RobotPollMs;
            robot.Poll();
            // Only a status that echoes THIS command's id counts: a poll that lands
            // before the device latched the command would otherwise read as completion.
            if (robot.LastCmdId == issued)
            {
                if (robot.State == RobotState.Error || robot.ErrorCode != RobotErrorCode.Ok)
                    return Fail(op, $"transfer failed: {robot.ErrorCode}");
                if (robot.State == RobotState.Idle)
                {
                    _currentChamber = dstName;
                    return true;
                }
            }
            else if (robot.State == RobotState.Error)
                return Fail(op, $"transfer failed: {robot.ErrorCode}");
        }

        TryAbort(robot);
        return Fail(op, $"transfer timed out after {TransferTimeoutMs / 1000}s");
    }

    /// <summary>Homes the robot and waits for Idle with Homed set.</summary>
    public static async Task<object?> Home(BaseRobot robot, CancellationToken ct)
    {
        const string op = "Home";
        if (!robot.Connected)
            return Fail(op, $"robot {robot.Params.ID} is not connected");

        byte issued;
        try
        {
            robot.Home();
            issued = robot.LastIssuedCmdId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(op, $"robot {robot.Params.ID} rejected the home command: {ex.Message}");
        }

        int elapsed = 0;
        while (elapsed < HomeTimeoutMs)
        {
            await Task.Delay(RobotPollMs, ct).ConfigureAwait(false);
            elapsed += RobotPollMs;
            robot.Poll();
            // No un-echoed Error check: homing is the recovery path out of Error, so a
            // poll that still shows the old Error before the command latched is not a failure.
            if (robot.LastCmdId == issued)
            {
                if (robot.State == RobotState.Error || robot.ErrorCode != RobotErrorCode.Ok)
                    return Fail(op, $"home failed: {robot.ErrorCode}");
                if (robot.State == RobotState.Idle && robot.Homed)
                    return true;
            }
        }
        return Fail(op, $"home timed out after {HomeTimeoutMs / 1000}s");
    }

    /// <summary>Best-effort robot abort: succeeds even if Idle is not confirmed in time.</summary>
    public static async Task<object?> Abort(BaseRobot robot, CancellationToken ct)
    {
        const string op = "Abort";
        if (!robot.Connected)
            return Fail(op, $"robot {robot.Params.ID} is not connected");

        try
        {
            robot.Abort();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(op, $"robot {robot.Params.ID} rejected the abort command: {ex.Message}");
        }

        int elapsed = 0;
        while (elapsed < AbortTimeoutMs)
        {
            await Task.Delay(RobotPollMs, ct).ConfigureAwait(false);
            elapsed += RobotPollMs;
            robot.Poll();
            if (robot.State == RobotState.Idle)
                break;
        }
        return true;
    }

    private static void TryAbort(BaseRobot robot)
    {
        try { robot.Abort(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Fail("Transfer", $"robot {robot.Params.ID} rejected the abort after the timeout: {ex.Message}");
        }
    }

    private static bool TryResolveFace(BaseRobot robot, string name, out byte face)
    {
        if (string.Equals(name, "park", StringComparison.OrdinalIgnoreCase))
        {
            face = RobotWireContract.ParkFace;
            return true;
        }
        return robot.Faces.TryGetValue(name, out face);
    }
}
