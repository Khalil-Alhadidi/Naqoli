namespace Wallet.MockApi.Data.Entities;

/// <summary>A store catalogue item: operator phone credit or an internet card (D11).</summary>
public class Product
{
    public int Id { get; set; }

    /// <summary>The seed id (<c>ZN-CR-5</c>).</summary>
    public required string Code { get; set; }

    /// <summary>The lower-case provider code: <c>zain</c>, <c>asiacell</c> or <c>korek</c>.</summary>
    public required string Provider { get; set; }

    public required string ProviderNameEn { get; set; }

    public required string ProviderNameAr { get; set; }

    /// <summary>Another name people use for the product, if any.</summary>
    public string? AlsoCalled { get; set; }

    public ProductType Type { get; set; }

    /// <summary>The value of the credit or card in dinars.</summary>
    public long FaceValueIqd { get; set; }

    /// <summary>The price in dinars. The price always comes from the catalogue.</summary>
    public long PriceIqd { get; set; }
}

public enum ProductType
{
    Credit,
    Internet,
}
