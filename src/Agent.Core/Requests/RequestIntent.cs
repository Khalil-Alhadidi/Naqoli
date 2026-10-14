namespace Agent.Core.Requests;

/// <summary>What the user asked for, as the parse call names it.</summary>
public enum RequestIntent
{
    /// <summary>Send money to a person.</summary>
    Transfer,

    /// <summary>Pay one of the user's bills.</summary>
    Bill,

    /// <summary>Buy a product from the store.</summary>
    Purchase,

    /// <summary>The user's own balance; read only, no card and no tap.</summary>
    Balance,

    /// <summary>A request the agent recognises but does not do; <see cref="ParsedRequest.UnsupportedReason"/> says which.</summary>
    Unsupported,

    /// <summary>A request the parse call could not read.</summary>
    Unclear,
}
