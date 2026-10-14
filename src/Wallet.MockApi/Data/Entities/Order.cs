namespace Wallet.MockApi.Data.Entities;

/// <summary>A completed store purchase and the code it returned (D11).</summary>
public class Order
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    public int TransactionId { get; set; }

    public Transaction? Transaction { get; set; }

    /// <summary>The purchase code. It never reaches the model, and logs keep only the last digits.</summary>
    public required string Code { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
