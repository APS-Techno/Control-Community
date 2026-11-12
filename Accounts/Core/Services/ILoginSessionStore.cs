namespace APS.Accounts.Services;

/// <summary>A no-op for offline sessions whose SessionId has no DB row.</summary>
public interface ILoginSessionStore
{
    void RecordEnd(Guid sessionId, DateTime endedUtc, string reason);
}
