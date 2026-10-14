using Microsoft.Extensions.Options;
using Wallet.MockApi;
using Wallet.MockApi.Data.Entities;
using Wallet.MockApi.Services;

namespace Agent.Core.Tests.Wallet;

public class FeeCalculatorTests
{
    private static readonly FeeCalculator Calculator = new(Options.Create(new WalletOptions
    {
        TransferFeeIqd = 1000,
        BillFeeIqd = 500,
        PurchaseFeeIqd = 0,
    }));

    [Fact]
    public void TransferFeeComesFromOptions() => Assert.Equal(1000, Calculator.Fee(TransactionType.Transfer, 50_000));

    [Fact]
    public void BillFeeComesFromOptions() => Assert.Equal(500, Calculator.Fee(TransactionType.Bill, 45_000));

    [Fact]
    public void PurchaseFeeComesFromOptions() => Assert.Equal(0, Calculator.Fee(TransactionType.Purchase, 5_000));

    [Fact]
    public void FeeFollowsConfiguredValues()
    {
        var custom = new FeeCalculator(Options.Create(new WalletOptions { TransferFeeIqd = 250, BillFeeIqd = 0, PurchaseFeeIqd = 75 }));

        Assert.Equal(250, custom.Fee(TransactionType.Transfer, 1));
        Assert.Equal(0, custom.Fee(TransactionType.Bill, 1));
        Assert.Equal(75, custom.Fee(TransactionType.Purchase, 1));
    }
}
