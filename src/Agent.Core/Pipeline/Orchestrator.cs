using System.Globalization;
using Agent.Core.Audit;
using Agent.Core.Pending;
using Agent.Core.Requests;
using Agent.Core.Resolver;
using Agent.Core.Text;
using Agent.Core.Wallet;
using Microsoft.Extensions.Options;

namespace Agent.Core.Pipeline;

/// <summary>
/// Turns one validated <see cref="ParsedRequest"/> into one reply: a question, a refusal, the balance, the consent
/// screen, or a confirmation card (architecture sections 3 and 4). It composes the wallet's reads, the resolvers, the
/// amount rule and the ordered checks, and opens the pending record of a card. It never executes: only the Confirm
/// handler, given the card's pending ID, does (rule 1). Nothing it reads from the wallet goes back into the request
/// (rule 5). Every question and refusal writes one audit row.
/// </summary>
public sealed class Orchestrator
{
    private const string TransferType = "transfer";
    private const string BillType = "bill";
    private const string PurchaseType = "purchase";

    /// <summary>The wallet's code for a phone with no account (<c>GET /accounts/by-phone</c>).</summary>
    private const string UnknownAccountCode = "unknown_account";

    private readonly WalletClient wallet;
    private readonly Checks checks;
    private readonly CardBuilder cards;
    private readonly PendingStore store;
    private readonly AuditLog audit;
    private readonly AgentOptions options;
    private readonly TimeProvider clock;

    public Orchestrator(
        WalletClient wallet,
        Checks checks,
        CardBuilder cards,
        PendingStore store,
        AuditLog audit,
        IOptions<AgentOptions> options,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        this.wallet = wallet;
        this.checks = checks;
        this.cards = cards;
        this.store = store;
        this.audit = audit;
        this.options = options.Value;
        this.clock = clock;
    }

    /// <summary>
    /// Answers <paramref name="request"/> for <paramref name="userId"/>. A wallet read with no answer is a refusal
    /// <see cref="ReasonCodes.NotDoneNothingTaken"/> with detail <c>wallet_</c> and the reason; it never throws for one.
    /// </summary>
    public async Task<AgentReply> HandleAsync(int userId, ParsedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. The balance question (D22): read only, after consent, and the figure never reaches the audit row.
        if (request.Intent == RequestIntent.Balance)
        {
            var reader = await wallet.GetUserAsync(userId, cancellationToken);
            if (reader is not WalletResult<WalletUser>.Ok { Value: var balanceUser })
                return await RefuseUnreadAsync(userId, reader, cancellationToken);
            if (!balanceUser.ConsentGiven)
                return await ConsentAsync(userId, cancellationToken);

            var balance = await wallet.GetBalanceAsync(userId, cancellationToken);
            if (balance is not WalletResult<WalletBalance>.Ok { Value: var shown })
                return await RefuseUnreadAsync(userId, balance, cancellationToken);

            await audit.WriteAsync(userId, null, AuditEvents.BalanceShown, null, null, null, null, null, cancellationToken);
            return new AgentReply.BalanceReply(shown.BalanceIqd);
        }

        // 2. Requests the agent does not do or could not read.
        if (request.Intent == RequestIntent.Unsupported)
        {
            var reason = request.UnsupportedReason ?? ReasonCodes.NotSupported;
            await audit.WriteAsync(userId, null, AuditEvents.Refused, null, null, reason, null, null, cancellationToken);
            return new AgentReply.Unsupported(reason);
        }

        if (request.Intent == RequestIntent.Unclear)
            return await RefuseAsync(userId, ReasonCodes.NotUnderstood, null, null, cancellationToken);

        if (request.CurrencyText is not null)
            return await RefuseAsync(userId, ReasonCodes.DinarsOnly, null, null, cancellationToken);

        // 3. The consent gate.
        var userRead = await wallet.GetUserAsync(userId, cancellationToken);
        if (userRead is not WalletResult<WalletUser>.Ok { Value: var user })
            return await RefuseUnreadAsync(userId, userRead, cancellationToken);
        if (!user.ConsentGiven)
            return await ConsentAsync(userId, cancellationToken);

        return request.Intent switch
        {
            RequestIntent.Transfer => await TransferAsync(user, request, cancellationToken),
            RequestIntent.Bill => await BillAsync(userId, request, cancellationToken),
            RequestIntent.Purchase => await PurchaseAsync(userId, request, cancellationToken),
            _ => await RefuseAsync(userId, ReasonCodes.NotUnderstood, null, null, cancellationToken),
        };
    }

    /// <summary>Step 4: the recipient first (rule 16), then the amount, then the checks and the card.</summary>
    private async Task<AgentReply> TransferAsync(WalletUser user, ParsedRequest request, CancellationToken cancellationToken)
    {
        var userId = user.Id;
        if (string.IsNullOrWhiteSpace(request.RecipientText))
            return await AskAsync(userId, QuestionKinds.WhichContact, [], cancellationToken);

        CardSubject subject;
        string? accountStatus;
        if (PhoneNumbers.LooksLikeNumber(request.RecipientText))
        {
            if (!PhoneNumbers.TryNormalize(request.RecipientText, out var phone))
                return await RefuseAsync(userId, ReasonCodes.InvalidNumber, null, null, cancellationToken);

            // The user's own number is refused before the wallet is asked about it.
            if (string.Equals(phone, user.Phone, StringComparison.Ordinal))
                return await RefuseAsync(userId, ReasonCodes.NotSupported, ReasonCodes.OwnNumberDetail, null, cancellationToken);

            switch (await wallet.GetAccountByPhoneAsync(phone, cancellationToken))
            {
                case WalletResult<WalletAccount>.Ok { Value: var account }:
                    subject = new CardSubject(TransferType, account.Phone, account.AccountId, phone, account.RegisteredName, null, null, null, null);
                    accountStatus = account.Status;
                    break;
                case WalletResult<WalletAccount>.Refused { Problem: { Status: 404 } or { Code: UnknownAccountCode } }:
                    return await RefuseAsync(userId, ReasonCodes.NumberHasNoWallet, null, null, cancellationToken);
                case var unread:
                    return await RefuseUnreadAsync(userId, unread, cancellationToken);
            }
        }
        else
        {
            var contactsRead = await wallet.GetContactsAsync(userId, cancellationToken);
            if (contactsRead is not WalletResult<WalletContacts>.Ok { Value: var contacts })
                return await RefuseUnreadAsync(userId, contactsRead, cancellationToken);

            switch (ContactResolver.Resolve(request.RecipientText, ContactCandidate.From(contacts)))
            {
                case Resolution.Card { Chosen: var chosen }:
                    subject = new CardSubject(
                        TransferType, chosen.Phone, chosen.AccountId, chosen.Text, chosen.RegisteredName, null, null, null, null);
                    accountStatus = chosen.AccountStatus;
                    break;
                case Resolution.Options choice:
                    var contactOptions = choice.Candidates
                        .Select(c => new Option(
                            c.ContactCode ?? c.AccountId.ToString(CultureInfo.InvariantCulture),
                            c.Text,
                            c.RegisteredName,
                            c.LastSentAt,
                            c.LastSentAmountIqd,
                            null))
                        .ToList();
                    return await AskAsync(userId, QuestionKinds.Of(choice.Kind), contactOptions, cancellationToken);
                case Resolution.NotFound:
                    return await RefuseAsync(userId, ReasonCodes.RecipientNotFound, request.RecipientText, null, cancellationToken);
                default:
                    throw new InvalidOperationException("Unknown resolution.");
            }
        }

        long amount;
        switch (AmountRule.Resolve(request, options))
        {
            case AmountResolution.Dinars dinars:
                amount = dinars.AmountIqd;
                break;
            case AmountResolution.Missing:
                return await AskAsync(userId, QuestionKinds.WhichAmount, [], cancellationToken);
            case AmountResolution.Ask ask:
                var amountOptions = ask.Options
                    .Select(a => new Option(a.ToString(CultureInfo.InvariantCulture), null, null, null, null, a))
                    .ToList();
                return await AskAsync(userId, QuestionKinds.WhichAmount, amountOptions, cancellationToken);
            case AmountResolution.Invalid invalid:
                return await RefuseAsync(userId, invalid.Reason, null, null, cancellationToken);
            default:
                throw new InvalidOperationException("Unknown amount resolution.");
        }

        var input = new CheckInput(
            userId, TransferType, amount, subject.ToPhone, subject.ToAccountId, accountStatus, null, null, SkipFormat: false);
        return await CheckAndIssueAsync(userId, input, subject, cancellationToken);
    }

    /// <summary>Step 5: the bill from the biller, paid in full (D9), then the checks and the card.</summary>
    private async Task<AgentReply> BillAsync(int userId, ParsedRequest request, CancellationToken cancellationToken)
    {
        var billsRead = await wallet.GetBillsAsync(userId, cancellationToken);
        if (billsRead is not WalletResult<IReadOnlyList<WalletBill>>.Ok { Value: var bills })
            return await RefuseUnreadAsync(userId, billsRead, cancellationToken);

        IReadOnlyList<WalletProduct> products = [];
        if (string.Equals(request.BillerCode, BillResolver.InternetBiller, StringComparison.Ordinal))
        {
            var productsRead = await wallet.GetProductsAsync(null, null, null, cancellationToken);
            if (productsRead is not WalletResult<IReadOnlyList<WalletProduct>>.Ok { Value: var catalogue })
                return await RefuseUnreadAsync(userId, productsRead, cancellationToken);
            products = catalogue;
        }

        WalletBill bill;
        switch (BillResolver.Resolve(request, bills, products, options))
        {
            case BillResolution.Card card:
                bill = card.Bill;
                break;
            case BillResolution.Options choice:
                return await AskAsync(userId, QuestionKinds.WhichBill, choice.Bills.Select(BillOption).ToList(), cancellationToken);
            case BillResolution.AskBillOrStore billOrStore:
                return await AskAsync(
                    userId,
                    QuestionKinds.BillOrStore,
                    [BillOption(billOrStore.Bill), new Option(Option.StoreInternet, null, null, null, null, null)],
                    cancellationToken);
            case BillResolution.StoreInternet store:
                return await AskAsync(userId, QuestionKinds.WhichProduct, ProductOptions(store.InternetCards), cancellationToken);
            case BillResolution.NothingDue:
                return await RefuseAsync(userId, ReasonCodes.NothingDue, null, null, cancellationToken);
            case BillResolution.NotFound:
                return await RefuseAsync(userId, ReasonCodes.BillNotFound, null, null, cancellationToken);
            default:
                throw new InvalidOperationException("Unknown bill resolution.");
        }

        // D9: a bill is paid in full. An amount the user named must be the amount due, else no card.
        if (request.AmountValue is not null)
        {
            var named = AmountRule.Resolve(request, options) switch
            {
                AmountResolution.Dinars dinars => dinars.AmountIqd == bill.AmountDueIqd,
                AmountResolution.Ask ask => ask.Options.Contains(bill.AmountDueIqd),
                _ => false,
            };
            if (!named)
                return await RefuseAsync(userId, ReasonCodes.BillsPaidInFull, null, bill.AmountDueIqd, cancellationToken);
        }

        var subject = new CardSubject(BillType, null, null, bill.Label ?? bill.BillerName, null, bill.BillAccountId, bill.Code, null, null);
        var input = new CheckInput(userId, BillType, bill.AmountDueIqd, null, null, null, bill.BillAccountId, null, SkipFormat: true);
        return await CheckAndIssueAsync(userId, input, subject, cancellationToken);
    }

    /// <summary>Step 6: the product from the catalogue at the catalogue price, then the checks and the card.</summary>
    private async Task<AgentReply> PurchaseAsync(int userId, ParsedRequest request, CancellationToken cancellationToken)
    {
        var productsRead = await wallet.GetProductsAsync(null, null, null, cancellationToken);
        if (productsRead is not WalletResult<IReadOnlyList<WalletProduct>>.Ok { Value: var catalogue })
            return await RefuseUnreadAsync(userId, productsRead, cancellationToken);

        WalletProduct product;
        switch (ProductResolver.Resolve(request, catalogue, options))
        {
            case ProductResolution.Card card:
                product = card.Product;
                break;
            case ProductResolution.Options choice:
                return await AskAsync(userId, QuestionKinds.WhichProduct, ProductOptions(choice.Products), cancellationToken);
            case ProductResolution.AskOperator ask:
                var operatorOptions = ask.ProviderCodes
                    .Select(code => new Option(
                        code,
                        catalogue.First(p => string.Equals(p.Provider, code, StringComparison.OrdinalIgnoreCase)).ProviderNameAr,
                        null, null, null, null))
                    .ToList();
                return await AskAsync(userId, QuestionKinds.WhichOperator, operatorOptions, cancellationToken);
            case ProductResolution.NotFound:
                return await RefuseAsync(userId, ReasonCodes.ProductNotFound, null, null, cancellationToken);
            default:
                throw new InvalidOperationException("Unknown product resolution.");
        }

        var subject = new CardSubject(PurchaseType, null, null, product.ProviderNameAr, null, null, null, product.ProductId, product.Code);
        var input = new CheckInput(userId, PurchaseType, product.PriceIqd, null, null, null, null, product.ProductId, SkipFormat: true);
        return await CheckAndIssueAsync(userId, input, subject, cancellationToken);
    }

    /// <summary>Runs the ordered checks; on a pass builds the card and opens its pending record (step 7).</summary>
    private async Task<AgentReply> CheckAndIssueAsync(
        int userId, CheckInput input, CardSubject subject, CancellationToken cancellationToken)
    {
        WalletQuote quote;
        switch (await checks.RunAsync(input, cancellationToken))
        {
            case CheckResult.Pass pass:
                quote = pass.Quote;
                break;
            case CheckResult.Fail fail:
                return await RefuseAsync(userId, fail.Reason, fail.Detail, fail.AmountIqd, cancellationToken);
            default:
                throw new InvalidOperationException("Unknown check result.");
        }

        var built = await cards.BuildAsync(userId, quote, subject, clock.GetUtcNow(), cancellationToken);
        if (built is not WalletResult<CardDraft>.Ok { Value: var draft })
            return await RefuseUnreadAsync(userId, built, cancellationToken);

        var record = await store.CreateAsync(draft, cancellationToken);
        return new AgentReply.CardIssued(record);
    }

    private static Option BillOption(WalletBill bill)
        => new(bill.Code, bill.Label ?? bill.BillerName, null, null, null, bill.AmountDueIqd);

    private List<Option> ProductOptions(IReadOnlyList<WalletProduct> products)
        => products
            .Take(options.MaxProductsInOneQuestion)
            .Select(p => new Option(p.Code, p.ProviderNameAr, null, null, null, p.PriceIqd))
            .ToList();

    private async Task<AgentReply> AskAsync(int userId, string kind, IReadOnlyList<Option> choices, CancellationToken cancellationToken)
    {
        await audit.WriteAsync(userId, null, AuditEvents.QuestionAsked, null, null, kind, null, null, cancellationToken);
        return new AgentReply.Question(kind, QuestionKinds.AnswerFor(kind, choices.Count), choices);
    }

    /// <summary>A refusal and its audit row; the row keeps the code only (no detail, no figure).</summary>
    private async Task<AgentReply> RefuseAsync(
        int userId, string code, string? detail, long? amountIqd, CancellationToken cancellationToken)
    {
        await audit.WriteAsync(userId, null, AuditEvents.Refused, null, null, code, null, null, cancellationToken);
        return new AgentReply.Refusal(code, detail, amountIqd);
    }

    /// <summary>A wallet read that gave no usable answer: nothing was sent to execute, so nothing was taken.</summary>
    private Task<AgentReply> RefuseUnreadAsync<T>(int userId, WalletResult<T> result, CancellationToken cancellationToken)
    {
        var detail = result switch
        {
            WalletResult<T>.Unavailable unavailable => ReasonCodes.WalletDetailPrefix + unavailable.Reason,
            WalletResult<T>.Refused refused => refused.Problem.Code,
            _ => throw new InvalidOperationException("A usable wallet answer is not a refusal."),
        };
        return RefuseAsync(userId, ReasonCodes.NotDoneNothingTaken, detail, null, cancellationToken);
    }

    private async Task<AgentReply> ConsentAsync(int userId, CancellationToken cancellationToken)
    {
        await audit.WriteAsync(userId, null, AuditEvents.ConsentShown, null, null, null, null, null, cancellationToken);
        return new AgentReply.ConsentScreen();
    }
}
