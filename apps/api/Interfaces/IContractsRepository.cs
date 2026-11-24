namespace Erp.Api.Interfaces;

// T6 contracts aggregate — commitment-only contracts (R-T6f), insert-only amendments (037 AC1/AC2),
// price schedules, obligations. Tenant+entity scoped on every lookup.
public interface IContractsRepository
{
    List<Contract> AllContracts(ContractStatus? status);
    Contract? FindContract(string contractId);
    void AddContract(Contract contract);
    void SetContractStatus(Contract contract, ContractStatus status, string? activatedBy, DateTimeOffset? activatedAt);
    void SetContractVersion(Contract contract, int version);
    void SetContractRenewed(Contract contract, int optionsExercised, DateOnly effectiveTo);

    List<ContractAmendment> AmendmentsForContract(string contractId);
    void AddAmendment(ContractAmendment amendment);

    List<ContractPriceLine> PriceLinesForContract(string contractId);
    void AddPriceLine(ContractPriceLine line);

    List<ContractObligation> ObligationsForContract(string contractId);
    ContractObligation? FindObligation(string contractId, string obligationId);
    void AddObligation(ContractObligation obligation);
    void SetObligationEvidence(ContractObligation obligation, string evidenceRef);
    void SetObligationVerified(ContractObligation obligation, string verifiedBy, DateTimeOffset verifiedAt);
    void SetObligationWaived(ContractObligation obligation, string waiverReason);

    int Save();
}
