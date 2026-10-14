namespace Wallet.MockApi.Data.Entities;

/// <summary>A company that issues bills, such as electricity or water.</summary>
public class Biller
{
    public int Id { get; set; }

    /// <summary>A stable code: <c>electricity</c>, <c>water</c> or <c>internet</c>.</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }
}
