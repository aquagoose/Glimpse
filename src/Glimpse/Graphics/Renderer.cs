using System.Diagnostics;
using System.Numerics;
using Glimpse.Assets;
using piko.SDL3;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = System.Drawing.Color;
using Size = System.Drawing.Size;

namespace Glimpse.Graphics;

public unsafe class Renderer : IDisposable
{
    private readonly SDL.Renderer _renderer;

    public readonly ImGuiRenderer ImGui;
    
    public Renderer(SDL.Window window, Size size)
    {
        _renderer = SDL.CreateRenderer(window, null);
        ImGui = new ImGuiRenderer(_renderer, size);
    }

    public Image CreateImage(byte[] data, uint width, uint height)
    {
        return new Image(_renderer, data, width, height);
    }

    public Image CreateImage(string path)
    {
        Stream stream;
        if (path.StartsWith("asset://"))
            stream = Asset.GetAssetStream(path["asset://".Length..]);
        else
            stream = File.OpenRead(path);
        
        using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
        byte[] pixels = new byte[image.Width * image.Height * sizeof(Rgba32)];
        image.CopyPixelDataTo(pixels);
        
        stream.Dispose();
        return new Image(_renderer, pixels, (uint) image.Width, (uint) image.Height);
    }

    public Image CreateImage(byte[] data, ImageLoadFlags flags = ImageLoadFlags.None)
    {
        using Image<Rgba32> image = SixLabors.ImageSharp.Image.Load<Rgba32>(data);
        
        if ((flags & ImageLoadFlags.LoadGrayscale) != 0)
            image.Mutate(x => x.Grayscale());
        
        byte[] pixels = new byte[image.Width * image.Height * sizeof(Rgba32)];
        image.CopyPixelDataTo(pixels);
        
        return new Image(_renderer, pixels, (uint) image.Width, (uint) image.Height);
    }

    public void Clear(Color color)
    {
        SDL.SetRenderDrawColor(_renderer, color.R, color.G, color.B, color.A);
        SDL.RenderClear(_renderer);
    }

    public void DrawImage(Image image, Vector2 position, Size size, Color tint)
    {
        SDL.FRect dest = new SDL.FRect(position.X, position.Y, position.X + size.Width, position.Y + size.Height);
        SDL.RenderTexture(_renderer, image.Texture, null, &dest);
    }

    public void DrawRectangle(Color color, Vector2 postion, Size size)
    {
        SDL.SetRenderDrawColor(_renderer, color.R, color.G, color.B, color.A);
        SDL.FRect rect = new SDL.FRect(postion.X, postion.Y, postion.X + size.Width, postion.Y + size.Height);
        SDL.RenderFillRect(_renderer, &rect);
    }

    public void Present(bool vsync)
    {
        Debug.Assert(vsync == true); // vsync cannot be false right now
        SDL.RenderPresent(_renderer);
    }

    public void Resize(Size size)
    {
        ImGui.Resize(size);
    }
    
    public void Dispose()
    {
        SDL.DestroyRenderer(_renderer);
    }

    public const string ShaderAssemblyBase = "Glimpse.Graphics.Shaders.";
}