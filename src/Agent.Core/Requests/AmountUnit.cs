namespace Agent.Core.Requests;

/// <summary>The unit the user gave with an amount.</summary>
public enum AmountUnit
{
    /// <summary>A bare number (matching rule 9 decides what it means).</summary>
    None,

    /// <summary>Dinars as written.</summary>
    Dinars,

    /// <summary>Thousands of dinars.</summary>
    Thousand,

    /// <summary>Millions of dinars.</summary>
    Million,
}
