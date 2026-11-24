using Erp.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Data.Repositories;

// T6 contracts tables — commitment-only (R-T6f). Amendments/price lines/obligations are
// insert-only (037 AC1/AC2 executed immutability); status flips go through the Set helper.
public sealed class ContractsRepository(ErpDbContext db, EntityContext ec) : IContractsRepository
{
    private IQueryable<Contract> ContractsScoped() =>
        db.Contracts.Where(c => c.TenantId == ec.TenantId && c.LegalEntityId == ec.EntityId);
    private IQueryable<ContractAmendment> AmendmentsScoped() =>
        db.ContractAmendments.Where(a => a.TenantId == ec.TenantId && a.LegalEntityId == ec.EntityId);
    private IQueryable<ContractPriceLine> PriceLinesScoped() =>
        db.ContractPriceLines.Where(p => p.TenantId == ec.TenantId && p.LegalEntityId == ec.EntityId);
    private IQueryable<ContractObligation> ObligationsScoped() =>
        db.ContractObligations.Where(o => o.TenantId == ec.TenantId && o.LegalEntityId == ec.EntityId);

    public List<Contract> AllContracts(ContractStatus? status) =>
        (status is null ? ContractsScoped() : ContractsScoped().Where(c => c.Status == status))
            .OrderBy(c => c.Id).ToList();

    public Contract? FindContract(string contractId) => ContractsScoped().FirstOrDefault(c => c.Id == contractId);

    public void AddContract(Contract contract) => db.Contracts.Add(contract);

    public void SetContractStatus(Contract contract, ContractStatus status, string? activatedBy, DateTimeOffset? activatedAt)
    {
        Set(contract, nameof(Contract.Status), status);
        Set(contract, nameof(Contract.ActivatedBy), activatedBy);
        Set(contract, nameof(Contract.ActivatedAt), activatedAt);
    }

    public void SetContractVersion(Contract contract, int version) =>
        Set(contract, nameof(Contract.Version), version);

    public void SetContractRenewed(Contract contract, int optionsExercised, DateOnly effectiveTo)
    {
        Set(contract, nameof(Contract.OptionsExercised), optionsExercised);
        Set(contract, nameof(Contract.EffectiveTo), effectiveTo);
    }

    public List<ContractAmendment> AmendmentsForContract(string contractId) =>
        AmendmentsScoped().Where(a => a.ContractId == contractId).OrderBy(a => a.Version).ToList();

    public void AddAmendment(ContractAmendment amendment) => db.ContractAmendments.Add(amendment);

    public List<ContractPriceLine> PriceLinesForContract(string contractId) =>
        PriceLinesScoped().Where(p => p.ContractId == contractId).OrderBy(p => p.Id).ToList();

    public void AddPriceLine(ContractPriceLine line) => db.ContractPriceLines.Add(line);

    public List<ContractObligation> ObligationsForContract(string contractId) =>
        ObligationsScoped().Where(o => o.ContractId == contractId).OrderBy(o => o.Id).ToList();

    public ContractObligation? FindObligation(string contractId, string obligationId) =>
        ObligationsScoped().FirstOrDefault(o => o.ContractId == contractId && o.Id == obligationId);

    public void AddObligation(ContractObligation obligation) => db.ContractObligations.Add(obligation);

    public void SetObligationEvidence(ContractObligation obligation, string evidenceRef)
    {
        Set(obligation, nameof(ContractObligation.Status), ObligationStatus.EvidenceSubmitted);
        Set(obligation, nameof(ContractObligation.EvidenceRef), evidenceRef);
    }

    public void SetObligationVerified(ContractObligation obligation, string verifiedBy, DateTimeOffset verifiedAt)
    {
        Set(obligation, nameof(ContractObligation.Status), ObligationStatus.Verified);
        Set(obligation, nameof(ContractObligation.VerifiedBy), verifiedBy);
        Set(obligation, nameof(ContractObligation.VerifiedAt), verifiedAt);
    }

    public void SetObligationWaived(ContractObligation obligation, string waiverReason)
    {
        Set(obligation, nameof(ContractObligation.Status), ObligationStatus.Waived);
        Set(obligation, nameof(ContractObligation.WaiverReason), waiverReason);
    }

    public int Save() => db.SaveChanges();

    private void Set<T>(object entity, string name, T value) => db.Entry(entity).Property(name).CurrentValue = value;
}
