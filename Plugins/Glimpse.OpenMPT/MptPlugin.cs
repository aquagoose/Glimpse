using Glimpse.API;
using Glimpse.API.UI;
using OpenMPT.NET;

namespace Glimpse.OpenMPT;

public class MptPlugin : IPlugin
{
    private IAudioPlayer _player;
    private IConfigManager _configManager;
    private bool _initialized;

    private MptConfig _workingConfig;
    private MptCodec _codec;

    public bool IsInitialized => _initialized;

    public bool ConfigChanged => _workingConfig != _codec.Config;

    public void Initialize(IGlimpse glimpse)
    {
        _player = glimpse.Player;
        _configManager = glimpse.ConfigManager;
        
        if (!_configManager.TryGetConfig("MPT", out _workingConfig))
        {
            _workingConfig = new MptConfig();
            _configManager.WriteConfig("MPT", _workingConfig);
        }

        _codec = new MptCodec(_workingConfig);
        _player.RegisterCodec(_codec);

        _initialized = true;
    }

    public void OnGUI(IImmediateGUI ui)
    {
        ui.Checkbox("Emulate Amiga Resampler", ref _workingConfig.EmulateAmigaResampler, tooltip: "If enabled, the amiga resampler will be used for .mod files.\nOtherwise, the current Resampler Mode will be used.");
        //ui.Checkbox("Fade Out at End", ref _codec.Config.FadeOutAtEnd); todo this doesn't actually work

        int resamplerFilter = (int) _workingConfig.ResamplerFilter;
        if (ui.Dropdown("Resampler Mode", ref resamplerFilter, ["Default", "None", "Linear", "Cubic", "Sinc"], tooltip: "Change the resampling quality."))
            _workingConfig.ResamplerFilter = (Filter) resamplerFilter;
    }

    public void Dispose()
    {
        if (!_initialized)
            return;
        _initialized = false;
        
        _player.DeregisterCodec(_codec);
    }

    public void SaveConfigChanges()
    {
        _codec.Config = _workingConfig;
        _configManager.WriteConfig("MPT", _workingConfig);
    }

    public void CancelConfigChanges()
    {
        _workingConfig = _codec.Config;
    }
}