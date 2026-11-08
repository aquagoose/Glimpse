#include "GLP/audio/GLP_player.h"

#include <Slant/Slant.h>
#include <Slant/Stream/AudioStream.h>
#include <Slant/Stream/Flac.h>
#include <SDL3/SDL.h>

#include <stdlib.h>

#define GLP_NUM_AUDIO_BUFFERS 2

typedef struct GLP_AudioPlayerImpl
{
    GLP_AudioPlayerInfo info;
    SlContext *context;
    SDL_AudioStream *device;

    SlAudioStream *stream;
    SlAudioSource source;
    uint8_t *streamBuffer;
    SlAudioBuffer buffers[GLP_NUM_AUDIO_BUFFERS];
} GLP_AudioPlayerImpl;

GLP_Result GLP_CreateAudioPlayer(const GLP_AudioPlayerInfo *info, GLP_AudioPlayer **player)
{
    GLP_AudioPlayerImpl *impl = (GLP_AudioPlayerImpl *) malloc(sizeof(GLP_AudioPlayerImpl));
    impl->info = *info;

    // seriously why did i design this API without returning results???????
    slCreateContext(info->sampleRate, &impl->context);

    // the device is only created when playback is started.
    impl->device = NULL;
    impl->stream = NULL;

    // set the buffer size to the sample rate / 2
    size_t bufferSize = info->sampleRate * 4;
    impl->streamBuffer = malloc(bufferSize);

    for (int i = 0; i < GLP_NUM_AUDIO_BUFFERS; i++)
        impl->buffers[i] = slContextCreateBuffer(impl->context, NULL, 0);

    *player = (GLP_AudioPlayer *) impl;
    return GLP_RESULT_OK;
}

GLP_Result GLP_DestroyAudioPlayer(GLP_AudioPlayer *player)
{
    GLP_AudioPlayerImpl *impl = (GLP_AudioPlayerImpl *) player;

    if (impl->device)
        SDL_DestroyAudioStream(impl->device);

    slDestroyContext(impl->context);
    free(impl->streamBuffer);
    free(impl);

    return GLP_RESULT_OK;
}

GLP_Result GLP_PlayTrack(GLP_AudioPlayer *player, const char *path)
{
    GLP_AudioPlayerImpl *impl = (GLP_AudioPlayerImpl *) player;
    // AGAIN WHY DID I NOT MAKE THIS SHIT RETURN RESULTS
    slStreamLoadFlac(path, &impl->stream);
    SlAudioFormat format = slStreamGetFormat(impl->stream);

    SlSourceDescription sourceInfo =
    {
        .Type = SL_SOURCE_TYPE_PCM,
        .Format = format
    };

    impl->source = slContextCreateSource(impl->context, &sourceInfo);
    

    return GLP_RESULT_OK;
}
