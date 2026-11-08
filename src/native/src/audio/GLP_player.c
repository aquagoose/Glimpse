#include "GLP/audio/GLP_player.h"

#include <Slant/Slant.h>
#include <Slant/Stream/AudioStream.h>
#include <Slant/Stream/Mp3.h>
#include <SDL3/SDL.h>

#include <assert.h>
#include <stdlib.h>

#define GLP_NUM_AUDIO_BUFFERS 2
#define GLP_MIX_BUFFER_SIZE 512

typedef struct GLP_AudioPlayerImpl
{
    GLP_AudioPlayerInfo info;
    SlContext *context;
    SDL_AudioStream *device;

    SlAudioStream *stream;
    SlAudioSource source;
    uint8_t *streamBuffer;
    size_t streamBufferSize;
    SlAudioBuffer buffers[GLP_NUM_AUDIO_BUFFERS];
} GLP_AudioPlayerImpl;

void GLP_MixAudioCallback(void *userdata, SDL_AudioStream *stream, int additional_amount, int total_amount)
{
    GLP_AudioPlayerImpl *impl = (GLP_AudioPlayerImpl *) userdata;

    float buffer[GLP_MIX_BUFFER_SIZE];
    while (additional_amount > 0)
    {
        int total = SDL_min(additional_amount, GLP_MIX_BUFFER_SIZE);
        slContextMixToStereoF32Buffer(impl->context, buffer, total / 4);
        SDL_PutAudioStreamData(stream, buffer, total);
        additional_amount -= total;
    }
}

GLP_Result GLP_CreateAudioPlayer(const GLP_AudioPlayerInfo *info, GLP_AudioPlayer **player)
{
    GLP_AudioPlayerImpl *impl = (GLP_AudioPlayerImpl *) malloc(sizeof(GLP_AudioPlayerImpl));
    impl->info = *info;

    // seriously why did i design this API without returning results???????
    slCreateContext(info->sampleRate, &impl->context);

    if (!SDL_Init(SDL_INIT_AUDIO))
    {
        slDestroyContext(impl->context);
        free(impl);
        return GLP_RESULT_UNKNOWN_ERROR;
    }

    // the device is only created when playback is started.
    impl->device = NULL;
    impl->stream = NULL;

    // set the buffer size to the sample rate / 2
    size_t bufferSize = info->sampleRate * 4;
    impl->streamBuffer = malloc(bufferSize);
    impl->streamBufferSize = bufferSize;

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
    SDL_Quit();

    slDestroyContext(impl->context);
    free(impl->streamBuffer);
    free(impl);

    return GLP_RESULT_OK;
}

GLP_Result GLP_PlayTrack(GLP_AudioPlayer *player, const char *path)
{
    GLP_AudioPlayerImpl *impl = (GLP_AudioPlayerImpl *) player;
    // AGAIN WHY DID I NOT MAKE THIS SHIT RETURN RESULTS
    slStreamLoadMp3(path, &impl->stream);
    SlAudioFormat format = slStreamGetFormat(impl->stream);

    SlSourceDescription sourceInfo =
    {
        .Type = SL_SOURCE_TYPE_PCM,
        .Format = format
    };

    impl->source = slContextCreateSource(impl->context, &sourceInfo);

    size_t bytesPerSample = 1;
    switch (format.DataType)
    {
        case SL_DATA_TYPE_I8:
        case SL_DATA_TYPE_U8:
            bytesPerSample = 1;
            break;
        case SL_DATA_TYPE_I16:
            bytesPerSample = 2;
            break;
        case SL_DATA_TYPE_I32:
        case SL_DATA_TYPE_F32:
            bytesPerSample = 4;
            break;
    }

    size_t bufferLength = (impl->info.sampleRate / 2) * bytesPerSample * format.Channels;
    assert(bufferLength <= impl->streamBufferSize);

    for (int i = 0; i < GLP_NUM_AUDIO_BUFFERS; i++)
    {
        size_t got = slStreamGetBuffer(impl->stream, impl->streamBuffer, bufferLength);
        if (got < bufferLength)
            break;
        slContextUpdateBuffer(impl->context, impl->buffers[i], impl->streamBuffer, got);
        slSourceSubmitBuffer(impl->context, impl->source, impl->buffers[i]);
    }

    SDL_AudioSpec spec =
    {
        .format = SDL_AUDIO_F32,
        .channels = 2,
        .freq = (int) impl->info.sampleRate
    };

    impl->device = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, GLP_MixAudioCallback, impl);
    if (!impl->device)
        return GLP_RESULT_UNKNOWN_ERROR;
    SDL_ResumeAudioStreamDevice(impl->device);

    slSourcePlay(impl->context, impl->source);

    return GLP_RESULT_OK;
}
