namespace FluentBlock.Models;

public sealed record RemovableDrive(
    string Letter,          // "E:"
    string Label)           // volume label or ""
{
    public string Display => string.IsNullOrEmpty(Label) ? Letter : $"{Letter} — {Label}";
}
