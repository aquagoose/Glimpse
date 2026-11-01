namespace Glimpse.API;

[Flags]
public enum ContextMenuType
{
    Track = 1 << 0,
    
    Album = 1 << 1,
    
    All = Track | Album
}
