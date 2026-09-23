namespace DevTerm.Core.Presenters;

/// <summary>
/// Optional companion to <see cref="IPresenter"/>, the same "optional companion" shape
/// <see cref="IPresenterInput"/> already establishes: a decoder that tracks named live values
/// (keyed by a <c>UiControl.Id</c>) publishes them here in addition to its normal human-readable
/// <see cref="IPresenter.Render"/> text, so a generic control-panel renderer can drive live
/// indicators without parsing rendered text back out.
/// </summary>
public interface IStructuredPresenter
{
    event EventHandler<IReadOnlyDictionary<string, string>>? ValuesChanged;
}
