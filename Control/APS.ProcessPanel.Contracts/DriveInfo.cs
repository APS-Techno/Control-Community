namespace APS.ProcessPanel.Contracts;

public record DriveInfo(
    string Id,
    float Position,
    bool IsMoving,
    bool IsHomed,
    int FlangeNumber,
    // Applied Motion user-register status code — 22=moving, 51=arrived-to-pos,
    // 52=leaving-pos, 99=complete, 0=idle. Lets the UI distinguish "actually
    // moving" from "arrived and dwelling" while IsMoving still reports
    // queue-active so recipe-side waits work correctly.
    int UserRegister = 0
);
