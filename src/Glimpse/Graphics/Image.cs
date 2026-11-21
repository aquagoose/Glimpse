using Hexa.NET.ImGui;
using piko.SDL3;

namespace Glimpse.Graphics;

public unsafe class Image : IDisposable
{
    internal readonly SDL.Texture Texture;

    public nint ID => Texture.Handle;

    public uint Width => (uint) Texture.W;

    public uint Height => (uint) Texture.H;

    public ImTextureRef TexRef => new ImTextureRef(texId: ID);
    
    internal Image(SDL.Renderer renderer, byte[] data, uint width, uint height)
    {
        Texture = SDL.CreateTexture(renderer, SDL.PixelFormat.Rgba32, SDL.TextureAccess.Target, (int) width, (int) height);

        SDL.SetTextureBlendMode(Texture, SDL.BlendMode.Blend);
        SDL.SetTextureScaleMode(Texture, SDL.ScaleMode.Linear);

        uint pitch = width * 4; // 4 bytes per pixel
        fixed (byte* pData = data)
            SDL.UpdateTexture(Texture, null, (nint) pData, (int) pitch);
    }

    public void Dispose()
    {
        SDL.DestroyTexture(Texture);
    }
    
    public static implicit operator ImTextureRef(Image img)
        => img.TexRef;
}