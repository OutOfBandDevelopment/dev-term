namespace DevTerm.Core.Control;

/// <summary>
/// Invokes a named command on a live device. Mirrors
/// <see cref="Presenters.IPresenterInput"/>'s "everything is text at the boundary" convention: one
/// string-valued parameter covers every <c>UiControl</c> kind a generic renderer can invoke
/// (button: <paramref name="value"/> null; toggle: "0"/"1"; slider/numeric: an
/// invariant-culture number string; choice: the selected option string). A <c>UiControl.Id</c>
/// is the command id, unless a control overrides it (e.g. <c>ButtonControl.CommandId</c>).
/// </summary>
public interface IControlSurface
{
    Task InvokeAsync(string commandId, string? value, CancellationToken cancellationToken = default);
}
