using Glimpse.API;
using OpenMPT.NET;

namespace Glimpse.OpenMPT;

public record struct MptConfig : IConfig
{
    public bool EmulateAmigaResampler;

    public Filter ResamplerFilter;

    public bool FadeOutAtEnd;

    public MptConfig()
    {
        EmulateAmigaResampler = true;
        ResamplerFilter = Filter.Default;
        FadeOutAtEnd = false;
    }
}