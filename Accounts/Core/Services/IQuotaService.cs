namespace APS.Accounts.Services;

public interface IQuotaService
{
    QuotaState GetQuotaState(Guid userId);
    Guid RecordUsage(Guid userId, long deltaSeconds, DateTime fromUtc, DateTime toUtc);
    ReconcileResult Reconcile(Guid userId);
}
