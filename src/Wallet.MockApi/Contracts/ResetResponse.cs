namespace Wallet.MockApi.Contracts;

/// <summary>The row counts of each table after <c>POST /admin/reset</c>, read back from the database.</summary>
public sealed record ResetResponse(
    int Users,
    int Accounts,
    int Contacts,
    int Aliases,
    int BillAccounts,
    int Products,
    int Transactions,
    int Orders);
