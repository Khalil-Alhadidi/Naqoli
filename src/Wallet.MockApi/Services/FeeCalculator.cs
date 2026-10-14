using Microsoft.Extensions.Options;
using Wallet.MockApi.Data.Entities;

namespace Wallet.MockApi.Services;

/// <summary>The fee for a payment: a flat amount per transaction type, from <see cref="WalletOptions"/>.</summary>
public sealed class FeeCalculator(IOptions<WalletOptions> options)
{
    private readonly WalletOptions settings = options.Value;

    /// <summary>The fee in dinars. <paramref name="amountIqd"/> is part of the contract so a fee scale can follow it later.</summary>
    public long Fee(TransactionType type, long amountIqd) => type switch
    {
        TransactionType.Transfer => settings.TransferFeeIqd,
        TransactionType.Bill => settings.BillFeeIqd,
        TransactionType.Purchase => settings.PurchaseFeeIqd,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown transaction type."),
    };
}
