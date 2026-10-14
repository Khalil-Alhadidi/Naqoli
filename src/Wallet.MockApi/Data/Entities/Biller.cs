namespace Wallet.MockApi.Data.Entities;

/// <summary>A company that issues bills, such as electricity or water.</summary>
public class Biller
{
    public int Id { get; set; }

    /// <summary>A stable code, such as <c>electricity</c> or <c>school_fees</c>.</summary>
    public required string Code { get; set; }

    /// <summary>The Arabic name.</summary>
    public required string Name { get; set; }

    /// <summary>The English name.</summary>
    public required string NameEn { get; set; }
}
