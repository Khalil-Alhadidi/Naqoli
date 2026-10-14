namespace Wallet.MockApi.Data.Entities;

/// <summary>A store catalogue item: operator phone credit or an internet card (D11).</summary>
public class Product
{
    public int Id { get; set; }

    public required string Provider { get; set; }

    public ProductType Type { get; set; }

    public required string Name { get; set; }

    /// <summary>The credit value in dinars; zero for internet cards.</summary>
    public long FaceValueIqd { get; set; }

    /// <summary>The price in dinars. The price always comes from the catalogue.</summary>
    public long PriceIqd { get; set; }
}

public enum ProductType
{
    Credit,
    Internet,
}
