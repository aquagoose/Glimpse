#include "GLP/audio/GLP_player.h"

#include <Slant/Slant.h>
#include <Slant/Stream/AudioStream.h>
#include <Slant/Stream/Flac.h>
#include <SDL3/SDL.h>

#include <assert.h>
#include <stdlib.h>

#define GLP_NUM_AUDIO_BUFFERS 2
#define GLP_MIX_BUFFER_SIZE 512
#define GLP_INIT_QUEUE_SIZE 16

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
    int currentBuffer;

    size_t currentTrackIndex;
    const char **playQueue;
    size_t *playOrder;
    size_t queueLength;
    size_t queueCapacity;
} GLP_AudioPlayerImpl;

static void GLP_MixAudioCallback(void *userdata, SDL_AudioStream *stream, int additional_amount, int total_amount)
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

static void GLP_BufferFinishedCallback(void *userData)
{
    GLP_AudioPlayerImpl *impl = (GLP_AudioPlayerImpl *) userData;
    size_t got = slStreamGetBuffer(impl->stream, impl->streamBuffer, impl->streamBufferSize);

    if (got == 0)
        return;
    if (got < impl->streamBufferSize)
        slSourceSetLooping(impl->context, impl->source, false);

    slContextUpdateBuffer(impl->context, impl->buffers[impl->currentBuffer], impl->streamBuffer, got);
    slSourceSubmitBuffer(impl->context, impl->source, impl->buffers[impl->currentBuffer]);

    impl->currentBuffer = (impl->currentBuffer + 1) % GLP_NUM_AUDIO_BUFFERS;
}

static void GLP_StopPlayback(GLP_AudioPlayerImpl* impl)
{
    if (!impl->stream)
        return;

    SDL_DestroyAudioStream(impl->device);
    impl->device = NULL;

    // destroying the source will immediately stop playback
    slContextDestroySource(impl->context, impl->source);
    slDestroyStream(impl->stream);

    impl->currentTrackIndex = 0;
    impl->queueLength = 0;
}

static GLP_Result GLP_NextTrack(GLP_AudioPlayerImpl* impl)
{
    GLP_Result result = GLP_PlayTrack((GLP_AudioPlayer *) impl, ++impl->currentTrackIndex);
    if (result != GLP_RESULT_OK)
        GLP_StopPlayback(impl);

    return result;
}

static void GLP_SourceStateChangedCallback(SlSourceState state, void *userData)
{
    GLP_AudioPlayerImpl *impl = (GLP_AudioPlayerImpl *) userData;
    if (state == SL_SOURCE_STATE_STOPPED)
        GLP_NextTrack(impl);
}

GLP_Result GLP_CreateAudioPlayer(const GLP_AudioPlayerInfo *info, GLP_AudioPlayer **player)
{
    GLP_AudioPlayerImpl *impl = (GLP_AudioPlayerImpl *) malloc(sizeof(GLP_AudioPlayerImpl));
    if (!impl)
        return GLP_RESULT_OUT_OF_MEMORY;

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
    if (!impl->streamBuffer)
    {
        slDestroyContext(impl->context);
        free(impl);
        return GLP_RESULT_OUT_OF_MEMORY;
    }

    impl->currentBuffer = 0;
    for (int i = 0; i < GLP_NUM_AUDIO_BUFFERS; i++)
        impl->buffers[i] = slContextCreateBuffer(impl->context, NULL, 0);

    impl->currentTrackIndex = 0;
    impl->playQueue = (const char **) malloc(GLP_INIT_QUEUE_SIZE * sizeof(const char *));
    impl->playOrder = (size_t *) malloc(GLP_INIT_QUEUE_SIZE * sizeof(size_t));
    impl->queueLength = 0;
    impl->queueCapacity = GLP_INIT_QUEUE_SIZE;

    *player = (GLP_AudioPlayer *) impl;
    return GLP_RESULT_OK;
}

GLP_Result GLP_DestroyAudioPlayer(GLP_AudioPlayer *player)
{
    GLP_AudioPlayerImpl *impl = (GLP_AudioPlayerImpl *) player;
    // stop playback and destroy all the playback objects
    GLP_StopPlayback(impl);

    if (impl->device)
        SDL_DestroyAudioStream(impl->device);
    SDL_Quit();

    slDestroyContext(impl->context);
    free(impl->streamBuffer);
    free(impl);

    return GLP_RESULT_OK;
}

GLP_PlaybackState GLP_GetPlaybackState(GLP_AudioPlayer *player)
{
    GLP_AudioPlayerImpl *impl = (GLP_AudioPlayerImpl *) player;
    if (!impl->stream)
        return GLP_PLAYBACK_STATE_STOPPED;

    SlSourceState sourceState = slSourceGetState(impl->context, impl->source);
    switch (sourceState)
    {
        case SL_SOURCE_STATE_STOPPED:
            return GLP_PLAYBACK_STATE_STOPPED;
        case SL_SOURCE_STATE_PAUSED:
            return GLP_PLAYBACK_STATE_PAUSED;
        case SL_SOURCE_STATE_PLAYING:
            return GLP_PLAYBACK_STATE_PLAYING;
    }

    return GLP_PLAYBACK_STATE_STOPPED;
}

GLP_Result GLP_QueueTrack(GLP_AudioPlayer *player, const char *path)
{
    GLP_AudioPlayerImpl *impl = (GLP_AudioPlayerImpl *) player;

    // resize the queues if necessary
    if (impl->queueLength + 1 > impl->queueCapacity)
    {
        size_t newCapacity = impl->queueCapacity << 1;
        const char **newPlayQueue = (const char **) realloc(impl->playQueue, newCapacity * sizeof(const char *));
        size_t *newPlayOrder = (size_t *) realloc(impl->playOrder, newCapacity * sizeof(size_t));

        if (!newPlayQueue || !newPlayOrder)
            return GLP_RESULT_OUT_OF_MEMORY;

        impl->playQueue = newPlayQueue;
        impl->playOrder = newPlayOrder;
        impl->queueCapacity = newCapacity;
    }

    size_t index = impl->queueLength++;
    impl->playQueue[index] = path;
    impl->playOrder[index] = index;

    return GLP_RESULT_OK;
}

GLP_Result GLP_PlayTrack(GLP_AudioPlayer *player, size_t index)
{
    GLP_AudioPlayerImpl *impl = (GLP_AudioPlayerImpl *) player;

    if (index >= impl->queueLength)
        return GLP_RESULT_INDEX_OUT_OF_RANGE;

    const char *path = impl->playQueue[impl->playOrder[index]];

    // AGAIN WHY DID I NOT MAKE THIS SHIT RETURN RESULTS
    slStreamLoadFlac(path, &impl->stream);
    SlAudioFormat format = slStreamGetFormat(impl->stream);

    SlSourceDescription sourceInfo =
    {
        .Type = SL_SOURCE_TYPE_PCM,
        .Format = format
    };

    impl->source = slContextCreateSource(impl->context, &sourceInfo);
    slSourceSetBufferFinishedCallback(impl->context, impl->source, GLP_BufferFinishedCallback, impl);
    slSourceSetStateChangedCallback(impl->context, impl->source, GLP_SourceStateChangedCallback, impl);

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
    impl->streamBufferSize = bufferLength;

    for (int i = 0; i < GLP_NUM_AUDIO_BUFFERS; i++)
    {
        size_t got = slStreamGetBuffer(impl->stream, impl->streamBuffer, bufferLength);
        if (got < bufferLength)
            break;
        slContextUpdateBuffer(impl->context, impl->buffers[i], impl->streamBuffer, got);
        slSourceSubmitBuffer(impl->context, impl->source, impl->buffers[i]);
    }

    impl->currentBuffer = 0;

    if (!impl->device)
    {
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
    }

    slSourcePlay(impl->context, impl->source);

    return GLP_RESULT_OK;
}
