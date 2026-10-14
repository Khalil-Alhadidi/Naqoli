using System.Runtime.CompilerServices;

// Only the tests may call WalletClient.ExecuteAsync directly; in the app only the Confirm handler reaches it (rule 1).
[assembly: InternalsVisibleTo("Agent.Core.Tests")]
