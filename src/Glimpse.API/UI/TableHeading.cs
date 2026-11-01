namespace Glimpse.API.UI;

public struct TableHeading
{
    public string Title;

    public float Width;

    public TableHeading(string title, float width = 0)
    {
        Title = title;
        Width = width;
    }

    // yeah we're doing some EVIL shit right here but its mostly for convenience
    // can't wait for this to bite me in the ass
    public static implicit operator TableHeading(string title)
        => new TableHeading(title);
}