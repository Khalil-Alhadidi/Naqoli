using Agent.Web.Resources;
using Agent.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Agent.Web.Components.Phone;

/// <summary>
/// A phone component that renders the circuit's <see cref="ChatSession"/>: it re-renders whenever the session changes
/// (the event may come from any thread, so it goes through <see cref="ComponentBase.InvokeAsync(Action)"/>).
/// </summary>
public abstract class SessionComponent : ComponentBase, IDisposable
{
    [Inject]
    protected ChatSession Session { get; set; } = default!;

    [Inject]
    protected IStringLocalizer<PhoneStrings> Text { get; set; } = default!;

    protected override void OnInitialized() => Session.Changed += OnSessionChanged;

    public void Dispose()
    {
        Session.Changed -= OnSessionChanged;
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    private void OnSessionChanged() => _ = InvokeAsync(StateHasChanged);
}
