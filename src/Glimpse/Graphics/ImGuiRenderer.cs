using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Glimpse.Assets;
using Hexa.NET.ImGui;
using piko.SDL3;
using ImDrawIdx = ushort;

namespace Glimpse.Graphics;

public class ImGuiRenderer : IDisposable
{
    private readonly SDL.Renderer _renderer;
    private readonly List<SDL.FColor> _colorConvertCache;
    private readonly List<nint> _loadedFonts;
    
    private readonly ImGuiContextPtr _context;

    public ImGuiContextPtr ImGuiContext => _context;
    
    public unsafe ImGuiRenderer(SDL.Renderer renderer, Size size)
    {
        _renderer = renderer;
        _colorConvertCache = [];
        _loadedFonts = [];
        
        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);

        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new Vector2(size.Width, size.Height);
        //io.DisplayFramebufferScale = new Vector2(scale, scale);
        io.IniFilename = null;
        io.LogFilename = null;
        //io.Fonts.AddFontDefault();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasTextures;
    }

    public unsafe ImFontPtr AddFont(string path, uint size)
    {
        using Stream stream = Asset.GetAssetStream(path);
        // Allocate the stream as a native pointer.
        // Unlike before, the font atlas no longer owns the font data.
        // I found when loading large fonts (like Chinese) that it would not
        // load properly if the atlas owned the font data.
        // This code may seem a bit convoluted but it is to avoid:
        //   1. Allocating a memory stream
        //   2. Copying the existing stream to the new memory stream
        //   3. Converting the memory stream to a byte array (another allocation)
        //   4. Allocating the native memory array
        //   5. Copying that byte array to the native memory
        //   6. Hoping and praying that the GC cleans it up
        // The other alternative was GC handles. But I prefered this method:
        long streamSize = stream.Length;
        byte* pStream = (byte*) NativeMemory.Alloc((nuint) streamSize);
        Span<byte> pStreamSpan = new Span<byte>(pStream, (int) streamSize);
        stream.ReadExactly(pStreamSpan); // Load the entire stream into the native memory
        _loadedFonts.Add((nint) pStream);
        
        ImFontAtlasPtr fonts = ImGui.GetIO().Fonts;
        
        ImFontConfig config = new()
        {
            MergeMode = (byte) (fonts.Fonts.Size > 0 ? 1 : 0),
            FontDataOwnedByAtlas = 0,
            RasterizerDensity = 1,
            RasterizerMultiply = 1,
            GlyphMaxAdvanceX = float.MaxValue,
        };

        ImFontPtr font = fonts.AddFontFromMemoryTTF(pStream, (int) streamSize, size, &config);
        
        return font;
    }

    public unsafe void SetDefaultFont(ImFontPtr font)
    {
        ImGui.GetIO().FontDefault = font;
    }

    internal unsafe void Render()
    {
        ImGui.SetCurrentContext(_context);
        
        ImGui.Render();
        ImDrawDataPtr drawData = ImGui.GetDrawData();

        ref ImVector<ImTextureDataPtr> textures = ref drawData.Textures;
        for (int i = 0; i < textures.Size; i++)
        {
            ImTextureDataPtr texture = textures[i];
            if (texture.Status != ImTextureStatus.Ok)
                UpdateTexture(texture);
        }

        SDL.SetRenderViewport(_renderer, null);

        Vector2 clipOff = drawData.DisplayPos;
        int fbWidth = (int) drawData.DisplaySize.X;
        int fbHeight = (int) drawData.DisplaySize.Y;

        for (int i = 0; i < drawData.CmdListsCount; i++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[i];

            ImDrawVert* vertexBuffer = cmdList.VtxBuffer.Data;
            ImDrawIdx* indexBuffer = cmdList.IdxBuffer.Data;

            for (int j = 0; j < cmdList.CmdBuffer.Size; j++)
            {
                ImDrawCmd drawCmd = cmdList.CmdBuffer[j];
                
                if (drawCmd.UserCallback != null)
                    continue;

                Vector2 clipMin = new Vector2(drawCmd.ClipRect.X - clipOff.X, drawCmd.ClipRect.Y - clipOff.Y);
                Vector2 clipMax = new Vector2(drawCmd.ClipRect.Z - clipOff.X, drawCmd.ClipRect.W - clipOff.Y);

                clipMin = Vector2.Clamp(clipMin, Vector2.Zero, new Vector2(fbWidth, fbHeight));
                clipMax = Vector2.Clamp(clipMax, Vector2.Zero, new Vector2(fbWidth, fbHeight));

                if (clipMax.X <= clipMin.X || clipMax.Y <= clipMin.Y)
                    continue;

                SDL.Rect clipRect = new SDL.Rect((int) clipMin.X, (int) clipMin.Y, (int) (clipMax.X - clipMin.X), (int) (clipMax.Y - clipMin.Y));
                SDL.SetRenderClipRect(_renderer, &clipRect);

                int numVertices = (int) (cmdList.VtxBuffer.Size - drawCmd.VtxOffset);
                int numIndices = (int) drawCmd.ElemCount;

                SDL.Texture texture = new SDL.Texture((SDL.TextureRef*) drawCmd.GetTexID());
                float* xy = (float*) (((byte*) vertexBuffer + drawCmd.VtxOffset) + 0);
                float* uv = (float*) (((byte*) vertexBuffer + drawCmd.VtxOffset) + 8);

                _colorConvertCache.Clear();
                _colorConvertCache.EnsureCapacity(numVertices);
                for (int c = 0; c < numVertices; c++)
                {
                    SDL.Color* color = (SDL.Color*) (((byte*) vertexBuffer + drawCmd.VtxOffset + (c * sizeof(ImDrawVert) + 16)));
                    _colorConvertCache.Add(new SDL.FColor
                    {
                        R = color->R / (float) byte.MaxValue,
                        G = color->G / (float) byte.MaxValue,
                        B = color->B / (float) byte.MaxValue,
                        A = color->A / (float) byte.MaxValue
                    });
                }

                fixed (SDL.FColor* col = CollectionsMarshal.AsSpan(_colorConvertCache))
                {
                    SDL.RenderGeometryRaw(_renderer, texture, xy, sizeof(ImDrawVert), col, sizeof(SDL.FColor), uv,
                        sizeof(ImDrawVert), numVertices, (nint) (indexBuffer + drawCmd.IdxOffset), numIndices, sizeof(ImDrawIdx));
                }
            }
        }
    }

    internal void Resize(in Size size)
    {
        ImGui.GetIO().DisplaySize = new Vector2(size.Width, size.Height);
    }
    
    /*private unsafe void RecreateFontTexture()
    {
        if (_gl.IsTexture(_imGuiTexture))
            _gl.DeleteTexture(_imGuiTexture);

        ImGuiIOPtr io = ImGui.GetIO();
        byte* pixels;
        int width, height;
        io.Fonts.GetTexDataAsRGBA32(&pixels, &width, &height);

        _imGuiTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _imGuiTexture);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint) width, (uint) height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int) TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int) TextureMagFilter.Linear);

        //_gl.GenerateMipmap(TextureTarget.Texture2D);
        
        io.Fonts.SetTexID(_imGuiTexture);
    }*/

    private unsafe void UpdateTexture(ImTextureDataPtr textureData)
    {
        Console.WriteLine(textureData.Status);
        
        switch (textureData.Status)
        {
            case ImTextureStatus.WantCreate:
            {
                SDL.Texture texture = SDL.CreateTexture(_renderer, SDL.PixelFormat.Rgba32, SDL.TextureAccess.Static, textureData.Width, textureData.Height);
                SDL.SetTextureBlendMode(texture, SDL.BlendMode.Blend);
                SDL.SetTextureScaleMode(texture, SDL.ScaleMode.Linear);

                SDL.UpdateTexture(texture, null, (nint) textureData.GetPixels(), textureData.GetPitch());

                textureData.TexID = texture.Handle;
                textureData.Status = ImTextureStatus.Ok;
                
                break;
            }
            
            case ImTextureStatus.WantUpdates:
            {
                ref ImVector<ImTextureRect> updates = ref textureData.Updates; 
                for (int i = 0; i < updates.Size; i++)
                {
                    ImTextureRect r = updates[i];
                    SDL.Rect rect = new SDL.Rect(r.X, r.Y, r.W, r.H);
                    SDL.UpdateTexture(new SDL.Texture((SDL.TextureRef*) textureData.TexID), &rect, (nint) textureData.GetPixelsAt(r.X, r.Y), textureData.GetPitch());
                }

                textureData.Status = ImTextureStatus.Ok;
                break;
            }

            case ImTextureStatus.WantDestroy:
            {
                SDL.DestroyTexture(new SDL.Texture((SDL.TextureRef*) textureData.TexID));
                textureData.TexID = ImTextureID.Null;
                textureData.Status = ImTextureStatus.Destroyed;
                break;
            }
            
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
    
    public unsafe void Dispose()
    {
        ref ImVector<ImTextureDataPtr> textures = ref ImGui.GetPlatformIO().Textures;
        for (int i = 0; i < textures.Size; i++)
        {
            textures[i].Status = ImTextureStatus.Destroyed;
            UpdateTexture(textures[i]);
        }
        
        foreach (nint font in _loadedFonts)
            NativeMemory.Free((void*) font);
        
        ImGui.DestroyContext(_context);
    }
}