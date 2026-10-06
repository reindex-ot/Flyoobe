namespace Flyoobe3.Services;

//Pages opt into the one search box owned by the main window.
internal interface ISearchableView
{
    void ApplySearch(string text);
}
